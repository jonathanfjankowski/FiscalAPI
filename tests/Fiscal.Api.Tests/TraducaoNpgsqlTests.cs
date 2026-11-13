using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Compila as queries dos endpoints admin contra o provider **Npgsql**, sem
/// conectar ao banco.
///
/// Por que existe: a suíte de integração roda em SQLite, e o tradutor do
/// SQLite é diferente do Npgsql. Um `OrderBy` sobre um record projetado, por
/// exemplo, passa no SQLite (o ramo materializa em memória) e lança
/// "could not be translated" em produção. Este teste fecha essa cegueira:
/// `ToQueryString()` força a tradução completa sem abrir conexão.
///
/// Não substitui teste de integração — só garante que a query É traduzível
/// pelo provider de produção.
/// </summary>
public class TraducaoNpgsqlTests
{
    private static FiscalDbContext ContextoNpgsql()
    {
        // Provider Npgsql, connection string fictícia: ToQueryString() compila
        // a query sem abrir conexão.
        var opts = new DbContextOptionsBuilder<FiscalDbContext>()
            .UseNpgsql("Host=localhost;Database=nao-conecta;Username=x;Password=x")
            .Options;
        return new FiscalDbContext(opts);
    }

    [Fact]
    public void Documentos_ordenacao_por_coluna_da_entidade_traduz()
    {
        using var db = ContextoNpgsql();

        // Forma CORRETA: ordena as colunas da entidade e só então projeta.
        var query = db.DocumentosFiscais.AsNoTracking()
            .Where(d => d.Status == StatusDocumento.AUTORIZADA)
            .OrderByDescending(d => d.CriadoEm)
            .Skip(0)
            .Take(50)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                TenantRazaoSocial = d.Tenant!.RazaoSocial,
                d.Numero,
                d.CriadoEm,
            });

        var sql = query.ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    [Fact]
    public void Documentos_ordenacao_sobre_record_nomeado_NAO_traduz()
    {
        using var db = ContextoNpgsql();

        // Forma ERRADA (a que causou 500 em produção): ordenar DEPOIS de
        // projetar para um RECORD NOMEADO. Tipo anônimo traduz; record com
        // construtor posicional não — o tradutor não consegue empurrar o
        // OrderBy para dentro da construção. O teste documenta a armadilha.
        var query = db.DocumentosFiscais.AsNoTracking()
            .Select(d => new DocRecord(d.Id, d.CriadoEm, d.Tenant!.RazaoSocial))
            .OrderByDescending(x => x.CriadoEm);

        var acao = () => query.ToQueryString();

        acao.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be translated*");
    }

    private record DocRecord(Guid Id, DateTimeOffset CriadoEm, string Nome);

    [Fact]
    public void NotasRecebidas_ordenacao_por_coluna_da_entidade_traduz()
    {
        using var db = ContextoNpgsql();

        var query = db.NotasRecebidas.AsNoTracking()
            .OrderByDescending(n => n.RecebidaEm)
            .ThenByDescending(n => n.Id)
            .Skip(0)
            .Take(50)
            .Select(n => new
            {
                n.Id,
                n.TenantId,
                TenantRazaoSocial = n.Tenant!.RazaoSocial,
                n.Chave,
                n.RecebidaEm,
            });

        var sql = query.ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    [Fact]
    public void NotasRecebidas_ordenacao_sobre_record_nomeado_NAO_traduz()
    {
        using var db = ContextoNpgsql();

        var query = db.NotasRecebidas.AsNoTracking()
            .Select(n => new NotaRecord(n.Id, n.RecebidaEm, n.Tenant!.RazaoSocial))
            .OrderByDescending(x => x.RecebidaEm);

        var acao = () => query.ToQueryString();

        acao.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be translated*");
    }

    private record NotaRecord(Guid Id, DateTimeOffset RecebidaEm, string Nome);

    [Fact]
    public void Auditoria_paginacao_no_banco_traduz()
    {
        using var db = ContextoNpgsql();

        // A auditoria é append-only e cresce para sempre: a paginação TEM que
        // ir para o SQL, não para a memória.
        var termo = "CERTIFICADO";
        var query = db.Auditoria.AsNoTracking()
            .Where(a => a.Acao.Contains(termo))
            .OrderByDescending(a => a.CriadoEm)
            .ThenByDescending(a => a.Id)
            .Skip(50)
            .Take(50);

        var sql = query.ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
        sql.Should().Contain("OFFSET");
    }

    [Fact]
    public void Documentos_busca_livre_traduz()
    {
        using var db = ContextoNpgsql();

        var busca = "Padaria";
        var digitos = "123";
        var query = db.DocumentosFiscais.AsNoTracking()
            .Where(d =>
                (d.ChaveAcesso != null && d.ChaveAcesso.Contains(busca)) ||
                (d.Numero != null && d.Numero.ToString() == digitos) ||
                (d.Tenant != null && d.Tenant.RazaoSocial.Contains(busca)));

        var sql = query.ToQueryString();

        sql.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Dashboard_fila_por_lista_de_enum_traduz()
    {
        using var db = ContextoNpgsql();

        var statusFila = new[]
        {
            StatusDocumento.PENDENTE,
            StatusDocumento.PROCESSANDO,
            StatusDocumento.CONTINGENCIA,
            StatusDocumento.CANCELAMENTO_PENDENTE,
        };

        var query = db.DocumentosFiscais.AsNoTracking()
            .Where(d => statusFila.Contains(d.Status))
            .Select(d => new { d.Id, d.Status, d.CriadoEm });

        query.ToQueryString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Auditoria_filtro_por_prefixo_de_guid_traduz()
    {
        using var db = ContextoNpgsql();

        // A tabela mostra só os 8 primeiros caracteres do recurso, então o
        // filtro aceita prefixo. Precisa traduzir: Guid -> texto no Npgsql.
        var prefixo = "b3016deb";
        var query = db.Auditoria.AsNoTracking()
            .Where(a => a.RecursoId != null && a.RecursoId.ToString()!.StartsWith(prefixo));

        query.ToQueryString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Documentos_filtro_por_lista_de_status_traduz()
    {
        using var db = ContextoNpgsql();

        // A fila tem 4 estados; "ver todos" filtra por todos de uma vez.
        var statuses = new List<StatusDocumento>
        {
            StatusDocumento.PENDENTE,
            StatusDocumento.PROCESSANDO,
            StatusDocumento.CONTINGENCIA,
            StatusDocumento.CANCELAMENTO_PENDENTE,
        };
        var query = db.DocumentosFiscais.AsNoTracking()
            .Where(d => statuses.Contains(d.Status));

        query.ToQueryString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Dashboard_agregados_no_sql_traduzem()
    {
        using var db = ContextoNpgsql();
        var inicio = DateTimeOffset.UtcNow.Date;

        // O dashboard é a landing page e polla a cada 15s: os agregados têm
        // que rodar no banco, não trazer a tabela para a memória.
        db.DocumentosFiscais.AsNoTracking()
            .Where(d => d.CriadoEm >= new DateTimeOffset(inicio, TimeSpan.Zero))
            .ToQueryString().Should().NotBeNullOrEmpty();

        var grupos = db.DocumentosFiscais.AsNoTracking()
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() });
        grupos.ToQueryString().Should().Contain("GROUP BY");
    }

    [Fact]
    public void Dashboard_emissao_recente_ordena_e_limita_no_sql()
    {
        using var db = ContextoNpgsql();

        var sql = db.DocumentosFiscais.AsNoTracking()
            .OrderByDescending(d => d.CriadoEm)
            .ThenByDescending(d => d.Id)
            .Take(20)
            .Select(d => new { d.Id, d.Numero, Nome = d.Tenant!.RazaoSocial })
            .ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    [Fact]
    public void Certificados_vencendo_por_DateOnly_traduz()
    {
        using var db = ContextoNpgsql();

        var limite = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var query = db.Certificados.AsNoTracking()
            .Where(c => c.Ativo && c.ValidoAte <= limite)
            .Select(c => new { c.Id, c.Thumbprint, c.ValidoAte });

        query.ToQueryString().Should().NotBeNullOrEmpty();
    }
}