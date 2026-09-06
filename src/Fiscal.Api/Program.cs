using System.Threading.RateLimiting;
using Fiscal.Adapters.Unimake;
using Fiscal.Api.Authentication;
using Fiscal.Api.Infrastructure;
using Fiscal.Api.Validators;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Fiscal.Pdf;
using Fiscal.Persistence.Criptografia;
using Fiscal.Persistence.Repositories;
using Fiscal.Worker.Fila;
using Fiscal.Worker.Jobs;
using Fiscal.Worker.Delivery;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog (JSON no stdout) ---
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

// --- Banco de dados ---
// Provider decidido a partir da connection string em runtime (lê a config
// do IServiceProvider, que já viu os overrides do WebApplicationFactory).
var connStr = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
builder.Services.AddDbContext<FiscalDbContext>((sp, opt) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var cs = cfg.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    if (cs.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
        cs.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
    {
        opt.UseSqlite(cs);
    }
    else
    {
        opt.UseNpgsql(cs);
    }
    // Migrations deste projeto são hand-written (sem Designer/TargetModel) — o
    // validador do EF 10 acha que há "pending model changes" e interrompe o
    // Migrate(). O snapshot em Migrations/ é mantido em dia manualmente.
    opt.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// --- Criptografia de certificados ---
builder.Services.AddSingleton<ICertificadoStore, EnvelopeEncryptionService>();

// --- Repositórios ---
builder.Services.AddScoped<IRepositorioTenant, RepositorioTenant>();
builder.Services.AddScoped<IRepositorioApiKey, RepositorioApiKey>();
builder.Services.AddScoped<IRepositorioCertificado, RepositorioCertificado>();
builder.Services.AddScoped<IRepositorioDocumentoFiscal, RepositorioDocumentoFiscal>();
builder.Services.AddScoped<IRepositorioAuditoria, RepositorioAuditoria>();
builder.Services.AddScoped<IRepositorioEventoFiscal, RepositorioEventoFiscal>();
builder.Services.AddScoped<IRepositorioNotaRecebida, RepositorioNotaRecebida>();
builder.Services.AddScoped<IRepositorioManifestacao, RepositorioManifestacao>();
builder.Services.AddScoped<IRepositorioNsu, RepositorioNsu>();

// --- Validador de consistência (puro, sem deps externas) ---
builder.Services.AddSingleton<ValidadorConsistenciaFiscal>();
builder.Services.AddSingleton<ValidadorImpostosV2>();

// --- PDF (DANFE/DANFCe via QuestPDF) ---
builder.Services.AddSingleton<IGeradorPdf, GeradorPdfQuestPdf>();

// --- Emissores fiscais ---
// Em ModoSandbox=true (default em dev), todos os tipos caem no mock.
// Em produção, registra os adapters reais. Em Testing, sempre mock (a
// integração real exige certificado A1 + SEFAZ no ar).
var modoSandbox = builder.Environment.IsEnvironment("Testing")
    || builder.Configuration.GetValue("Fiscal:ModoSandbox", true);
if (modoSandbox)
{
    builder.Services.AddSingleton<IEmissorFiscal, EmissorMock>();
}
builder.Services.AddSingleton<IEmissorFiscal, EmissorNFe>();
builder.Services.AddSingleton<IEmissorFiscal, EmissorNFCe>();
builder.Services.AddSingleton<IEmissorFiscal, EmissorNFSe>();

// --- Transmissores de eventos (cancelamento/CC-e/inutilização) ---
// Mesma regra do emissor: sandbox → mock; produção → Unimake.
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

// --- Despacho de webhooks (outbox) ---
// A entrega real (HTTP assinado) roda no Worker; aqui fica registrado para
// que testes de integração consigam executar o ProcessarWebhookJob via DI.
builder.Services.AddSingleton<IDespachanteWebhook, DespachanteWebhookHttp>();

// --- Fila assíncrona (Hangfire) ---
// A API só registra o cliente; o HangfireServer (quem executa) roda no Fiscal.Worker.
builder.Services.AddHangfire(cfg => cfg
    .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connStr))
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings());
builder.Services.AddSingleton<IFilaEmissao, HangfireFilaEmissao>();

// ProcessarDocumentoJob também fica registrado aqui para que testes de integração
// possam executá-lo via DI. Em produção, quem de fato executa é o Fiscal.Worker.
builder.Services.AddScoped<ProcessarDocumentoJob>();
builder.Services.AddScoped<ProcessarEventoJob>();
builder.Services.AddScoped<ProcessarWebhookJob>();
builder.Services.AddScoped<ProcessarManifestacaoJob>();
builder.Services.AddScoped<SincronizarDistribuicaoDFeJob>();
builder.Services.AddScoped<AlertarCertificadosVencendoJob>();
builder.Services.AddMemoryCache();

