using System.Text;
using Fiscal.Api.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>Secret manager plugável via convenção CHAVE_FILE (Docker secrets).</summary>
public class SegurancaArquivosSecretsTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "fiscal-secrets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Env_com_suficiente_file_le_valor_do_arquivo()
    {
        Directory.CreateDirectory(_pasta);
        var caminhoKek = Path.Combine(_pasta, "kek");
        File.WriteAllText(caminhoKek, "chave-mestra-secreta\n", new UTF8Encoding(false));
        Environment.SetEnvironmentVariable("CERTIFICADOS__CHAVEMESTRAKEK_FILE", caminhoKek);
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Certificados:ChaveMestraKEK"] = "valor-do-appsettings",
                })
                .AddFileSecrets()
                .Build();

            config["Certificados:ChaveMestraKEK"].Should().Be("chave-mestra-secreta");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CERTIFICADOS__CHAVEMESTRAKEK_FILE", null);
        }
    }

    [Fact]
    public void Sem_arquivo_nao_quebra_o_load()
    {
        Environment.SetEnvironmentVariable("ADMIN_JWT_SECRET_FILE", Path.Combine(_pasta, "nao-existe"));
        try
        {
            var act = () => new ConfigurationBuilder().AddFileSecrets().Build();
            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMIN_JWT_SECRET_FILE", null);
        }
    }
}
