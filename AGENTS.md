# AGENTS.md — FiscalAPI

API REST open source para emissão de **NF-e (55), NFC-e (65) e NFS-e Nacional (DPS)** —
multi-tenant, assíncrona, .NET 10. Recebe dados **já calculados** do ERP, monta o XML,
assina com certificado A1 e transmite à SEFAZ. Projeto em **alpha**; docs e commits em **pt-BR**.

**Decisão de escopo central: a API NÃO calcula tributos.** Valores vêm prontos do ERP;
a API valida estrutura e aritmética — nunca inventa valores. Não adicione cálculo de imposto.

## Comandos

```bash
dotnet build                                  # TreatWarningsAsErrors=true
dotnet test                                   # 224 testes (Core unit + Api integração)
dotnet format --verify-no-changes             # os 3 acima são obrigatórios antes de PR (CI roda os 3)

dotnet run --project src/Fiscal.Api           # API em http://localhost:5039
dotnet run --project src/Fiscal.Worker        # host Hangfire (jobs de emissão/eventos/webhooks/DFe)

cd frontend && npm run dev                    # painel admin em :5173 (proxy /v1 → :5039)
cd frontend && npm run build                  # tsc -b + vite build (strict)
cd frontend && npm run lint                   # oxlint

docker compose -f docker/docker-compose.yml --env-file .env up -d   # Postgres + API + Worker (:8080)
```

- SDK pinado em `global.json` (.NET 10); `dotnet-ef` é ferramenta local (`dotnet-tools.json`).
- Versão única em `VersionPrefix` do `Directory.Build.props` — releases bumpam lá.
- Em dev, `MODO_SANDBOX=true` no `.env` faz tudo usar `EmissorMock` (sem certificado/CSC/SEFAZ).
  Para emissão real, `MODO_SANDBOX=false` na **API e no Worker**.

## Arquitetura (regras não negociáveis)

| Projeto | Papel |
|---|---|
| `Fiscal.Core` | Domínio (entidades, enums, interfaces). **Zero deps**: sem Unimake, EF, Hangfire ou QuestPDF. |
| `Fiscal.Persistence` | EF Core + PostgreSQL; criptografia de certificados (envelope AES-GCM DEK/KEK). |
| `Fiscal.Adapters.Unimake` | Tudo que fala com SEFAZ/Unimake, atrás de `IEmissorFiscal` (`EmissorNFe/NFCe/NFSe` + `EmissorMock` p/ sandbox). |
| `Fiscal.Api` | HTTP, auth por API Key, FluentValidation, rate limit, health checks, serve o `dist/` do painel em produção. |
| `Fiscal.Worker` | Jobs Hangfire: `ProcessarDocumentoJob`, eventos, webhooks (outbox), Distribuição DFe, contingência SVC. |
| `Fiscal.Pdf` | DANFE/DANFCe/DANFSe via QuestPDF. |

- O core não conhece infra: adapters/fila/PDF ficam atrás de interfaces em Core.
- **Falha alto em vez de transmitir errado**: dado obrigatório ausente →
  `ErroNaoRecuperavelException` (`ERRO_INTERNO`), nunca payload "mais ou menos".
- Ambiente vem **atrelado à API key** (homologação não emite em produção → `403`).
- Todo `POST` que cria documento/evento exige header `Idempotency-Key`.

## Gotchas

- **Migrations hand-written** precisam dos atributos `[DbContext(...)]` e `[Migration("...")]`,
  senão o `Migrate()` não as aplica (já custou um smoke test).
- Testes de integração rodam **serializados** (`[Collection]`) sobre um SQLite compartilhado:
  não use `OrderBy` nem comparação de `DateTimeOffset` em queries EF (não traduz no SQLite).
- Tipos da Unimake: `double` na maioria dos valores monetários, `decimal` em quantidade/unidade.
- NF-e é **assíncrona em duas fases** (lote → recibo → consulta); NFC-e é síncrona e exige
  CSC/IdCSC do tenant. Chave de acesso e cDV são calculados pela Unimake.
- Retry de webhook (outbox) é **separado** do retry de SEFAZ — não misturar os fluxos.
- Frontend: React 19 + Vite + Tailwind v4; em produção o `dist/` é servido pela própria API
  (mesma origem, sem CORS).

## Convenções

- Commits: Conventional Commits em pt-BR — `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.
- Erros da API padronizados em `application/problem+json` (RFC 7807).
- Docs do repo são a fonte da verdade — ler antes de mexer em área sensível:
  - `docs/arquitetura.md` — como funciona por dentro
  - `docs/integracao-api.md` — referência de endpoints/DTOs (manter em sync ao mudar contrato)
  - `docs/status-atual.md` — o que está pronto/faltando
  - `docs/plano-evolucao-contrato-v2.md` — evolução do contrato (impostosV2, reforma IBS/CBS)
  - `docs/revisao-seguranca.md` — padrões de segurança (PBKDF2, AES-GCM, rate limit)
  - `docs/guia-primeira-emissao.md` (seção 9) — homologação real contra SEFAZ
