# Arquitetura — FiscalAPI (visão completa, 1.3.0-alpha)

> Documento vivo. Última revisão: 2026-09-04. O README tem o quickstart e a
> referência de endpoints; este documento explica **como o sistema funciona
> por dentro** e por que.

## 1. Visão geral

API open source .NET 10 para emissão fiscal (NF-e 55, NFC-e 65, NFS-e
Nacional/DPS), **multi-tenant**, consumida por ERP (não por usuário final).
A API **não calcula tributos** — o integrador envia valores já calculados; a
API valida consistência, monta o XML, assina, transmite à SEFAZ e entrega o
resultado (REST + webhooks assinados).

```
ERP ──HTTPS──> Fiscal.Api ──202──┐
                  │ (Hangfire)   │
                  ▼              ▼
            Fiscal.Worker ──> SEFAZ/SVC (Unimake.DFe)
                  │              │
                  ├──> Postgres (estado + outbox)
                  └──> Webhooks HTTPS (HMAC-SHA256) ──> ERP
```

## 2. Projetos e regra de dependências

| Projeto | Responsabilidade | Pode referenciar |
|---|---|---|
| `Fiscal.Core` | Domínio: entidades, enums, interfaces, serviços puros (validador, webhooks/payload/assinador, gerador de chaves) | **nada** (sem EF/Unimake/Hangfire/QuestPDF) |
| `Fiscal.Persistence` | EF Core + Postgres (mapeamento explícito), envelope AES-GCM, repositórios, migrations hand-written | Core |
| `Fiscal.Adapters.Unimake` | `IEmissorFiscal` (Mock/NFe/NFCe/NFSe), eventos, inutilização, distribuição DFe, manifestação, consulta protocolo, status-serviço, mapper `EnviNFe` | Core + Unimake.DFe |
| `Fiscal.Pdf` | DANFE/DANFCe/DANFSe (QuestPDF, licença Community, layout simplificado) | Core |
| `Fiscal.Api` | HTTP, auth dupla (ApiKey + JWT admin), FluentValidation, rate limit, webhooks outbox (gravação), SPA do painel | Core, Persistence, Adapters, Pdf, Worker |
| `Fiscal.Worker` | Host do Hangfire + todos os jobs + `DespachanteWebhookHttp` | Core, Persistence, Adapters |
| `frontend/` | Painel admin (React 19 + Vite + TS strict + Tailwind v4) | consome `/v1/admin/*` |

Padrão transversal: **interface no Core, implementação real no adapter, mock
para sandbox/testes** (`EmissorMock`, `TransmissorEventoMock`,
`ConsultaDistribuicaoMock`, `ConsultaStatusServicoMock`,
`ConsultaProtocoloMock`, `TransmissorManifestacaoMock`).

## 3. Fluxo de emissão (NF-e/NFC-e)

1. `POST /v1/documentos-fiscais/nfe|nfce` (API key, `Idempotency-Key`):
   valida (FluentValidation + `ValidadorConsistenciaFiscal` — aritmética,
   tolerância 0,01) → reserva número atômico (`sequencias_numeracao`,
   `UPDATE…RETURNING` no Postgres) → grava `documentos_fiscais` PENDENTE com
   payload JSONB → enfileira → **202** `{id, status, links}`.
2. `ProcessarDocumentoJob` (Worker): carrega doc + tenant + certificado
   descriptografado → resolve emissor (Mock em sandbox; real por tipo) →
   transmite → persiste resultado (chave, protocolo, XMLs, recibo).
3. Falha de transmissão → `CONTINGENCIA` + backoff 30s/1m/2m/5m/10m;
   `VarrerContingenciaJob` reenfileira. Rejeição/denegação não reprocessam.
4. Erros de mapeamento/config → `ErroNaoRecuperavelException` →
   `ERRO_INTERNO` (sem retry — nunca vira fila infinita).

