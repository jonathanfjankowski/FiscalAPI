using Fiscal.Adapters.Unimake;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Fiscal.Persistence.Criptografia;
using Fiscal.Persistence.Repositories;
using Fiscal.Worker.Fila;
using Fiscal.Worker.Jobs;
using Fiscal.Worker.Delivery;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

var connStr = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
builder.Services.AddDbContext<FiscalDbContext>(opt => opt.UseNpgsql(connStr));

builder.Services.AddSingleton<ICertificadoStore, EnvelopeEncryptionService>();
builder.Services.AddSingleton<MetricasFiscais>();

// --- OpenTelemetry (métricas de negócio) ---
// Worker não tem endpoint HTTP: as métricas saem por OTLP quando
// Fiscal:Observabilidade:OtlpEndpoint está configurado; sem ele, são no-ops.
var builderOtel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("FiscalAPI.Worker", serviceVersion: MetricasFiscais.VersaoServico))
    .WithMetrics(m =>
    {
        m.AddRuntimeInstrumentation();
        m.AddMeter(MetricasFiscais.NomeMedidor);
        var otlp = builder.Configuration["Fiscal:Observabilidade:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlp))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    });
builder.Services.AddScoped<IRepositorioDocumentoFiscal, RepositorioDocumentoFiscal>();
builder.Services.AddScoped<IRepositorioCertificado, RepositorioCertificado>();
builder.Services.AddScoped<IRepositorioTenant, RepositorioTenant>();
builder.Services.AddScoped<IRepositorioAuditoria, RepositorioAuditoria>();
builder.Services.AddScoped<IRepositorioEventoFiscal, RepositorioEventoFiscal>();
builder.Services.AddScoped<IRepositorioNotaRecebida, RepositorioNotaRecebida>();
builder.Services.AddScoped<IRepositorioManifestacao, RepositorioManifestacao>();
builder.Services.AddScoped<IRepositorioNsu, RepositorioNsu>();

// ModoSandbox=true no Worker também registra EmissorMock (caso queira rodar
// ponta-a-ponta sem certificado real). Default: false (produção).
var modoSandbox = builder.Configuration.GetValue("Fiscal:ModoSandbox", false);
if (modoSandbox)
{
    builder.Services.AddSingleton<IEmissorFiscal, EmissorMock>();
}
builder.Services.AddSingleton<IEmissorFiscal, EmissorNFe>();
builder.Services.AddSingleton<IEmissorFiscal, EmissorNFCe>();
builder.Services.AddSingleton<IEmissorFiscal, EmissorNFSe>();

// Transmissores de eventos: mesma regra do emissor (sandbox → mock).
if (modoSandbox)
{
    builder.Services.AddSingleton<ITransmissorEventoFiscal, TransmissorEventoMock>();
}
builder.Services.AddSingleton<ITransmissorEventoFiscal, TransmissorEventoUnimake>();

// Distribuição DFe + manifestação: mesma regra do emissor (sandbox → mock).
if (modoSandbox)
{
    builder.Services.AddSingleton<IConsultaDistribuicaoDfe, ConsultaDistribuicaoMock>();
    builder.Services.AddSingleton<ITransmissorManifestacao, TransmissorManifestacaoMock>();
}
builder.Services.AddSingleton<IConsultaDistribuicaoDfe, ConsultaDistribuicaoUnimake>();
// Consulta de protocolo (recuperação de timeout/contingência): única.
if (modoSandbox)
{
    builder.Services.AddSingleton<IConsultaProtocolo, ConsultaProtocoloMock>();
}
else
{
    builder.Services.AddSingleton<IConsultaProtocolo, ConsultaProtocoloUnimake>();
}

// Status de serviço: implementação única (mock em sandbox, Unimake em produção).
if (modoSandbox)
{
    builder.Services.AddSingleton<IConsultaStatusServico, ConsultaStatusServicoMock>();
}
else
{
    builder.Services.AddSingleton<IConsultaStatusServico, ConsultaStatusServicoUnimake>();
}
builder.Services.AddSingleton<ITransmissorManifestacao, TransmissorManifestacaoUnimake>();

// Entrega de webhooks (outbox) — HTTP assinado com HMAC.
builder.Services.AddSingleton<IDespachanteWebhook, DespachanteWebhookHttp>();

builder.Services.AddScoped<ProcessarDocumentoJob>();
builder.Services.AddScoped<VarrerContingenciaJob>();
builder.Services.AddScoped<ProcessarEventoJob>();
builder.Services.AddScoped<VarrerEventosJob>();
builder.Services.AddScoped<ProcessarWebhookJob>();
builder.Services.AddScoped<VarrerWebhooksJob>();
builder.Services.AddScoped<SincronizarDistribuicaoDFeJob>();
builder.Services.AddScoped<ProcessarManifestacaoJob>();
builder.Services.AddScoped<VarrerManifestacoesJob>();
builder.Services.AddScoped<AlertarCertificadosVencendoJob>();
builder.Services.AddSingleton<IFilaEmissao, HangfireFilaEmissao>();

builder.Services.AddHangfire(cfg => cfg
    .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connStr))
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings());
builder.Services.AddHangfireServer();

var host = builder.Build();

// Job recorrente: varre CONTINGENCIA e reenfileira (a cada 30s).
RecurringJob.AddOrUpdate<VarrerContingenciaJob>(
    "varrer-contingencia",
    j => j.ExecutarAsync(CancellationToken.None),
    "*/30 * * * * *");

// Job recorrente: reenfileira eventos (cancelamento/CC-e/inutilização) cujo
// retry de transmissão venceu (a cada 30s).
RecurringJob.AddOrUpdate<VarrerEventosJob>(
    "varrer-eventos",
    j => j.ExecutarAsync(CancellationToken.None),
    "*/30 * * * * *");

// Job recorrente: entrega webhooks da outbox cujo retry venceu (a cada 60s).
RecurringJob.AddOrUpdate<VarrerWebhooksJob>(
    "varrer-webhooks",
    j => j.ExecutarAsync(CancellationToken.None),
    "*/60 * * * * *");

// Job recorrente: distribuição DFe (NSU) por tenant/ambiente (a cada 60s).
RecurringJob.AddOrUpdate<SincronizarDistribuicaoDFeJob>(
    "sincronizar-distribuicao-dfe",
    j => j.ExecutarAsync(CancellationToken.None),
    "*/60 * * * * *");

// Job recorrente: retry de manifestações pendentes (a cada 30s).
RecurringJob.AddOrUpdate<VarrerManifestacoesJob>(
    "varrer-manifestacoes",
    j => j.ExecutarAsync(CancellationToken.None),
    "*/30 * * * * *");

// Job recorrente: alerta de certificados vencendo (diário, 12:00).
RecurringJob.AddOrUpdate<AlertarCertificadosVencendoJob>(
    "alertar-certificados-vencendo",
    j => j.ExecutarAsync(CancellationToken.None),
    "0 0 12 * * *");

host.Run();

namespace Fiscal.Worker
{
    public class WorkerHeartbeat(ILogger<WorkerHeartbeat> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Mantido por compatibilidade; o host agora é dominado pelo HangfireServer.
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker heartbeat at {time}", DateTimeOffset.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }
    }
}
