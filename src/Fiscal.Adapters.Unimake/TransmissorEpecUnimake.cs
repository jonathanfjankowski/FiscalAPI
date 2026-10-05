using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using NFeRecepcaoEvento = Unimake.Business.DFe.Servicos.NFe.RecepcaoEvento;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Transmite o evento prévio EPEC (110140) à SVRS — contingência NF-e. O evento
/// registra a operação na SEFAZ ANTES da transmissão da NF-e completa (que vai
/// depois, com tpEmis 4, dentro da janela de 168h). A chave referenciada é a
/// MESMA da NF-e futura: determinística (dhEmi = CriadoEm, cNF = id do doc) e
/// já com tpEmis 4, pois o mapper lê doc.ModoContingencia = "EPEC".
/// </summary>
public class TransmissorEpecUnimake : ITransmissorEpec
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Task<ResultadoEvento> TransmitirAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        if (documento.Modelo is not 55)
            throw new ErroNaoRecuperavelException("EPEC só se aplica a NF-e (modelo 55).");
        if (documento.Serie is null || documento.Numero is null)
            throw new ErroNaoRecuperavelException("Documento sem série/número reservados — EPEC não pode ser montado.");
        if (documento.PayloadEntrada is null)
            throw new ErroNaoRecuperavelException("Documento sem PayloadEntrada — EPEC não pode ser montado.");

        var request = JsonSerializer.Deserialize<EmissaoRequest>(documento.PayloadEntrada, JsonOpts)
            ?? throw new ErroNaoRecuperavelException("PayloadEntrada não pôde ser deserializado como EmissaoRequest.");

        if (request.Destinatario is null)
            throw new ErroNaoRecuperavelException("EPEC exige destinatário identificado no payload.");
        var ufDestino = request.Destinatario.Endereco?.Uf;
        if (string.IsNullOrWhiteSpace(ufDestino) || ufDestino!.Trim().Length != 2)
            throw new ErroNaoRecuperavelException("EPEC exige o endereço do destinatário com UF (2 letras).");

        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var ufEmit) || ufEmit == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida para EPEC: '{tenant.Uf}'.");
        if (!Enum.TryParse<UFBrasil>(ufDestino, ignoreCase: true, out var ufDest) || ufDest == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do destinatário inválida para EPEC: '{ufDestino}'.");

        // Mesma montagem da NF-e futura (tpEmis 4 via doc.ModoContingencia="EPEC")
        // → a chave do EPEC é a chave da NF-e que será transmitida depois.
        var envi = MapperEnviNFe.Criar(documento, tenant, request, ambiente);
        var chave = envi.NFe[0].InfNFeField.Chave
            ?? throw new ErroNaoRecuperavelException("Chave da NF-e não calculada pelo mapper — EPEC não pode ser montado.");

        // vICMS/vST: somados do payload (mesma regra do MapearTotal).
        decimal vIcms = 0, vSt = 0;
        foreach (var item in request.Itens)
        {
            if (item.ImpostosV2?.Icms is { } icms)
            {
                vIcms += icms.Valor ?? 0;
                vSt += icms.St?.ValorSt ?? 0;
            }
            else
            {
                foreach (var im in item.Impostos ?? [])
                    vIcms += im.Valor ?? 0;
            }
        }

        var detEpec = new DetEventoEPEC
        {
            DescEvento = "EPEC",
            Versao = "1.00",
            COrgaoAutor = ufEmit,
            TpAutor = TipoAutor.EmpresaEmitente, // 1 = emitente
            VerAplic = "FiscalAPI",
            DhEmi = documento.CriadoEm,
            TpNF = request.TipoOperacao?.Trim().ToLowerInvariant() == "entrada"
                ? TipoOperacao.Entrada
                : TipoOperacao.Saida,
            IE = tenant.InscricaoEstadual,
            Dest = new DetEventoEPECDest
            {
                UF = ufDest,
                CNPJ = request.Destinatario.CnpjCpf.Length == 14
                    ? new string(request.Destinatario.CnpjCpf.Where(char.IsDigit).ToArray())
                    : null,
                CPF = request.Destinatario.CnpjCpf.Length == 11
                    ? new string(request.Destinatario.CnpjCpf.Where(char.IsDigit).ToArray())
                    : null,
                IE = request.Destinatario.InscricaoEstadual is { Length: > 0 } ie ? ie : null,
                VNF = (double)request.Totais.ValorNota,
                VICMS = (double)vIcms,
                VST = (double)vSt,
            },
        };

        var inf = new InfEvento
        {
            Id = $"ID{(int)TipoEventoNFe.EPEC}{chave}01",
            COrgao = UFBrasil.SVRS, // EPEC é sempre autorizado pela SVRS
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            CNPJ = new string(tenant.Cnpj.Where(char.IsDigit).ToArray()),
            ChNFe = chave,
            DhEvento = DateTimeOffset.Now,
            TpEvento = TipoEventoNFe.EPEC,
            NSeqEvento = 1,
            VerEvento = "3.10",
            DetEvento = detEpec,
        };

        var env = new EnvEvento
        {
            Versao = "3.10",
            IdLote = (documento.Id.GetHashCode() & 0x7fffffff).ToString(),
            Evento = new List<Evento> { new() { Versao = "3.10", InfEvento = inf } },
        };

        var config = ConfiguracaoUnimakeFactory.Criar(55, tenant, certificado, ambiente, exigirCsc: false);
        config.CodigoUF = (int)UFBrasil.SVRS; // roteia o WS do evento para a SVRS

        using var servico = new NFeRecepcaoEvento(env, config);
        RetornoUnimake.Executar(servico);

        var ret = servico.Result;
        var evRet = ret?.RetEvento?.FirstOrDefault(e => e.InfEvento is not null);
        if (evRet is null)
        {
            if ((ret?.CStat ?? 0) >= 200)
                return Task.FromResult(new ResultadoEvento(
                    ResultadoEventoStatus.Rejeitado, null, servico.RetornoWSString,
                    $"cStat {ret!.CStat}: {ret.XMotivo}"));

            return Task.FromResult(new ResultadoEvento(
                ResultadoEventoStatus.ErroTransmissao, null, servico.RetornoWSString,
                $"Retorno inesperado da SVRS (cStat {ret?.CStat}): {ret?.XMotivo}"));
        }

        return Task.FromResult(InterpreteEventoUnimake.InterpretarEvento(
            evRet.InfEvento.CStat, evRet.InfEvento.NProt, evRet.InfEvento.XMotivo, servico.RetornoWSString));
    }
}