**Contingência SVC** (config `Fiscal:Contingencia:*`, desligada por padrão):
em falha ambígua (timeout/rede), o emissor consulta o **protocolo** da chave
determinística (dhEmi = `CriadoEm`, cNF derivado do id) — SEFAZ processou?
recupera sem reenviar. Não processou? marca `ModoContingencia` ("SVCAN"/
"SVCRS") e o retry reemite via SVC (tpEmis 6/7) com a config roteando ao WS
certo. A chave muda de tpEmis — por isso a consulta de protocolo precede a
troca (evita duplicidade).

**NFe (55) é assíncrona em duas fases**: lote → recibo persistido
(`recibo_lote`) → `NFeRetAutorizacao` no retry (cStat 105/106 reconsultam sem
reenviar o lote). **NFC-e (65) é síncrona** (`indSinc=1`), exige CSC/IdCSC do
tenant (cifrado); QR code gerado pelo adapter Unimake.

## 4. Eventos (cancelamento/CC-e/inutilização)

`POST .../cancelamento|carta-correcao|/v1/inutilizacoes` grava `EventoFiscal`
PENDENTE → `ProcessarEventoJob` transmite (`RecepcaoEvento` 110111/110110,
`InutNFe`) → PROCESSADO (cancelamento → documento `CANCELADA`; CC-e mantém
AUTORIZADA) / REJEITADO (cancelamento → `ERRO_CANCELAMENTO`) / PENDENTE com
backoff (`VarrerEventosJob`). Inutilização carrega modelo/série/faixa em
`dados_evento` (jsonb). NFS-e não usa evento — usa substituição de DPS
(`POST /v1/documentos-fiscais/{id}/substituicao`).

## 5. Webhooks (outbox + HMAC)

Gravação na **mesma transação** da mudança de status (outbox pattern):
`documento.autorizado|rejeitado|denegado|cancelado|carta_correcao`,
`nota.recebida`, `manifestacao.processada`, `certificado.vencendo`.
`VarrerWebhooksJob` (60 s) enfileira `ProcessarWebhookJob` → POST com
`X-Fiscal-Timestamp` + `X-Fiscal-Signature: sha256=HMAC-SHA256(secret,
"{timestamp}.{payload}")`. Retry próprio (1m→5m→15m→1h→6h→6h→24h, 8 tentativas
→ FALHA). Consumidor valida janela de 5 min (anti-replay).

## 6. Distribuição DFe + Manifestação

`SincronizarDistribuicaoDFeJob` (60 s) consulta por tenant + ambiente a partir
do `ultimo_nsu`, grava `notas_recebidas` (resNFe/procNFe, `ExtratorDfe` faz o
parsing defensivo), webhook `nota.recebida`. `POST /v1/notas-recebidas/{id}/
manifestacao` (210200/210210/210220/210240, idempotente) →
`ProcessarManifestacaoJob` → `RecepcaoEvento` com cOrgao = cUF da chave →
webhook `manifestacao.processada`. Endpoints de listagem/detalhe/`xml-completo`.

## 7. NFS-e (padrão Nacional)

Envelope completo (`POST /v1/documentos-fiscais/nfse`, modelo interno 115 —
não é código SEFAZ; o DPS não usa modelo), numeração/fila/idempotência/sandbox
iguais ao resto, PDF simplificado, cancelamento 409 (usa substituição).
`EmissorNFSe` real (transmissão DPS) implementado — fora do sandbox exige
credenciamento do prestador homologado.

## 8. Jobs (Worker)

| Job | Recorrência | Função |
|---|---|---|
| `ProcessarDocumentoJob` | enfileirado | emissão (doc PENDENTE/CONTINGENCIA) |
| `VarrerContingenciaJob` | 30 s | reenfileira emissões com retry vencido |
| `ProcessarEventoJob` | enfileirado | cancelamento/CC-e/inutilização |
| `VarrerEventosJob` | 30 s | reenfileira eventos em backoff |
| `ProcessarWebhookJob` | enfileirado | entrega HMAC de webhook |
| `VarrerWebhooksJob` | 60 s | drena a outbox |
| `SincronizarDistribuicaoDFeJob` | 60 s | NSU por tenant/ambiente |
| `ProcessarManifestacaoJob` | enfileirado | manifestação do destinatário |
| `VarrerManifestacoesJob` | 30 s | reenfileira manifestações em backoff |
| `AlertarCertificadosVencendoJob` | diário 12:00 | webhook `certificado.vencendo` (≤15 dias, 1/dia) |

