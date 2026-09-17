# Status atual — FiscalAPI

> Snapshot de 2026-09-06 (2ª revisão), gerado a partir do `CHANGELOG.md`,
> `docs/roadmap.md` e `README.md`. Este documento resume **o que está pronto,
> o que falta e onde o projeto está** — para a lista cronológica detalhada,
> veja o `CHANGELOG.md`; para o que vem depois do 1.0, veja `docs/roadmap.md`.

## Identidade

| | |
|---|---|
| **Versão atual** | `1.10.0-alpha` (2026-09-06) |
| **Estágio** | Alpha — funcional ponta a ponta em sandbox; emissão real NF-e/NFC-e/NFS-e(DPS) implementada, homologação contra SEFAZ pendente de certificado A1 |
| **Stack** | .NET 10 (API + Worker Hangfire), PostgreSQL/EF Core, React 19 + Vite + Tailwind v4 (painel admin), Unimake.DFe (adapters SEFAZ), QuestPDF (DANFE/DANFCe) |
| **Testes** | 173/173 (116 unitários em `Fiscal.Core.Tests`, 57 de integração em `Fiscal.Api.Tests`) |
| **Licença** | MIT — licença da `Unimake.DFe` **confirmada (MIT)**, sem bloqueios de licença |
| **Repositório** | https://github.com/jonathanfjankowski/FiscalAPI |

## O que está funcionando (resumo por capacidade)

- **Emissão de NF-e (modelo 55)** — fluxo real via Unimake.DFe,
  assíncrono em duas fases (lote → recibo → consulta de retorno), mapper
  do layout 4.00 com **ICMS completo** (CST 00–90 e CSOSN 101–900 —
  Simples Nacional emite — com ST, FCP e DIFAL, via grupo `impostosV2`).
  Em sandbox, `EmissorMock` devolve chave determinística sem certificado.
- **Emissão de NFC-e (modelo 65)** — síncrona (`indSinc=1`), exige
  CSC/IdCSC do tenant (cifrado com KEK, AES-GCM); QR code montado pelo
  adapter Unimake.
- **NFS-e padrão Nacional (DPS)** — envelope REST completo (numeração
  interna modelo 115, fila, idempotência, PDF simplificado, sandbox via
  mock) **e transmissão DPS real** (layout 1.01 síncrono, rota
  `POST /nfse/dps`) com **substituição** (`POST {id}/substituicao`).
  Credenciamento + homologação ficam na trilha manual do README.
- **Eventos** — cancelamento (110111), carta de correção (110110) e
  inutilização de faixa transmitidos de verdade (`TransmissorEventoUnimake`)
  com ciclo `PENDENTE → PROCESSANDO → PROCESSADO/REJEITADO/ERRO` e backoff.
- **Contingência SVC** — em falha ambígua de transmissão (timeout/rede),
  consulta de protocolo pela chave determinística e reemissão via
  SVC-AN/SVC-RS (`tpEmis 6/7`), configurável em
  `Fiscal:Contingencia:Habilitada/Modo`. EPEC e NFC-e offline (tpEmis 9)
  ficam para depois.
- **Webhooks** — outbox gravada na mesma transação do status, entrega
  assinada HMAC-SHA256 (`X-Fiscal-Signature` + janela anti-replay de 5 min),
  retry próprio `1m/5m/15m/1h/6h/6h/24h` (8 tentativas → `FALHA`).
  Configuração **self-service** pelo próprio integrador
  (`PUT /v1/tenants/webhooks`). Eventos: `documento.autorizado/rejeitado/
  denegado/cancelado/carta_correcao`, `nota.recebida`, `manifestacao.processada`,
  `certificado.vencendo`.
- **DANFE/DANFCe/DANFSe** — PDF via QuestPDF com **barcode CODE-128 da
  chave** e **QR Code do DANFCe** (extraído do XML autorizado), em leiaute
  simplificado (o quadro oficial de 20 campos segue como evolução).
- **Distribuição DFe + Manifestação do Destinatário** — sincronização por
  NSU a cada 60 s no Worker (`notas_recebidas`/`ultimo_nsu`), manifestação
  (210200/210210/210220/210240) com idempotência e webhook.
- **Status-serviço SEFAZ** — `GET /v1/status-servico` com cache de 60 s.
- **Painel admin** — React em `frontend/`: login de operador (JWT 8 h),
  dashboard, CRUD de tenants, API keys (exibição única), certificados com
  alerta de vencimento, documentos (XMLs, polling, cancelar/CC-e) e
  Playground de emissão. Em produção o `dist/` é servido pela própria API.
