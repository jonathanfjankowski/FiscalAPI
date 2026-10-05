using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Fiscal.Api.Infrastructure;

/// <summary>
/// Rate limiter de janela fixa distribuído via Redis (INCR + PEXPIRE atômicos),
/// usado quando Fiscal:Redis:ConnectionString está configurado — permite
/// múltiplas instâncias da API compartilhando o mesmo limite por chave/IP.
/// Falha aberta (limite liberado) se o Redis estiver inacessível no momento
/// da contagem: disponibilidade acima do rate limiting.
/// </summary>
public class LimitadorRedis : RateLimiter
{
    // INCR; se for a primeira contagem da janela, define o TTL. Atômico no Redis.
    private const string Script =
        """
        local atual = redis.call('INCR', KEYS[1])
        if atual == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return atual
        """;

    private readonly IDatabase _db;
    private readonly long _limite;
    private readonly TimeSpan _janela;
    private readonly string _chaveBase;
    private int _concedidasCache;

    public LimitadorRedis(IDatabase db, string chaveBase, long limite, TimeSpan janela)
    {
        _db = db;
        _chaveBase = chaveBase;
        _limite = limite;
        _janela = janela;
    }

    public override TimeSpan? IdleDuration => _janela;

    public override RateLimiterStatistics GetStatistics() => new()
    {
        TotalSuccessfulLeases = _concedidasCache,
    };

    protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
        // Contagem síncrona não existe no cliente Redis async-only — o caminho
        // async (AcquireAsyncCore) é quem limita de verdade.
        new LimitadorLease(true, null);

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount, CancellationToken cancellationToken)
    {
        try
        {
            var janelaAtual = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                              / (long)_janela.TotalMilliseconds;
            var resultado = (long)(await _db.ScriptEvaluateAsync(
                Script,
                [$"{_chaveBase}:{janelaAtual}"],
                [(long)_janela.TotalMilliseconds]));

            if (resultado <= _limite)
            {
                Interlocked.Increment(ref _concedidasCache);
                return new LimitadorLease(true, null);
            }

            // Retry após o fim da janela atual.
            var decorrido = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            % (long)_janela.TotalMilliseconds;
            return new LimitadorLease(
                false, TimeSpan.FromMilliseconds(_janela.TotalMilliseconds - decorrido));
        }
        catch (Exception)
        {
            // Redis fora: falha aberta — não derruba a API por causa do limitador.
            return new LimitadorLease(true, null);
        }
    }

    private sealed class LimitadorLease : RateLimitLease
    {
        public LimitadorLease(bool concedido, TimeSpan? retryAfter)
        {
            IsAcquired = concedido;
            if (retryAfter is { } r)
                RetryAfter = r;
        }

        public override bool IsAcquired { get; }

        public TimeSpan? RetryAfter { get; }

        public override IEnumerable<string> MetadataNames =>
            RetryAfter is null ? [] : ["RetryAfter"];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == "RetryAfter" && RetryAfter is { } r)
            {
                metadata = r;
                return true;
            }
            metadata = null;
            return false;
        }
    }
}

/// <summary>
/// PartitionedRateLimiter de janela fixa em Redis — partição = API key
/// (prefixo de 16 chars) ou IP quando não autenticado (pendência 3 de
/// docs/revisao-seguranca.md: rate limit por API key para múltiplas instâncias).
/// </summary>
public static class LimitadorRedisParticionado
{
    public static PartitionedRateLimiter<HttpContext> Criar(
        IConnectionMultiplexer redis, long limite, TimeSpan janela) =>
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.Get(
                ChaveDe(httpContext),
                chave => new LimitadorRedis(redis.GetDatabase(), chave, limite, janela)));

    public static string ChaveDe(HttpContext httpContext)
    {
        var auth = httpContext.Request.Headers.Authorization.ToString();
        const string prefixoEsquema = "ApiKey ";
        if (auth.StartsWith(prefixoEsquema, StringComparison.OrdinalIgnoreCase))
        {
            var chave = auth[prefixoEsquema.Length..].Trim();
            if (chave.Length > 0)
                return "rl:key:" + chave[..Math.Min(16, chave.Length)];
        }

        return "rl:ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon");
    }
}
