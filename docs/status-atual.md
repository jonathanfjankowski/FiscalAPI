# Status atual — FiscalAPI

> Snapshot de 2026-09-06, gerado a partir do `CHANGELOG.md`, `docs/roadmap.md`
> e `README.md`. Este documento resume **o que está pronto, o que falta e
> onde o projeto está** — para a lista cronológica detalhada, veja o
> `CHANGELOG.md`; para o que vem depois do 1.0, veja `docs/roadmap.md`.

## Identidade

| | |
|---|---|
| **Versão atual** | `1.4.0-alpha` (2026-09-06) |
| **Estágio** | Alpha — funcional ponta a ponta em sandbox; emissão real NF-e/NFC-e implementada, homologação contra SEFAZ pendente de certificado A1 |
| **Stack** | .NET 10 (API + Worker Hangfire), PostgreSQL/EF Core, React 19 + Vite + Tailwind v4 (painel admin), Unimake.DFe (adapters SEFAZ), QuestPDF (DANFE/DANFCe) |
| **Testes** | 122/122 (81 unitários em `Fiscal.Core.Tests`, 41 de integração em `Fiscal.Api.Tests`) |
| **Licença** | MIT — **sujeito à confirmação da licença da `Unimake.DFe`** (bloqueio do release público) |
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
  mock). **Transmissão DPS real à SEFAZ Nacional ainda pendente** (fora de
  sandbox falha alto, por design).
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
- **DANFE/DANFCe/DANFSe** — PDF via QuestPDF em layout **simplificado**
  (sem código de barras/QR do leiaute oficial de 20 campos — limitação
  documentada).
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
- **Operações** — Docker multi-stage (API + Worker, amd64/arm64), backup
  `pg_dump` com retenção (7+4), restore com confirmação, checklist de
  go-live e estratégia DR em `docs/backup-dr.md` (RPO ≤ 24 h, RTO ≤ 1 h).
- **CI/CD** — GitHub Actions: build + testes + format em PR; publicação
  das imagens `ghcr.io` em tags `v*.*.*`.

## Pendências antes do 1.0 (release público)

Em ordem de prioridade (detalhes em `docs/roadmap.md`):

1. **Transmissão DPS real da NFS-e Nacional** — última integração
   pendente; o envelope já está coberto pelo sandbox.
2. **Homologação real NF-e/NFC-e/eventos contra SEFAZ** — exige
   certificado A1; checklist no README (`### Checklist de homologação real`).
3. **Confirmação da licença da `Unimake.DFe`** — bloqueio burocrático do
   release público (o MIT do projeto depende dessa checagem).
4. **Substituição de NFS-e**
   (`POST /v1/documentos-fiscais/{id}/substituicao`).
5. **OpenTelemetry/Prometheus** (taxa de rejeição por UF, latência
   `PENDENTE → AUTORIZADA`, docs em contingência) + alertas.
6. **Rate limiting distribuído (Redis)** + cache compartilhado.
7. **PITR (WAL archiving)** para RPO ≤ 5 min + secret manager plugável.

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

- Mapper NF-e cobre ICMS completo via `impostosV2`, mas ainda **um grupo
  de PIS/COFINS/IPI por item não existe** (F3), unidade fixa `UN`, GTIN
  fixo `SEM GTIN` (F2), sem desconto/frete/seguro (F2), sem NF-ref (F4) e
  sem transporte/volumes (backlog). Combinações não suportadas **falham
  alto** em vez de transmitir errado.
- PDF **simplificado** (sem código de barras/QR do leiaute oficial) — F6.
- Certificado **A1 apenas** (A3/HSM fora de escopo).
- NFS-e real exige a transmissão DPS (item 1 das pendências).
- Sem página de reenvio manual de webhooks (consultável via
  `outbox_webhooks`).
- Homologação real exige certificado A1 válido (não existe certificado
  "de teste" separado na SEFAZ).

## Health do projeto

| Indicador | Estado |
|---|---|
| Build + testes no CI | ✅ verde (`build-and-test.yml`) |
| Cobertura de testes automatizados | 122 testes — segurança, ICMS/CSOSN, contingência, DFe, webhooks, painel |
| `dotnet format --verify-no-changes` | ✅ exigido no CI |
| Migrations | Aplicadas automaticamente no startup (hand-written com atributos `[Migration]`/`[DbContext]`) |
| Documentação | README + 17 docs em `docs/` + CHANGELOG completo 0.1 → 1.4 |
| Dívida documentada | EPEC/NFC-e offline, OTel, Redis, PITR, substituição NFS-e, contrato v2 F2–F6 |