- **Segurança** — API key com hash PBKDF2-SHA256 (prefixo de 12 chars,
  fallback do legado), ambiente atrelado à chave (403 cross-ambiente),
  certificados em envelope AES-GCM (DEK/KEK), rate limit global por IP
  (100/min default), dashboard `/hangfire` protegido por JWT de admin,
  idempotência com guarda de tamanho. Revisão completa em
  `docs/revisao-seguranca.md`.
- **Observabilidade** — OpenTelemetry com `GET /metrics` (Prometheus),
  métricas de negócio (documentos, latência de autorização, webhooks,
  contingência, eventos) e alertas sugeridos em `docs/observabilidade.md`.
- **Redis (opcional)** — rate limiting distribuído por API key (fail-open)
  e cache compartilhado do status-serviço; sem Redis, in-memory.
- **Operações** — Docker multi-stage (API + Worker, amd64/arm64), backup
  `pg_dump` com retenção (7+4), restore com confirmação, checklist de
  go-live e estratégia DR em `docs/backup-dr.md` (RPO ≤ 24 h, RTO ≤ 1 h).
- **CI/CD** — GitHub Actions: build + testes + format em PR; publicação
  das imagens `ghcr.io` em tags `v*.*.*`.

## Pendências antes do 1.0 (release público)

Em ordem de prioridade (detalhes em `docs/roadmap.md`):

1. **Homologação real NF-e/NFC-e/eventos/NFS-e contra SEFAZ** — exige
   certificado A1; checklist no README (`### Checklist de homologação real`).
   A NFS-e Nacional exige também credenciamento do prestador.
2. ~~Confirmação da licença da `Unimake.DFe`~~ — **feito: confirmada como
   MIT**, bloqueio do release público removido.
3. ~~PITR (WAL archiving) + secret manager plugável~~ — **feito (1.9.0-alpha)**.

## Evolução do contrato (v2) — F1 concluída, F2–F6 planejadas

`docs/plano-evolucao-contrato-v2.md` documenta a evolução do contrato de
emissão de NF-e/NFC-e. **F1 (ICMS completo + CSOSN) e o self-service de
webhook (G10) estão implementados** (1.4.0-alpha): grupo `impostosV2` com
CST 00–90, CSOSN 101–900, ST, FCP e DIFAL. Restam: F2 (item rico/totais),
F3 (IPI/PIS/COFINS), F4 (NF-ref/devolução), F5 (reforma IBS/CBS/IS) e F6
(DANFE oficial). Princípio: **evolução aditiva no mesmo endpoint**
(campos novos opcionais; payload atual continua emitindo igual; sem rota
`/v2`). A API **continua sem calcular tributos**.

## Limitações conhecidas (resumo)

A lista completa e justificada está no README (`Limitações conhecidas`).
As principais:

- Mapper NF-e cobre ICMS completo + IPI/PIS/COFINS + item rico (GTIN,
  CEST, unidade, desconto, frete/seguro/outras) + NF-ref/devolução +
  reforma IBS/CBS/IS (NT 2025.x) via `impostosV2`. Fica para depois:
  transporte/volumes (backlog v2). Combinações não suportadas **falham
  alto** em vez de transmitir errado.
- PDF **simplificado** (sem código de barras/QR do leiaute oficial) — F6.
- Certificado **A1 apenas** (A3/HSM fora de escopo).
- NFS-e real exige credenciamento do prestador na SEFAZ Nacional +
  certificado A1 (homologação manual).
- Sem página de reenvio manual de webhooks (consultável via
  `outbox_webhooks`).
- Homologação real exige certificado A1 válido (não existe certificado
  "de teste" separado na SEFAZ).

## Health do projeto

| Indicador | Estado |
|---|---|
| Build + testes no CI | ✅ verde (`build-and-test.yml`) |
| Cobertura de testes automatizados | 173 testes — segurança, ICMS/CSOSN, IPI/PIS/COFINS, NF-ref/devolução, reforma IBS/CBS/IS, DPS/substituição, contingência, DFe, webhooks, painel, métricas, secrets |
| `dotnet format --verify-no-changes` | ✅ exigido no CI |
| Migrations | Aplicadas automaticamente no startup (hand-written com atributos `[Migration]`/`[DbContext]`) |
| Documentação | README + 18 docs em `docs/` + CHANGELOG completo 0.1 → 1.8 |
| Dívida documentada | EPEC/NFC-e offline, leiaute visual completo do DANFE, backlog v2 (transporte, batch, ICMSPart) |
