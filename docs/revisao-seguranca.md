# Revisão de segurança — 2026-09-04 (1.3.0-alpha)

Revisão estática do backend (auth, criptografia, controllers, config) e do
painel admin (frontend/), com correções aplicadas nesta release. **Escopo:
revisão de código + testes automatizados — não houve pentest dinâmico nem
análise de dependências em runtime.** Repetir a revisão antes do 1.0 público.

## Corrigidos nesta release

| # | Severidade | Achado | Correção |
|---|---|---|---|
| 1 | **Alta** | `ApiKeysController` e `CertificadosController` faziam `AdicionarAsync` **sem `SaveChanges`** — API key respondida em 201 e certificado "criado" nunca eram persistidos (descoberto ao escrever os testes que faltavam). | `FiscalDbContext` injetado + `SaveChangesAsync` nas mutações (ambos os controllers). Cobertura nova em `SegurancaTests`. |
| 2 | **Alta** | **Rate limiter inaplicado**: política "api" registrada, mas sem `GlobalLimiter` nem `[EnableRateLimiting]` — nenhum limite estava ativo de fato. | `GlobalLimiter` por IP (janela fixa, default 100/min, 429) configurável em `Fiscal:RateLimit:PorMinuto`. |
| 3 | **Média** | **Prefixo de API key constante**: coluna de 8 chars guardava exatamente `fk_live_`/`fk_test_`, então o lookup por prefixo devolvia TODAS as chaves ativas do ambiente e cada request executava PBKDF2 (≈100 ms) para cada uma — vetor de custo de CPU. | Prefixo agora tem 12 chars (migration `ApiKeyPrefixo16` para 16) + fallback de lookup para chaves antigas de 8. |
| 4 | **Média (frontend)** | **401 não deslogava**: o `fetch` limpa o localStorage, mas o estado em memória do `AuthProvider` continuava logado — UI cheia de erros até F5. | Evento global `fiscal:nao-autorizado` → `AuthProvider` limpa a sessão → `RequireAuth` redireciona. |
| 5 | **Média (frontend)** | **Paginação de documentos quebrada**: `setFiltro` apagava `?page=` incondicionalmente — impossível sair da página 1. | `delete('page')` só quando a chave alterada não é `page`. |
| 6 | **Baixa** | `Idempotency-Key` sem limite de tamanho (coluna varchar(100) → estouro virava 500). | 400 claro acima de 100 caracteres. |
| 7 | **Baixa (frontend)** | Classes Tailwind construídas em runtime (`STATUS_COLOR.match(...)`) não geram CSS no Tailwind v4 — gráfico do dashboard sem cores. | Mapa estático `BARRA_POR_STATUS` com classes literais. |
| 8 | **Baixa (frontend)** | Polling do Playground: loops em paralelo brigando pelo mesmo estado e rodando após sair da tela. | Token por emissão + abort no unmount. |
| 9 | **Baixa (frontend)** | `tsconfig` sem `strict` (null-safety não verificada em todo o painel). | `"strict": true` — build limpo. |
| 10 | **Baixa (frontend)** | Filtros de data com fuso inconsistente (`de` em UTC, `ate` em local) e `?page=abc` → `NaN`. | Ambos locais; `parseInt` com fallback 1. |

## Verificado sem achados (pontos positivos)

- **API keys**: PBKDF2-SHA256 (100k iterações, salt 16 B, 32 B de hash) com
  `CryptographicOperations.FixedTimeEquals`; fallback legado auditado.
- **Login admin**: hash dummy equaliza tempo de resposta quando o e-mail não
  existe (não vaza existência de conta); falhas auditadas.
- **JWT admin**: issuer/audience/lifetime validados, clock skew 1 min, segredo
  obrigatório ≥ 32 chars (falha com mensagem clara, não startup quebrado).
- **Certificados**: envelope AES-256-GCM com DEK aleatória por registro +
  KEK externa ao banco; nonce novo por cifração; KEK ausente falha no startup.
- **Upload de PFX**: limite de 10 MB; senha errada → 422 (sem stack vazando).
- **Webhooks**: HMAC-SHA256 por tenant sobre `{timestamp}.{payload}` +
  janela de replay recomendada (5 min) documentada.
- **SQL**: queries dinâmicas usam `FromSqlInterpolated` (parametrizado);
  resto é LINQ/EF.
- **CORS** restrito às origens do Vite; SPA servida same-origin em produção.
- **Hangfire dashboard** protegido por filtro que exige role admin (antes
  estava aberto).
- **Auditoria** append-only (REVOKE documentado na migration) cobre eventos
  de negócio, logins e falhas de autenticação.
- **Frontend**: zero `dangerouslySetInnerHTML`/`innerHTML`/`eval`; XML da
  SEFAZ renderizado como texto; zero `any`; token de operador expira localmente.

## Aceitos/documentados (com mitigações)

