using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Emissor "sandbox" — não fala com SEFAZ. Usado em modo dev e em testes de integração.
/// Resolve o caso "como contribuo/testo sem ter um certificado A1 real".
/// </summary>
public class EmissorMock : IEmissorFiscal
{
    public Task<ResultadoEmissao> EmitirAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        // Simula autorização com chave determinística baseada no id do documento.
        var chave = DeterministicChave(documento.Id);
        var protocolo = "1" + Math.Abs(documento.Id.GetHashCode()).ToString("D14")[..14];

        var xmlAssinado = $"""
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="{chave}" versao="4.00">
                  <ide>
                    <cUF>41</cUF>
                    <natOp>VENDA</natOp>
                    <serie>{documento.Serie}</serie>
                    <nNF>{documento.Numero}</nNF>
                    <tpAmb>{(short)ambiente}</tpAmb>
                  </ide>
                </infNFe>
              </NFe>
              <protNFe versao="4.00">
                <infProt>
                  <nProt>{protocolo}</nProt>
                  <cStat>100</cStat>
                  <xMotivo>Autorizado o uso da NF-e (MOCK)</xMotivo>
                </infProt>
              </protNFe>
            </nfeProc>
            """;

        var xmlRetorno = $"""
            <retEnviNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <cStat>100</cStat>
              <xMotivo>Autorizado o uso da NF-e (MOCK)</xMotivo>
              <protNFe><infProt><nProt>{protocolo}</nProt></infProt></protNFe>
            </retEnviNFe>
            """;

        return Task.FromResult(new ResultadoEmissao(
            ResultadoEmissaoStatus.Autorizada,
            chave, protocolo, xmlAssinado, xmlRetorno, Motivo: null));
    }

    private static string DeterministicChave(Guid id)
    {
        // 44 dígitos, determinístico a partir do id (para testes idempotentes).
        var bytes = id.ToByteArray();
        var sb = new System.Text.StringBuilder(44);
        for (int i = 0; i < 44; i++) sb.Append(bytes[i % bytes.Length] % 10);
        return sb.ToString();
    }
}
