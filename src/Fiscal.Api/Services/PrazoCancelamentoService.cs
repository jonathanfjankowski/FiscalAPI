using System.Xml.Linq;
using Fiscal.Core.Entities;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Services;

public record PrazoCancelamentoDto(
    string Uf,
    short Modelo,
    int PrazoMinutos,
    int PrazoPadraoMinutos,
    bool Customizado,
    string? Observacao,
    DateTimeOffset? AtualizadoEm);

/// <summary>
/// Resolve, valida e gerencia os prazos de cancelamento por UF e modelo
/// (55 = NF-e, 65 = NFC-e), combinando a tabela padrão SEFAZ com eventuais
/// sobrescritas persistidas em <c>prazos_cancelamento</c>.
/// </summary>
public class PrazoCancelamentoService
{
    public const int MinutosMinimo = 1;
    public const int MinutosMaximo = 30 * 24 * 60; // 30 dias

    private readonly FiscalDbContext _db;

    public PrazoCancelamentoService(FiscalDbContext db)
    {
        _db = db;
    }

    public async Task<int> ObterPrazoMinutosAsync(string uf, short modelo, CancellationToken ct)
    {
        var ufNorm = (uf ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(ufNorm))
        {
            var custom = await _db.PrazosCancelamento
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Uf == ufNorm && p.Modelo == modelo, ct);

            if (custom is not null)
                return custom.PrazoMinutos;
        }

        return TabelaPrazosCancelamentoSefaz.PadraoPara(ufNorm, modelo).PrazoMinutos;
    }

    /// <summary>
    /// Verifica se o documento está dentro do prazo legal de cancelamento para
    /// a UF do emitente e modelo. Retorna null quando válido, ou a mensagem de
    /// erro detalhada quando expirado.
    /// </summary>
    public async Task<string?> ValidarDentroDoPrazoAsync(DocumentoFiscal doc, CancellationToken ct)
    {
        if (doc.Modelo is not (55 or 65))
            return null;

        var uf = doc.Tenant?.Uf;
        if (string.IsNullOrWhiteSpace(uf))
        {
            uf = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == doc.TenantId)
                .Select(t => t.Uf)
                .FirstOrDefaultAsync(ct);
        }

        var ufNorm = (uf ?? string.Empty).Trim().ToUpperInvariant();
        var prazoMinutos = await ObterPrazoMinutosAsync(ufNorm, doc.Modelo.Value, ct);
        var referencia = ExtrairDataAutorizacao(doc.XmlRetornoSefaz) ?? doc.CriadoEm;

        if (DateTimeOffset.UtcNow <= referencia.AddMinutes(prazoMinutos))
            return null;

        var nomeModelo = doc.Modelo == 65 ? "NFC-e" : "NF-e";
        var prazoTexto = TabelaPrazosCancelamentoSefaz.FormatarPrazo(prazoMinutos);
        var sufixoUf = string.IsNullOrEmpty(ufNorm) ? string.Empty : $" em {ufNorm}";

        return $"Fora do prazo de cancelamento ({prazoTexto} para {nomeModelo}{sufixoUf}, contados da autorização).";
    }

    public async Task<IReadOnlyList<PrazoCancelamentoDto>> ListarTodosAsync(CancellationToken ct)
    {
        var customizados = await _db.PrazosCancelamento
            .AsNoTracking()
            .ToListAsync(ct);

        var mapa = customizados.ToDictionary(p => (p.Uf.ToUpperInvariant(), p.Modelo));
        var lista = new List<PrazoCancelamentoDto>(TabelaPrazosCancelamentoSefaz.Ufs.Length * 2);

        foreach (var uf in TabelaPrazosCancelamentoSefaz.Ufs)
        {
            foreach (short modelo in new short[] { 55, 65 })
            {
                var (padraoMinutos, obsPadrao) = TabelaPrazosCancelamentoSefaz.PadraoPara(uf, modelo);
                if (mapa.TryGetValue((uf, modelo), out var custom))
                {
                    lista.Add(new PrazoCancelamentoDto(
                        uf,
                        modelo,
                        custom.PrazoMinutos,
                        padraoMinutos,
                        true,
                        string.IsNullOrWhiteSpace(custom.Observacao) ? obsPadrao : custom.Observacao,
                        custom.AtualizadoEm));
                }
                else
                {
                    lista.Add(new PrazoCancelamentoDto(
                        uf,
                        modelo,
                        padraoMinutos,
                        padraoMinutos,
                        false,
                        obsPadrao,
                        null));
                }
            }
        }

        return lista;
    }

    public async Task<PrazoCancelamentoDto> AtualizarAsync(
        string uf,
        short modelo,
        int prazoMinutos,
        string? observacao,
        CancellationToken ct)
    {
        var ufNorm = uf.Trim().ToUpperInvariant();
        var (padraoMinutos, obsPadrao) = TabelaPrazosCancelamentoSefaz.PadraoPara(ufNorm, modelo);
        var obsLimpa = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();

        var existente = await _db.PrazosCancelamento
            .FirstOrDefaultAsync(p => p.Uf == ufNorm && p.Modelo == modelo, ct);

        var agora = DateTimeOffset.UtcNow;
        if (existente is null)
        {
            existente = new PrazoCancelamento
            {
                Uf = ufNorm,
                Modelo = modelo,
                PrazoMinutos = prazoMinutos,
                Observacao = obsLimpa,
                AtualizadoEm = agora,
            };
            _db.PrazosCancelamento.Add(existente);
        }
        else
        {
            existente.PrazoMinutos = prazoMinutos;
            existente.Observacao = obsLimpa;
            existente.AtualizadoEm = agora;
        }

        await _db.SaveChangesAsync(ct);

        return new PrazoCancelamentoDto(
            ufNorm,
            modelo,
            existente.PrazoMinutos,
            padraoMinutos,
            true,
            obsLimpa ?? obsPadrao,
            existente.AtualizadoEm);
    }

    public async Task<PrazoCancelamentoDto> RestaurarPadraoAsync(string uf, short modelo, CancellationToken ct)
    {
        var ufNorm = uf.Trim().ToUpperInvariant();
        var existente = await _db.PrazosCancelamento
            .FirstOrDefaultAsync(p => p.Uf == ufNorm && p.Modelo == modelo, ct);

        if (existente is not null)
        {
            _db.PrazosCancelamento.Remove(existente);
            await _db.SaveChangesAsync(ct);
        }

        var (padraoMinutos, obsPadrao) = TabelaPrazosCancelamentoSefaz.PadraoPara(ufNorm, modelo);
        return new PrazoCancelamentoDto(ufNorm, modelo, padraoMinutos, padraoMinutos, false, obsPadrao, null);
    }

    /// <summary>
    /// Extrai <c>&lt;dhRecbto&gt;</c> do XML de retorno da SEFAZ (data real de
    /// autorização protocolada). Retorna null se o XML não estiver presente ou
    /// não contiver o campo (ex.: modo sandbox/mock).
    /// </summary>
    public static DateTimeOffset? ExtrairDataAutorizacao(string? xmlRetorno)
    {
        if (string.IsNullOrWhiteSpace(xmlRetorno))
            return null;

        try
        {
            var doc = XDocument.Parse(xmlRetorno);
            var dhRecbto = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "dhRecbto")
                ?.Value;

            if (!string.IsNullOrWhiteSpace(dhRecbto) && DateTimeOffset.TryParse(dhRecbto, out var parsed))
                return parsed.ToUniversalTime();
        }
        catch
        {
            // XML malformado/incompleto cai no fallback CriadoEm.
        }

        return null;
    }
}