// --- Auth ---
// Duas frentes: ApiKey (tenants, endpoints /v1/*) e JWT Bearer (painel admin,
// endpoints /v1/admin/* + dashboard do Hangfire).
builder.Services.AddAuthentication(ApiKeyAuthenticationOptions.SchemeName)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.SchemeName, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opt =>
    {
        opt.MapInboundClaims = false;
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = AdminTokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = AdminTokenService.Audience,
            ValidateIssuerSigningKey = true,
            // Lazy: o segredo pode não estar configurado no startup (produção
            // sem painel). A falha acontece quando um JWT é apresentado, não aqui.
            IssuerSigningKeyResolver = (_, _, _, _) =>
                new[] { AdminTokenService.ResolveSigningKey(builder.Configuration) },
            ValidateLifetime = true,
            RoleClaimType = "role",
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // Dashboard do Hangfire aberto pelo browser do painel: token via query string.
        // MapHangfireDashboard usa Map(), que remove o prefixo do Path dentro do
        // branch — para /hangfire exato, Path chega vazio; por isso consideramos
        // PathBase + Path.
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var fullPath = (ctx.Request.PathBase.Value ?? "") + (ctx.Request.Path.Value ?? "");
                if (!string.IsNullOrEmpty(token)
                    && fullPath.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddSingleton<IAdminTokenService, AdminTokenService>();
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole("admin")));

// --- CORS (painel admin em dev via Vite; em prod o SPA é servido pela própria API) ---
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins("http://localhost:5173", "http://localhost:4173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// --- Controllers + Rate Limiting (in-memory por enquanto; Redis entra na F3.1) ---
builder.Services.AddControllers();
builder.Services.AddValidatorsFromAssemblyContaining<EmissaoRequestValidator>();
var limitePorMinuto = builder.Configuration.GetValue("Fiscal:RateLimit:PorMinuto", 100);
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Global por IP (era registrado mas nunca aplicado — faltava GlobalLimiter).
    opt.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = limitePorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    opt.AddFixedWindowLimiter("api", o =>
    {
        o.PermitLimit = limitePorMinuto;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
});

// --- Health checks ---
var healthConn = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddHealthChecks()
    .AddNpgSql(healthConn, name: "postgres", tags: new[] { "ready" });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

// --- SPA do painel admin (frontend/dist copiado para wwwroot) ---
// Em dev puro (sem wwwroot/index.html) o painel roda no servidor do Vite.
var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
if (File.Exists(spaIndex))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Dashboard do Hangfire só em Development/Production — em Testing ele exige
// schema Postgres que o SQLite do WebApplicationFactory não tem.
// Exige JWT de admin (header Authorization ou ?access_token=).
if (!app.Environment.IsEnvironment("Testing"))
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAdminDashboardFilter() }
    });
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
    await db.Database.MigrateAsync();
}

// --- Seed do primeiro admin (idempotente; exige ADMIN_EMAIL/ADMIN_PASSWORD) ---
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    var adminEmail = app.Configuration["ADMIN_EMAIL"];
    var adminSenha = app.Configuration["ADMIN_PASSWORD"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminSenha))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        if (!await db.AdminUsers.AnyAsync())
        {
            db.AdminUsers.Add(new AdminUser
            {
                Email = adminEmail.Trim().ToLowerInvariant(),
                SenhaHash = ApiKeyAuthenticationHandler.HashKey(adminSenha)
            });
            await db.SaveChangesAsync();
            Log.Information("Primeiro admin criado ({Email}) via ADMIN_EMAIL/ADMIN_PASSWORD.", adminEmail);
        }
    }
}

// --- Fallback do SPA: rotas do painel que não são arquivo (history API) ---
if (File.Exists(spaIndex))
{
    app.MapWhen(
        ctx => !(ctx.Request.Path.StartsWithSegments("/v1")
                 || ctx.Request.Path.StartsWithSegments("/health")
                 || ctx.Request.Path.StartsWithSegments("/hangfire")
                 || ctx.Request.Path.StartsWithSegments("/openapi")),
        spa => spa.Run(ctx => ctx.Response.SendFileAsync(spaIndex)));
}

app.Run();

// Make the implicit Program class visible for integration tests.
namespace Fiscal.Api
{
    public partial class Program { }
}