Sandbox sem certificado: os jobs seguem com X509 null (mocks não assinam).
Produção sem certificado ativo → falha alto.

## 9. Segurança

- **Dois esquemas de auth**: `ApiKey` (tenants, prefixo de 12 chars no banco
  para lookup + PBKDF2-SHA256 100k iterações com `FixedTimeEquals`; fallback
  legado SHA-256) e `JwtBearer` (painel admin, política `Admin`/role `admin`,
  segredo ≥ 32 chars, clock skew 1 min). Login admin tem hash dummy para
  equalizar tempo de resposta (não vaza existência de conta).
- **Certificados**: PFX + senha cifrados com envelope AES-256-GCM (DEK
  aleatória por certificado, DEK cifrada com KEK — `ChaveMestraKEK`, 32 bytes).
  KEK fora do banco; backup separado (docs/backup-dr.md).
- **Webhooks**: assinatura HMAC-SHA256 por tenant + timestamp.
- **Rate limit global** por IP (`Fiscal:RateLimit:PorMinuto`, default 100/min,
  429).
- **Auditoria** append-only (migration de REVOKE; Acao/IpOrigem/Detalhe jsonb)
  para eventos de negócio e falhas de autenticação.
- Revisão completa e achados: **docs/revisao-seguranca.md**.

## 10. Dados (Postgres; SQLite in-memory nos testes)

`tenants` (perfil fiscal do emitente + webhook + CSC cifrado), `api_keys`
(prefixo 16 + hash), `certificados` (envelope), `documentos_fiscais` (payload
jsonb, XMLs, recibo, modo_contingencia), `sequencias_numeracao` (PK composta),
`eventos_fiscais` (+dados_evento), `outbox_webhooks`, `notas_recebidas`,
`manifestacoes`, `ultimo_nsu`, `admin_users`, `auditoria`.
Migrations hand-written **com** `[DbContext]`/`[Migration]`; snapshot mantido
manualmente.

## 11. Configuração principal

| Chave | Default | Uso |
|---|---|---|
| `Certificados:ChaveMestraKEK` | — (obrigatória) | KEK do envelope |
| `Fiscal:ModoSandbox` | true (API e Worker) | mock vs transmissão real |
| `Fiscal:Contingencia:Habilitada` | false | troca automática p/ SVC |
| `Fiscal:Contingencia:Modo` | SVCAN | SVCAN ou SVCRS |
| `Fiscal:RateLimit:PorMinuto` | 100 | rate limit global por IP |
| `Fiscal:Redis:ConnectionString` | — | cache + rate limit distribuído |
| `Fiscal:Cors:Origens` | localhost 5173/4173 | origens CORS (produção: configurar) |
| `Fiscal:Observabilidade:MetricsAnonimos` | false | `/metrics` sem auth (opt-in) |
| `Fiscal:Proxies:KnownProxies` | loopback | proxies confiáveis p/ X-Forwarded-For |
| `Fiscal:Webhooks:PermitirRedesPrivadas` | false | opt-out do guarda SSRF |
| `Fiscal:RodarMigrations` | dev: true | migrations fora de dev só com flag |
| `Fiscal:RespTec:Cnpj/Contato/Email/Fone` | — | grupo infRespTec (972 em algumas SEFAZ) |
| `Certificados:ChaveMestraKEKAnterior` | — | KEK anterior durante rotação |
| `ADMIN_JWT_SECRET` | — (painel) | ≥ 32 chars |

## 12. Testes

224 testes (128 unitários em `Fiscal.Core.Tests` — mapper EnviNFe, contingência,
extrator DFe, assinatura webhook, PDF, validador, mocks; 96 de integração em
`Fiscal.Api.Tests` — emissão ponta a ponta, eventos, webhooks, distribuição +
manifestação, admin, **segurança** (PBKDF2, envelope AES-GCM, API keys
criar/usar/revogar, upload de certificado real, idempotency guard)).
Classes de teste rodam em `[Collection("fiscal-db")]` (SQLite compartilhado
não é seguro para paralelismo entre classes).
