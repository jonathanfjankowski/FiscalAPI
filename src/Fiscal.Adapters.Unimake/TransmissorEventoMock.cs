using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Transmissor "sandbox" de eventos — não fala com SEFAZ. Usado em
/// ModoSandbox=true e nos testes de integração (Testing).
/// </summary>
public class TransmissorEventoMock : ITransmissorEventoFiscal
{
    public Task<ResultadoEvento> TransmitirAsync(
        EventoFiscal evento,
        DocumentoFiscal? documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        var protocolo = "MOCK" + Math.Abs(evento.Id.GetHashCode()).ToString("D13")[..13];

        var xmlRetorno = evento.TipoEvento == "INUTILIZACAO"
            ? $"""
               <retInutNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00">
                 <infInut Id="MOCK">
                   <cStat>102</cStat>
                   <xMotivo>Inutilizacao homologada (MOCK)</xMotivo>
                   <nProt>{protocolo}</nProt>
                 </infInut>
               </retInutNFe>
               """
            : $"""
               <retEnvEvento xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00">
                 <retEvento versao="1.00">
                   <infEvento>
                     <cStat>135</cStat>
                     <xMotivo>Evento homologado (MOCK)</xMotivo>
                     <nProt>{protocolo}</nProt>
                   </infEvento>
                 </retEvento>
               </retEnvEvento>
               """;

        return Task.FromResult(new ResultadoEvento(
            ResultadoEventoStatus.Processado,
            protocolo,
            xmlRetorno,
            Motivo: null));
    }
}
