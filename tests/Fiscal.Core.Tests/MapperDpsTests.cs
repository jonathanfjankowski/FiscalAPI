using Fiscal.Adapters.Unimake;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Services;
using FluentAssertions;
using Unimake.Business.DFe.Servicos;
using Xunit;

namespace Fiscal.Core.Tests;

/// <summary>
/// Mapper e validação do DPS da NFS-e Nacional (layout 1.01) — objeto e XML
/// montados pelas classes NACIONAL da Unimake. Transmissão real exige
/// homologação (trilha externa).
/// </summary>
public class MapperDpsTests
{
    private static Tenant Tenant() => new()
    {
        Id = Guid.NewGuid(),
        Cnpj = "12345678000199",
        RazaoSocial = "Prestador Teste LTDA",
        Uf = "PR",
        CodigoMunicipioIbge = "4106902",
        RegimeTributario = 3,
        InscricaoEstadual = "12345678-01",
        Logradouro = "Rua do Emitente",
        Numero = "100",
        Bairro = "Centro",
        Cep = "80000000",
        NomeMunicipio = "Curitiba",
    };

    private static DocumentoFiscal Documento() => new()
    {
        Id = Guid.NewGuid(),
        Tipo = TipoDocumento.NFSE,
        Ambiente = (short)Ambiente.Homologacao,
        Modelo = ModelosDocumento.NFSeNacional,
        Serie = 1,
        Numero = 42,
        CriadoEm = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero),
        PayloadEntrada = "{}",
    };

    private static NfseDpsRequest Request(Func<NfseDpsRequest, NfseDpsRequest>? customizar = null)
    {
        var req = new NfseDpsRequest(
            Ambiente: "homologacao",
            Serie: 1,
            DataCompetencia: "2026-09-05",
            TipoEmissor: null,
            CodigoMunicipioEmissor: null,
            Tomador: new NfseTomaDto(
                CnpjCpf: "12345678000199", Nome: "Tomador Teste",
                InscricaoMunicipal: null, Telefone: null, Email: null,
                Endereco: new NfseEnderecoDto(
                    CodigoMunicipioIbge: "3550308", Cep: "01001000",
                    Logradouro: "Praça da Sé", Numero: "1",
                    Complemento: null, Bairro: "Sé")),
            Servico: new NfseServicoDto(
                CodigoMunicipioPrestacao: null,
                CodigoTributarioNacional: "010701",
                CodigoTributarioMunicipal: null,
                DescricaoServico: "Desenvolvimento de software",
                CodigoNbs: "112011000"),
            Valores: new NfseValoresDto(
                ValorServicos: 1000, ValorRecebido: null, DescontoIncondicionado: 100,
                TributacaoIssqn: 1, RetencaoIssqn: 1, AliquotaIssqn: 5,
                TributacaoFederal: null, TotalTributos: null),
            IbsCbs: new NfseIbsCbsDto(
                Finalidade: 0, IndicadorFinal: 1,
                CodigoIndicadorOperacao: "000001",
                TipoOperacaoGov: null, TipoEnteGovernamental: null,
                IndicadorDestinatario: 0,
                GibbsCbs: new NfseGibsCbsDto(
                    Cst: "101", CClassTrib: "000001", CodigoCreditoPresumido: null)),
            InformacoesComplementares: "teste");
        return customizar?.Invoke(req) ?? req;
    }

    private static Unimake.Business.DFe.Xml.NFSe.NACIONAL.DPS Mapear(
        NfseDpsRequest req, MapperDps.InfoSubstituicao? subst = null) =>
        MapperDps.Criar(Documento(), Tenant(), req, Ambiente.Homologacao, subst);

    [Fact]
    public void Id_do_dps_tem_45_digitos_no_formato_do_layout()
    {
        var dps = Mapear(Request());

        // DPS + cLocEmi(7) + tpInsc(1) + CNPJ(14) + série(5) + nDPS(15) = 45
        // posições (layout 1.01 — tpInsc "1" = CNPJ).
        dps.InfDPS.Id.Should().Be("DPS4106902" + "1" + "12345678000199" + "00001" + "000000000000042");
        dps.Versao.Should().Be("1.01");
        dps.InfDPS.Serie.Should().Be("00001");
        // TSNumDPS: padrão [1-9][0-9]{0,14} — sem zeros à esquerda.
        dps.InfDPS.NDPS.Should().Be("42");
        dps.InfDPS.DCompet.Should().Be(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Xml_contem_prest_toma_serv_valores_e_ibscbs()
    {
        var xml = Mapear(Request()).GerarXML().OuterXml;

        xml.Should().Contain("<DPS");
        xml.Should().Contain("<CNPJ>12345678000199</CNPJ>");
        xml.Should().Contain("<opSimpNac>1</opSimpNac>"); // regime normal → não optante
        xml.Should().Contain("<cTribNac>010701</cTribNac>");
        xml.Should().Contain("<cNBS>112011000</cNBS>");
        xml.Should().Contain("<vServ>1000.00</vServ>");
        xml.Should().Contain("<vDescIncond>100.00</vDescIncond>");
        xml.Should().Contain("<tribISSQN>1</tribISSQN>");
        xml.Should().Contain("<CST>101</CST>");
        xml.Should().Contain("<cClassTrib>000001</cClassTrib>");
    }

    [Fact]
    public void Prestador_do_simples_nacional_vira_opsimpnac_3()
    {
        var tenant = Tenant();
        tenant.RegimeTributario = 1;
        var dps = MapperDps.Criar(Documento(), tenant, Request(), Ambiente.Homologacao);

        dps.InfDPS.Prest.RegTrib!.OpSimpNac.Should().Be(OptSimplesNacional.ME_EPP);
    }

    [Fact]
    public void Tomador_cpf_e_mapeado_como_cpf()
    {
        var cpf = new NfseTomaDto(
            CnpjCpf: "52998224725", Nome: "Pessoa Física",
            InscricaoMunicipal: null, Telefone: null, Email: null, Endereco: null);
        var req = Request(r => r with { Tomador = cpf });

        var dps = Mapear(req);
        dps.InfDPS.Toma.CPF.Should().Be("52998224725");
    }

    [Fact]
    public void Substituicao_monta_grupo_subst()
    {
        var chaveOriginal = "NFS" + new string('1', 47);
        var xml = Mapear(Request(),
            new MapperDps.InfoSubstituicao(5, "Rejeitada pelo tomador", chaveOriginal))
            .GerarXML().OuterXml;

        xml.Should().Contain("<subst>");
        xml.Should().Contain(chaveOriginal);
    }

    [Fact]
    public void Substituicao_com_cmotivo_invalido_falha_alto()
    {
        var act = () => Mapear(Request(),
            new MapperDps.InfoSubstituicao(42, null, "NFS" + new string('1', 47)));

        act.Should().Throw<ErroNaoRecuperavelException>().WithMessage("*cMotivo 42 inválido*");
    }

    [Fact]
    public void Sem_tomador_falha_alto()
    {
        var semTomador = Request(r => r with { Tomador = null });
        var act = () => Mapear(semTomador);

        act.Should().Throw<ErroNaoRecuperavelException>().WithMessage("*'tomador'*");
    }

    [Fact]
    public void LerRequest_devolve_o_payload_e_a_substituicao()
    {
        var req = Request();
        var chaveOriginal = "NFS" + new string('2', 47);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            dps = req,
            cMotivo = 5,
            xMotivo = "erro de valores",
            chaveSubstituida = chaveOriginal,
        });

        MapperDps.LerRequest(payload).Should().BeEquivalentTo(req);
        var subst = MapperDps.LerSubstituicao(payload);
        subst.Should().NotBeNull();
        subst!.CMotivo.Should().Be(5);
        subst.XMotivo.Should().Be("erro de valores");
        subst.ChaveSubstituida.Should().Be(chaveOriginal);

        var payloadSimples = System.Text.Json.JsonSerializer.Serialize(req);
        MapperDps.LerRequest(payloadSimples).Should().BeEquivalentTo(req);
        MapperDps.LerSubstituicao(payloadSimples).Should().BeNull();
    }
}

