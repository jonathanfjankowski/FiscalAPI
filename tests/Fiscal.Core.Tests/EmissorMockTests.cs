using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Adapters.Unimake;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

public class EmissorMockTests
{
    [Fact]
    public async Task Emissao_retorna_sucesso_com_chave_e_protocolo()
    {
        var mock = new EmissorMock();
        var doc = new DocumentoFiscal
        {
            Id = Guid.NewGuid(),
            Tipo = TipoDocumento.NFE,
            Serie = 1,
            Numero = 100
        };
        // Mock não usa tenant nem certificado — a interface exige.
        var tenant = new Tenant();
        var cert = (X509Certificate2?)null;

        var resultado = await mock.EmitirAsync(doc, tenant, cert!, Ambiente.Homologacao, CancellationToken.None);

        resultado.Status.Should().Be(ResultadoEmissaoStatus.Autorizada);
        resultado.ChaveAcesso.Should().NotBeNullOrEmpty().And.HaveLength(44);
        resultado.ProtocoloAutorizacao.Should().NotBeNullOrEmpty();
        resultado.XmlAssinado.Should().Contain("nfeProc");
        resultado.XmlRetornoSefaz.Should().Contain("cStat");
        resultado.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task Chave_e_deterministica_para_mesmo_id()
    {
        var mock = new EmissorMock();
        var id = Guid.NewGuid();
        var doc1 = new DocumentoFiscal { Id = id, Serie = 1, Numero = 1 };
        var doc2 = new DocumentoFiscal { Id = id, Serie = 1, Numero = 1 };

        var r1 = await mock.EmitirAsync(doc1, new Tenant(), null!, Ambiente.Homologacao, default);
        var r2 = await mock.EmitirAsync(doc2, new Tenant(), null!, Ambiente.Homologacao, default);

        r1.ChaveAcesso.Should().Be(r2.ChaveAcesso);
    }
}
