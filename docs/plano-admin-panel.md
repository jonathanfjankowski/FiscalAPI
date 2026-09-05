# Plano — Admin Panel (FiscalAPI)

> **Status:** ✅ implementado (2026-09-03). Ver README (seção "Admin Panel")
> e CHANGELOG 0.4.0-alpha. O smoke test em Postgres real expôs e corrigiu
> ainda três bugs pré-existentes: migrations hand-written sem `[Migration]`/
> `[DbContext]` (nunca aplicadas pelo `Migrate()`), `key_hash` varchar(64)
> insuficiente para o PBKDF2 (criação de API key falhava em Postgres) e o
> NETSDK1152 no `dotnet publish`.

## Contexto
API .NET 10 multi-tenant (NF-e/NFC-e) em v0.3.0-alpha, sem nenhum frontend hoje. O painel resolve gaps reais: criar tenant é SQL manual no psql, gerir API keys exige `curl`, não há como visualizar documentos/XML sem ir ao banco, e o dashboard Hangfire está exposto sem auth.

**Decisões tomadas no planejamento:**
- Stack: **React 19 + Vite + TS + Tailwind v4 + shadcn/ui + TanStack Query + React Router**
- Auth de operador: **login → JWT** (tabela `admin_users`, seed via env) — o "fluxo admin" que o README já promete
- MVP = **Tenants + API Keys + Documentos + Playground + Certificados**

## Fase B1 — Backend: auth de admin + CORS (pré-requisito)
Em `src/Fiscal.Api`:

1. **Entidade + migration**: `AdminUser` (Id, Email, SenhaHash, Ativo, CriadoEm) → tabela `admin_users`. Reusar o helper PBKDF2 de `ApiKeyAuthenticationHandler.HashKey`.
2. **Endpoints de auth**: `POST /v1/admin/auth/login` (`{email, senha}` → `{token, expiraEm}`) com JWT (Microsoft.AspNetCore.Authentication.JwtBearer), claims `sub` + `role: admin`, expiração 8h (config `Admin:JwtSecret` / `Admin:TokenHoras`).
3. **Seed**: no startup (Development), se tabela vazia e env `ADMIN_EMAIL`/`ADMIN_PASSWORD` definidas, cria o primeiro admin.
4. **Autorização**: policy `Admin` exigindo role `admin`; aplicar a `/v1/admin/*`. Auth por API key dos endpoints de tenant permanece intacta.
5. **CORS**: policy de Development permitindo `http://localhost:5173` (e porta do Vite).
6. **Hangfire**: `IAuthorizationFilter` que exige JWT de admin (corrige o dashboard exposto).

## Fase B2 — Backend: endpoints admin
Todos sob `/v1/admin/*`, `[Authorize(Policy="Admin")]`, escopo cross-tenant:

- `GET/POST/PUT /v1/admin/tenants` (+ `DELETE` = soft `Ativo=false`) — criação hoje inexistente via API.
- `GET /v1/admin/tenants/{id}/api-keys` + `POST` (gera, retorna chave em claro 1x) + `DELETE /{keyId}` (revoga) — reusar lógica de `ApiKeysController`.
- `GET /v1/admin/documentos-fiscais?tenantId&status&modelo&de&ate&page` — listagem paginada (nova query no repositório).
- `GET/POST /v1/admin/tenants/{id}/certificados` — listar metadados + upload (reusar `CertificadosController`).
- `GET /v1/admin/dashboard` — contagens por status (hoje/7 dias), tenants ativos, docs em contingência.
- Testes de integração em `tests/Fiscal.Api.Tests` cobrindo login (401/200), 403 sem role, CRUD tenants, e geração/revogação de key via admin.

## Fase F1 — Frontend: scaffold
Pasta `frontend/` no monorepo:

- Vite + React 19 + TS, Tailwind v4, shadcn/ui (tema claro/escuro), React Router, TanStack Query.
- `src/lib/api.ts`: fetch wrapper tipado (baseURL por env `VITE_API_URL`, injeta JWT, trata 401 → logout, RFC 7807 → toast de erro).
- `vite.config.ts`: proxy `/v1` e `/hangfire` → `http://localhost:5039`.
- Layout do painel: sidebar (Dashboard, Tenants, Documentos, Playground, Certificados) + header com ambiente/tenant ativo e logout.
- Página Login (JWT em memória/localStorage com expiração).

## Fase F2 — Tenants + API Keys
- **Tenants**: tabela (CNPJ, razão social, UF, ambiente padrão, ativo) + formulário criar/editar (validação client-side espelhando FluentValidation: CNPJ 14 dígitos, UF, código IBGE, regime).
- **Detalhe do tenant**: abas — API keys (gerar → modal "copie agora, não será exibida novamente" com o prefixo `fk_live_/fk_test_`; revogar com confirmação), certificados, dados de webhook.
- Documentação: nova seção no README para os endpoints `/v1/admin/*`.

## Fase F3 — Documentos fiscais
- **Lista**: badges de status coloridos (AUTORIZADA verde, REJEITADA vermelho, CONTINGENCIA âmbar, PENDENTE azul…), filtros por tenant/status/modelo/período, paginação, auto-refresh (refetch a cada 5s quando houver docs transitórios).
- **Detalhe**: timeline de status (PENDENTE → AUTORIZADA/…), chave de acesso/protocolo com copiar, tabs de XML (assinado / retorno SEFAZ, com syntax highlight leve), ações: **cancelar** e **carta de correção** (modal com justificativa, respeitando regras 409 da API).

## Fase F4 — Playground (o "testar" da API)
- Seletor tenant/modelo/ambiente; formulário de emissão NF-e/NFC-e (emitente herdado do tenant, destinatário, itens com valores/impostos, cálculo automático dos totais para passar no `ValidadorConsistenciaFiscal`) **ou** modo "colar JSON cru".
- Gera `Idempotency-Key` (uuid) automaticamente; botão Emitir mostra o 202 + link de consulta; painel faz polling do status até terminal, exibindo as transições ao vivo.
- Histórico local das emissões de teste da sessão.

## Fase F5 — Certificados + Dashboard home
- **Certificados**: upload .pfx (multipart, senha), tabela com vencimento e alerta < 30 dias (o índice `valido_ate` já existe).
- **Dashboard home**: cards com contagens de `/v1/admin/dashboard` (emitidos hoje, autorizadas/rejeitadas, em contingência) + link para o Hangfire (autenticado).

## Fase D — Deploy e documentação
- Build do SPA → servido pela própria API: `UseStaticFiles` + fallback para `index.html` (map após o `/v1`), `frontend/dist` copiado no Dockerfile da API. Alternativa mantida: dev via Vite com proxy.
- README: seção "Admin Panel" (como rodar `npm run dev`, seed `ADMIN_EMAIL`/`ADMIN_PASSWORD`), CHANGELOG 0.4.0-alpha.

## Ordem e verificação
B1 → B2 → F1 → F2 → F3 → F4 → F5 → D.

Em cada fase: `dotnet test` (19 testes existentes + novos), `npm run build` sem erros de tipo. Verificação final: subir API+Worker (docker compose ou `dotnet run`), login no painel, criar tenant, gerar API key, emitir NF-e de teste no playground (sandbox/EmissorMock) e acompanhar até AUTORIZADA.

**Fora do escopo do MVP** (backlog): trilha de auditoria na UI (endpoint ainda não existe), gestão de múltiplos operadores/roles, recuperação de senha, webhooks dispatcher.
