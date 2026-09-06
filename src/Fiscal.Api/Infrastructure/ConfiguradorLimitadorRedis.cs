using Microsoft.Extensions.Options;
using StackExchange.Redis;
using RateLimiterOptions = Microsoft.AspNetCore.RateLimiting.RateLimiterOptions;

namespace Fiscal.Api.Infrastructure;

/// <summary>
/// Sobrescreve o GlobalLimiter in-memory pelo particionado em Redis quando
/// Fiscal:Redis:ConnectionString está configurado (mesma janela/limite).
/// </summary>
internal class ConfiguradorLimitadorRedis(IConnectionMultiplexer redis, long limite)
    : IPostConfigureOptions<RateLimiterOptions>
{
    public void PostConfigure(string? name, RateLimiterOptions options)
    {
        options.GlobalLimiter = LimitadorRedisParticionado.Criar(redis, limite, TimeSpan.FromMinutes(1));
    }
}
