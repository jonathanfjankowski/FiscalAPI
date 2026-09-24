using Fiscal.Adapters.Unimake;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Core.Entities;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography.X509Certificates;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Distribuição DFe: para cada tenant ativo e cada ambiente, consulta a
/// SEFAZ/RFB a partir do último NSU, grava as NFe destinadas em
/// notas_recebidas e dispara webhook `nota.recebida` (via outbox).
/// Sandbox sem certificado apenas adianta o NSU via ConsultaDistribuicaoMock.
/// </summary>
public class SincronizarDistribuicaoDFeJob
{
    private readonly FiscalDbContext _db;
    private readonly IRepositorioNotaRecebida _notaRepo;
    private readonly IRepositorioNsu _nsuRepo;
    private readonly IRepositorioCertificado _certRepo;
    private readonly ICertificadoStore _certStore;
    private readonly IEnumerable<IConsultaDistribuicaoDfe> _consultas;
    private readonly bool _sandbox;
    private readonly ILogger<SincronizarDistribuicaoDFeJob> _logger;

    public SincronizarDistribuicaoDFeJob(
        FiscalDbContext db,
        IRepositorioNotaRecebida notaRepo,
        IRepositorioNsu nsuRepo,
        IRepositorioCertificado certRepo,
        ICertificadoStore certStore,
        IEnumerable<IConsultaDistribuicaoDfe> consultas,
        IConfiguration configuration,
        ILogger<SincronizarDistribuicaoDFeJob> logger)
    {
        _db = db;
        _notaRepo = notaRepo;
        _nsuRepo = nsuRepo;
        _certRepo = certRepo;
        _certStore = certStore;
        _consultas = consultas;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", false);
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        IConsultaDistribuicaoDfe? consulta =
            _consultas.OfType<ConsultaDistribuicaoMock>().FirstOrDefault()
            ?? _consultas.OfType<ConsultaDistribuicaoUnimake>().FirstOrDefault()
            ?? _consultas.FirstOrDefault();
        if (consulta is null) return;

        var tenants = await _db.Tenants.Where(t => t.Ativo).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            try
            {
                await SincronizarTenantAsync(tenant, consulta, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // shutdown do host — não tratar como falha de tenant
            }
            catch (Exception ex)
            {
                // Falha de um tenant não trava os demais (SEFAZ fora, certificado
                // vencido etc.) — o próximo ciclo tenta de novo a partir do NSU.
                _logger.LogWarning(ex, "Distribuição DFe falhou para tenant {Tenant}.", tenant.Id);
            }
        }
    }

    private async Task SincronizarTenantAsync(Tenant tenant, IConsultaDistribuicaoDfe consulta, CancellationToken ct)
    {
        var cert = await _certRepo.ObterAtivoPorTenantAsync(tenant.Id, ct);
        if (cert is null && !_sandbox) return; // sem certificado em produção: nada a fazer
        using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);

        foreach (var ambiente in new[] { Ambiente.Producao, Ambiente.Homologacao })
        {
            var nsu = await _nsuRepo.ObterAsync(tenant.Id, (short)ambiente, ct);
            var ultimoNsu = nsu?.UltimoNsu ?? "000000000000000";

            // Cada página entrega até 50 DF-e: consulta em loop até consumir a
            // fila do RFB. O cursor para a próxima página é o UltNSU da resposta
            // (último NSU efetivamente entregue) — jamais o MaxNSU, que é o maior
            // NSU da base RFB e pularia as notas não incluídas nesta página.
            var cursor = ultimoNsu.PadLeft(15, '0');
            var novas = 0;
            var iteracoes = 0;
            while (iteracoes++ < 20)
            {
                var resultado = await consulta.ConsultarAsync(tenant, x509, ambiente, cursor, ct);

                foreach (var documento in resultado.Documentos)
                {
                    var chave = ExtratorDfe.ChaveDoXml(documento.Xml);
                    if (chave is null)
                    {
                        // Não é possível arquivar sem chave — mas a perda precisa ser
                        // alta (LogError), nunca silenciosa: o cursor avança para não
                        // travar a sincronização inteira num XML imprestável.
                        _logger.LogError(
                            "DFe tenant {Tenant}/{Ambiente}: NSU {Nsu} ({Schema}) sem chave de acesso extraível — documento não arquivado. XML: {Xml}",
                            tenant.Id, ambiente, documento.Nsu, documento.Schema, documento.Xml);
                        continue;
                    }
                    if (await _notaRepo.ObterPorChaveAsync(tenant.Id, chave, ct) is not null) continue;

                    var nota = new NotaRecebida
                    {
                        TenantId = tenant.Id,
                        Ambiente = (short)ambiente,
                        Chave = chave,
                        Nsu = documento.Nsu,
                        TipoSchema = documento.Schema.Contains("procNFe", StringComparison.OrdinalIgnoreCase) ? "procNFe" : "resNFe",
                        XmlResumo = documento.Xml,
                        XmlCompleto = documento.Schema.Contains("procNFe", StringComparison.OrdinalIgnoreCase) ? documento.Xml : null,
                        CnpjEmitente = ExtratorDfe.ValorDoElemento(documento.Xml, "CNPJ"),
                        NomeEmitente = ExtratorDfe.ValorDoElemento(documento.Xml, "xNome"),
                        Valor = decimal.TryParse(ExtratorDfe.ValorDoElemento(documento.Xml, "vNF"),
                            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null,
                        EmitidaEm = DateTimeOffset.TryParse(ExtratorDfe.ValorDoElemento(documento.Xml, "dhEmi")
                            ?? ExtratorDfe.ValorDoElemento(documento.Xml, "dEmi"), out var dh) ? dh : null,
                    };
                    await _notaRepo.AdicionarAsync(nota, ct);
                    novas++;

                    if (tenant.WebhookUrl is not null)
                    {
                        _db.WebhooksEntrega.Add(new WebhookEntrega
                        {
                            TenantId = tenant.Id,
                            DocumentoId = null,
                            TipoEvento = Webhooks.NotaRecebida,
                            Payload = Webhooks.PayloadNotaRecebida(nota, DateTimeOffset.UtcNow),
                            Status = "PENDENTE",
                            ProximaTentativaEm = DateTimeOffset.UtcNow,
                        });
                    }
                }

                // cStat 137 = nada localizado abaixo do maxNSU (pode saltar para ele);
                // cStat 138 = há documentos — a próxima página começa no UltNSU entregue.
                var proximoCursor = (resultado.CStat == 137 ? resultado.MaxNsu : resultado.UltNsu)
                    .PadLeft(15, '0');
                var consumiuFila = resultado.CStat != 138 || proximoCursor == cursor;
                cursor = proximoCursor;
                await _db.SaveChangesAsync(ct);

                if (consumiuFila) break;
            }

            if (cursor != ultimoNsu.PadLeft(15, '0'))
                await _nsuRepo.SalvarAsync(new NsuDistribuicao
                {
                    TenantId = tenant.Id,
                    Ambiente = (short)ambiente,
                    UltimoNsu = cursor,
                }, ct);

            await _db.SaveChangesAsync(ct);

            if (novas > 0)
                _logger.LogInformation("Distribuição DFe tenant {Tenant}/{Ambiente}: {Novas} nova(s) nota(s).",
                    tenant.Id, ambiente, novas);
        }
    }
}
