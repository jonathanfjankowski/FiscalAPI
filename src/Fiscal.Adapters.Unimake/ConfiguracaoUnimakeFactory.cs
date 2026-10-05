using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Unimake.Business.DFe.Servicos;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Monta a Configuracao da Unimake a partir do tenant + certificado + ambiente.
/// A assinatura XML e a escolha da URL do WS (por UF/ambiente/serviço) são
/// feitas pela própria Unimake dentro do serviço (Autorizacao/RetAutorizacao).
/// </summary>
internal static class ConfiguracaoUnimakeFactory
{
    public static Configuracao Criar(
        short modelo,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        string? csc = null,
        string? cscId = null,
        bool exigirCsc = true,
        string? modoContingencia = null)
    {
        var nfce = modelo == 65;
        if (nfce && exigirCsc && (string.IsNullOrWhiteSpace(csc) || string.IsNullOrWhiteSpace(cscId)))
            throw new ErroNaoRecuperavelException(
                "Emissão de NFC-e exige CSC e CscId cadastrados para o tenant " +
                "(configure via PUT /v1/tenants/perfil).");

        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var ufBrasil) || ufBrasil == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida para emissão: '{tenant.Uf}'.");

        return new Configuracao
        {
            TipoDFe = nfce ? TipoDFe.NFCe : TipoDFe.NFe,
            TipoEmissao = modoContingencia switch
            {
                "SVCAN" => TipoEmissao.ContingenciaSVCAN,
                "SVCRS" => TipoEmissao.ContingenciaSVCRS,
                "EPEC" => TipoEmissao.ContingenciaEPEC,
                "OFFLINE" => TipoEmissao.ContingenciaOffLine,
                _ => TipoEmissao.Normal,
            },
            TipoAmbiente = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            Modelo = nfce ? ModeloDFe.NFCe : ModeloDFe.NFe,
            SchemaVersao = "4.00",
            CodigoUF = (int)ufBrasil,
            CertificadoDigital = certificado,
            CSC = csc,
            CSCIDToken = int.TryParse(cscId, out var id) ? id : 0,
        };
    }
}