| Achado | Risco | Mitigação/decisão |
|---|---|---|
| JWT do Hangfire via query string (`/hangfire?access_token=…`) | Token pode aparecer em access logs/Referer | Restrito ao prefixo `/hangfire`; token de 8 h; dashboard é interno. Alternativa futura: cookie de sessão só para o dashboard. |
| `WebhookSecret` do tenant | Leitura do banco expõe segredo HMAC | ~~texto plano~~ **Cifrado em repouso desde 1.4.0-alpha** (envelope KEK + migração em voo). |
| TLS/HSTS | Tráfego interno HTTP | TLS termina no proxy; HSTS + redireção são item do checklist de go-live (docs/backup-dr.md). |
| `ChaveMestraKEK` default vazio nos appsettings | Falha no startup por design (não sobe sem KEK) | Intencional; produção via env/secret manager. |
| Senha default do Postgres no docker-compose (`fiscal`) | Ambiente de dev | Go-live exige troca (checklist backup-dr.md). |

## Auditoria de 2026-09-24 (correções aplicadas)

| # | Severidade | Achado | Correção |
|---|---|---|---|
| 1 | **Crítica** | `.env.backup-sandbox` com `KEK_MASTER_KEY`/`ADMIN_JWT_SECRET`/`ADMIN_PASSWORD` reais fora do `.gitignore` (repositório open source — um `git add .` versionaria a KEK que cifra todos os certificados). | `.gitignore` ampliado para `.env*` + `!.env.example`; backup apagado; **rotacionar as 3 credenciais** (instruções no relatório da auditoria). |
| 2 | **Alta** | **SSRF cego**: webhook do tenant aceitava qualquer URL e o Worker fazia POST server-side (`169.254.169.254`, `localhost`, `10.x`). | `DespachanteWebhookHttp` intercepta a conexão (`ConnectCallback`) e bloqueia loopback/privadas/link-local — imune a DNS rebinding. Opt-out on-premise: `Fiscal:Webhooks:PermitirRedesPrivadas=true`. `https` obrigatório para webhooks fora do sandbox (`ValidadorWebhookUrl`, tenant e admin). |
| 3 | **Média** | Sem `UseForwardedHeaders`: atrás de proxy TLS, o rate limit por IP colapsava todos os clientes num único balde (DoS do rate limit). | `UseForwardedHeaders` no pipeline com `Fiscal:Proxies:KnownProxies` configurável (default: só loopback). |
| 4 | **Média** | Login admin sem lockout — brute-force dentro do rate limit global. | 5 falhas por (IP, e-mail) em 15 min → 429 (contador no `IDistributedCache`, compatível Redis). |
| 5 | **Média** | Key de homologação podia mintar API key de produção do mesmo tenant. | `POST /v1/api-keys` com key de homologação + `ambiente: "producao"` → 403. |
| 6 | **Média** | Sem rotação de KEK: envelope sem versão/chave (`kid`); girar a KEK exigia re-cifrar tudo manualmente. | Envelope versado (`[0x02, kid]`): `Certificados:ChaveMestraKEKAnterior` mantém o legado legível durante a rotação; legado sem prefixo continua lendo com a KEK atual. |
| 7 | **Baixa** | CORS default incluía origens de dev em produção. | Documentado — produção DEVE configurar `Fiscal:Cors:Origens` (checklist go-live). |
| 8 | **Baixa** | JWT admin em query string do Hangfire e flood anônimo na tabela de auditoria (~100 linhas/min/IP por key inválida) — mantidos com mitigação existente, monitorar. | — |

**Regra de isolamento multi-tenant (padrão obrigatório)**: NÃO há global query
filter (`HasQueryFilter`) por decisão de projeto — jobs do Worker fazem claim
por id sem conhecer o tenant antes (chicken-and-egg), e um filtro com valor
vazio deixaria queries do Worker mudas. Enquanto isso vale a regra abaixo:

> Toda query de `FiscalDbContext` em entidade com `TenantId` DEVE filtrar por
> tenant nos repositórios/controllers (`ObterPorIdAsync(id, tenantId, ...)`).
> Endpoints admin (JWT) são a exceção explícita (cross-tenant por design).
> Code review deve rejeitar query nova de tenant sem o filtro.

## Pendências recomendadas (antes do 1.0 público)

1. Pentest dinâmico básico (OWASP ASVS nível 1–2) contra ambiente de
   homologação, incluindo abuso do rate limit e brute-force de API key.
2. ~~Cifrar `webhook_secret` em repouso (reusar o envelope da KEK).~~
   **Feito (1.4.0-alpha)** — envelope AES-GCM + migração em voo dos
   segredos legados.
3. ~~Rate limit por API key (além do IP) com Redis para múltiplas
   instâncias.~~ **Feito (1.7.0-alpha)** — janela fixa em Redis
   (`LimitadorRedis`), partição por API key caindo para IP, fail-open.
4. ~~`dotnet format` + oxlint no CI (hoje só build/test).~~ **Feito
   (1.8.0-alpha)** — oxlint do painel entrou no `build-and-test.yml`.
5. ~~Revisar logs: XMLs completos nunca devem ser logados em nível Info.~~
   **Verificado (1.8.0-alpha)** — nenhum XML completo em log; apenas
   ids/status/motivos.
6. ~~Substituir o e-mail placeholder do SECURITY.md~~ **Feito** (e-mail real
   configurado). Confirmação da licença da Unimake.DFe segue como bloqueio
   do release público.
