# Observabilidade — OpenTelemetry/Prometheus + Redis

> Roadmap item 4 ("OpenTelemetry/Prometheus … + alertas") e item 5
> ("Rate limiting distribuído (Redis) + cache compartilhado").

## Métricas de negócio (meter `FiscalAPI`)

| Métrica | Tipo | Labels | Origem |
|---|---|---|---|
| `fiscal_documentos` | counter | `tipo`, `status`, `uf`, `ambiente` | `ProcessarDocumentoJob` (todo resultado) |
| `fiscal_latencia_autorizacao` | histogram (s) | `tipo`, `uf` | `ProcessarDocumentoJob` — `CriadoEm → AUTORIZADA` |
| `fiscal_eventos` | counter | `tipo`, `status` | `ProcessarEventoJob` |
| `fiscal_webhooks` | counter | `evento`, `resultado` | `ProcessarWebhookJob` |
| `fiscal_contingencia` | counter | `modo` | `ProcessarDocumentoJob` (falha de transmissão) |
| instrumentação padrão | — | — | ASP.NET Core (`http_server_duration`), HttpClient, runtime (GC/threads) |

## Coleta

- **API** (`FiscalAPI`): endpoint **`GET /metrics`** no formato Prometheus
  (`MapPrometheusScrapingEndpoint`) — scrape a cada 15–60 s.
- **Worker** (`FiscalAPI.Worker`): sem endpoint HTTP — configure
  `Fiscal:Observabilidade:OtlpEndpoint` (ex.: `http://otel-collector:4317`)
  para empurrar as métricas por **OTLP** para um collector com exporter
  Prometheus, ou rode um sidecar de scrape.

## Alertas sugeridos (Prometheus rules)

| Alerta | Expressão | Para |
|---|---|---|
| Taxa de rejeição alta por UF | `sum by (uf) (rate(fiscal_documentos{status="REJEITADA"}[15m])) / sum by (uf) (rate(fiscal_documentos[15m])) > 0.2` | 15 min |
| Autorização lenta | `histogram_quantile(0.95, sum by (le, tipo) (rate(fiscal_latencia_autorizacao_bucket[30m]))) > 300` | 30 min |
| Documentos presos em contingência | `increase(fiscal_contingencia[1h]) > 50` | 1 h |
| Webhooks falhando | `sum(rate(fiscal_webhooks{resultado="falha"}[30m])) > 0` | 30 min |
| API fora | `up{job="fiscalapi"} == 0` | 2 min |

## Redis (opcional)

`Fiscal:Redis:ConnectionString` (env `Fiscal__Redis__ConnectionString`; no
compose, `REDIS_CONNECTION_STRING`) habilita:

1. **Rate limiting distribuído** — janela fixa de 1 min com `INCR`+`PEXPIRE`
   atômicos (`LimitadorRedis`), **partição por API key** (prefixo de 16
   chars) caindo para IP quando não autenticado — pendência 3 de
   `docs/revisao-seguranca.md`. Redis fora do ar = falha aberta (não derruba
   a API).
2. **Cache compartilhado** — status-serviço (60 s) via `IDistributedCache`;
   todas as instâncias compartilham as mesmas respostas.

Sem Redis: tudo funciona in-memory (single-node, comportamento do alpha).

## Pendências de segurança cobertas nesta fase

- ~~Rate limit por API key (além do IP) com Redis~~ — **feito** (revisao-seguranca.md §Pendências 3).
