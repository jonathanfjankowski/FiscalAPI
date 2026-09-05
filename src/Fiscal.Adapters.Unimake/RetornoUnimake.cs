using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using UExceptions = global::Unimake.Exceptions;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Helpers compartilhados pelos emissores reais: execução do serviço Unimake
/// (com tradução de exceções) e interpretação do protNFe.
/// </summary>
internal static class RetornoUnimake
{
    /// <summary>
    /// Executa o serviço (bloqueante). Falhas de schema/assinatura/certificado não
    /// se resolvem com retry — viram ErroNaoRecuperavelException; falhas de rede
    /// propagam como erro de transmissão (CONTINGENCIA no job).
    /// </summary>
    public static void Executar(ServicoBase servico)
    {
        try
        {
            servico.Executar();
        }
        catch (UExceptions.ValidarXMLException ex)
        {
            throw new ErroNaoRecuperavelException("XML rejeitado na validação de schema local: " + ex.Message, ex);
        }
        catch (UExceptions.ValidatorDFeException ex)
        {
            throw new ErroNaoRecuperavelException("Estrutura do DFe inválida: " + ex.Message, ex);
        }
        catch (UExceptions.SemSchemaException ex)
        {
            throw new ErroNaoRecuperavelException("Validação de schema não executada: " + ex.Message, ex);
        }
        catch (UExceptions.AssinaturaException ex)
        {
            throw new ErroNaoRecuperavelException("Falha ao assinar o XML (certificado sem chave privada?): " + ex.Message, ex);
        }
        catch (UExceptions.CarregarCertificadoException ex)
        {
            throw new ErroNaoRecuperavelException("Falha ao carregar o certificado digital: " + ex.Message, ex);
        }
        catch (UExceptions.CertificadoDigitalException ex)
        {
            throw new ErroNaoRecuperavelException("Certificado digital inválido: " + ex.Message, ex);
        }
    }

    /// <summary>cStat → resultado. 100/150 = autorizada; 110/205/301/302/303 =
    /// denegada (uso denegado); demais = rejeitada.</summary>
    public static ResultadoEmissao InterpretarProt(InfProt prot, string? xmlAssinado, string? xmlRetorno, string? xmlGerado)
    {
        var status = prot.CStat switch
        {
            100 or 150 => ResultadoEmissaoStatus.Autorizada,
            110 or 205 or 301 or 302 or 303 => ResultadoEmissaoStatus.Denegada,
            _ => ResultadoEmissaoStatus.Rejeitada,
        };

        return new ResultadoEmissao(
            status,
            prot.ChNFe,
            prot.NProt,
            xmlAssinado, xmlRetorno,
            status == ResultadoEmissaoStatus.Autorizada ? null : $"cStat {prot.CStat}: {prot.XMotivo}",
            XmlGerado: xmlGerado);
    }
}