public class ValidadorNfseDpsTests
{
    private static NfseDpsRequest RequestBase() => new(
        Ambiente: "homologacao",
        Serie: 1,
        DataCompetencia: null,
        TipoEmissor: null,
        CodigoMunicipioEmissor: null,
        Tomador: new NfseTomaDto(
            CnpjCpf: "12345678000199", Nome: "Tomador", InscricaoMunicipal: null,
            Telefone: null, Email: null, Endereco: null),
        Servico: new NfseServicoDto(null, "010701", null, "Serviço de teste", null),
        Valores: new NfseValoresDto(100, null, null, 1, 1, 5, null, null),
        IbsCbs: null,
        InformacoesComplementares: null);

    private static string[] Validar(NfseDpsRequest req) =>
        [.. new ValidadorNfseDps().Validar(req).Select(e => $"{e.Campo}: {e.Mensagem}")];

    [Fact]
    public void Request_completo_passa_sem_erros()
    {
        Validar(RequestBase()).Should().BeEmpty();
    }

    [Fact]
    public void Ambiente_invalido_falha()
    {
        Validar(RequestBase() with { Ambiente = "test" })
            .Should().Contain(e => e.Contains("'producao' ou 'homologacao'"));
    }

    [Fact]
    public void Tomador_ausente_ou_documento_invalido_falham()
    {
        Validar(RequestBase() with { Tomador = null })
            .Should().Contain(e => e.Contains("exige 'tomador'"));

        Validar(RequestBase() with
        {
            Tomador = new NfseTomaDto("123", null, null, null, null, null)
        }).Should().Contain(e => e.Contains("esperado 11 ou 14 dígitos"));
    }

