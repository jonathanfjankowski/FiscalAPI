using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Fiscal.Api.Infrastructure;

/// <summary>
/// Secret manager plugável via convenção de arquivos (Docker secrets / K8s
/// mounted files / Vault com sidecar): toda env <c>CHAVE_FILE</c> aponta o
/// caminho do arquivo cujo conteúdo vira o valor de <c>CHAVE</c>.
/// Ex.: <c>ADMIN_JWT_SECRET_FILE=/run/secrets/admin_jwt_secret</c> e
/// <c>CERTIFICADOS__CHAVEMESTRAKEK_FILE=/run/secrets/kek</c>.
/// Prioridade: o provider é adicionado por último — valor direto da env
/// ganha do arquivo; arquivo ganha dos appsettings.
/// </summary>
public sealed class FileSecretsConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new FileSecretsConfigurationProvider();
}

public sealed class FileSecretsConfigurationProvider : ConfigurationProvider
{
    public override void Load()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var chave = entry.Key as string;
            var arquivo = entry.Value as string;

            if (string.IsNullOrEmpty(chave) || !chave.EndsWith("_FILE", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(arquivo) || !File.Exists(arquivo))
                continue;

            var chaveConfig = chave[..^"_FILE".Length].Replace("__", ":");
            Data[chaveConfig] = File.ReadAllText(arquivo).TrimEnd('\r', '\n');
        }
    }
}

public static class FileSecretsConfigurationExtensions
{
    public static IConfigurationBuilder AddFileSecrets(this IConfigurationBuilder builder) =>
        builder.Add(new FileSecretsConfigurationSource());
}