    [Fact]
    public void Servico_sem_ctribnac_ou_descricao_falha()
    {
        Validar(RequestBase() with
        {
            Servico = new NfseServicoDto(null, "", null, "", null)
        }).Should().Contain(e => e.Contains("cTribNac"))
          .And.Contain(e => e.Contains("xDescServ"));
    }

    [Fact]
    public void Data_de_competencia_com_formato_errado_falha()
    {
        Validar(RequestBase() with { DataCompetencia = "05/09/2026" })
            .Should().Contain(e => e.Contains("yyyy-MM-dd"));
    }

    [Fact]
    public void Ibscbs_com_sepec_incompleto_falha()
    {
        var req = RequestBase() with
        {
            IbsCbs = new NfseIbsCbsDto(
                Finalidade: 0, IndicadorFinal: 1,
                CodigoIndicadorOperacao: "123",
                TipoOperacaoGov: null, TipoEnteGovernamental: null, IndicadorDestinatario: null,
                GibbsCbs: new NfseGibsCbsDto(Cst: "1", CClassTrib: "000001", CodigoCreditoPresumido: null)),
        };

        Validar(req).Should().Contain(e => e.Contains("cIndOp deve ter 6 dígitos"))
                             .And.Contain(e => e.Contains("CST do IBS/CBS deve ter 3 dígitos"));
    }

    [Fact]
    public void Tipo_operacao_gov_exige_ente_governamental()
    {
        var req = RequestBase() with
        {
            IbsCbs = new NfseIbsCbsDto(
                Finalidade: 0, IndicadorFinal: 1, CodigoIndicadorOperacao: "000001",
                TipoOperacaoGov: 1, TipoEnteGovernamental: null, IndicadorDestinatario: null,
                GibbsCbs: new NfseGibsCbsDto("101", "000001", null)),
        };

        Validar(req).Should().Contain(e => e.Contains("tpOper informado exige tpEnteGov"));
    }
}
