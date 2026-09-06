# Changelog

## [1.4.0-alpha] — 2026-09-06

### Adicionado — contrato v2 F1: ICMS completo + CSOSN (Simples Nacional emite!)
- **Grupo `impostosV2` por item** (evolução aditiva — sem rota `/v2`): CST
  `00/10/20/40/41/51/60/70/90` e **CSOSN `101–900` do Simples Nacional**
  (destrava a maior fatia do varejo, que não emitia), com origem 0–8,
  `modBc`, redução de base, **ICMS-ST própria e retida**, **FCP**
  (próprio/ST/retido), **DIFAL** (`ICMSUFDest`, partilha 100% destino —
  Convênio 190/2017) e crédito do Simples (`pCredSN`/`vCredICMSSN`).
- `ValidadorImpostosV2`: obrigatoriedade por CST/CSOSN, isento × valor e
  aritmética por grupo (tolerância R$ 0,01) → `422` com `campo` exato.
  Item com `impostos` **e** `impostosV2` → `400` (ambíguo).
- `ICMSTot` completo para os novos grupos (`vBCST`, `vST`, `vFCP*`,
  `vICMSUFDest`, `vICMSUFRemet`, `vFCPUFDest`).
- Payload legado (`impostos[]`) continua emitindo igual (ICMS 00/40/41/50).
- **Corrigido (bug legado)**: `ValidadorConsistenciaFiscal` tratava CST
  `00/20/90` (tributados) como isentos — rejeitava emissões com destaque
  válido; a lista de isentos agora é 40/41/50/60.

### Infra — self-service de webhook (G10)
- **`GET/PUT /v1/tenants/webhooks`** — o integrador configura
  `webhookUrl`/`webhookSecret` sem depender do painel admin (URL http(s)
  validada; segredo de 16–200 caracteres).

### Segurança
- **`webhook_secret` cifrado em repouso** (pendência 2 de
  docs/revisao-seguranca.md): mesmo envelope AES-GCM (DEK/KEK) do CSC e dos
  certificados — migration `WebhookSecretCriptografado`; segredos legados em
  texto plano são migrados em voo pelo `ProcessarWebhookJob` e a coluna
  antiga é esvaziada. Painel admin também grava cifrado.

### Testes
- **122/122** (81 + 41). Novos: 22 unitários de ICMS/CSOSN (grupos, ST, FCP,
  DIFAL, validação declarativa) + 7 de integração (emissão com CSOSN 102/DIFAL,
  ambiguidade → 400, isento → 422, self-service de webhook com segredo cifrado,
  migração em voo do segredo legado).

## [1.3.0-alpha] — 2026-09-04

### Segurança (revisão completa em docs/revisao-seguranca.md)
- **Corrigido (alta)**: `ApiKeysController`/`CertificadosController` não
  chamavam `SaveChanges` — chaves e certificados "criados" não eram
  persistidos (achado pelos novos testes de segurança).
- **Corrigido (alta)**: rate limiter registrado mas **nunca aplicado** —
  agora `GlobalLimiter` por IP (100/min default, 429) configurável em
  `Fiscal:RateLimit:PorMinuto`.
- **Corrigido (média)**: prefixo de API key constante (`fk_live_` = 8 chars)
  fazia toda request verificar PBKDF2 de TODAS as chaves do ambiente —
  prefixo passa a 12 chars (migration `ApiKeyPrefixo16`, com fallback para
  chaves antigas).
- `Idempotency-Key` > 100 chars → 400 (antes estourava 500).

### Frontend (painel admin)
- 401 agora desloga de verdade (evento global → redireciona sem F5).
- Paginação de documentos corrigida (era impossível sair da página 1).
- Gráfico do dashboard com cores reais (Tailwind v4 não gera classes
  construídas em runtime); polling do Playground cancelável e sem corrida;
  campo **Webhook secret** no formulário de tenant; `strict` no TypeScript;
  `encodeURIComponent` nos paths; filtros de data no fuso local correto.

### Adicionado — contingência SVC
- **`Fiscal:Contingencia:Habilitada`/`Modo`** (SVCAN/SVCRS): em falha
  ambígua de transmissão, consulta o **protocolo** pela chave determinística
  (dhEmi = `CriadoEm`, agora estável entre tentativas) — SEFAZ autorizou?
  recupera sem reenviar. Não? marca `modo_contingencia` e o retry reemite
  via SVC (tpEmis 6/7). `IConsultaProtocolo` (Unimake/Mock).
- EPEC e NFC-e offline (tpEmis 9) ficam como evolução documentada.

### Testes
- **75/75** (42 + 33). Novos: 3 de contingência (tpEmis/chave determinística),
  9 de segurança (PBKDF2 roundtrip/legado, envelope AES-GCM com KEK errada,
  API key criar→usar→revogar→401, upload real de certificado + 422 senha
  errada, guard de idempotency), classes de integração serializadas
  (`[Collection]`) sobre o SQLite compartilhado e CNPJ de fixture único
  (corrigia corrida de seed).

## [1.2.0-alpha] — 2026-09-03

### Adicionado (Fase 6 — documentação e comunidade)
- **`docs/roadmap.md`** público: tabela de sprints entregues (0.1 → 1.2) e a
  lista "antes do 1.0 público" (DPS real NFS-e, homologação A1, substituição
  NFS-e, OTel/Prometheus, Redis, PITR, licença Unimake).
- **README**: badges de CI/Docker/licença + seção Contribuir/Roadmap.
- **CONTRIBUTING.md**: regras de arquitetura inegociáveis (Core puro,
  adapters isolados, falha-alto, atributos de migration, armadilhas de teste
  SQLite/Unimake).

## [1.1.0-alpha] — 2026-09-03

### Adicionado (Fase 5 — operações: backup/DR + hardening)
- **`docker/backup.sh`**: `pg_dump` custom format diário com retenção
  (7+4 por mtime) — cron no host.
- **`docker/restore.sh`**: restore com confirmação explícita e fumaça
  documentada (health + documento + carregar certificado = prova da KEK).
- **`docs/backup-dr.md`**: estratégia mínima viável (RPO ≤ 24 h, RTO ≤ 1 h),
  regra da KEK (backup separado em cofre — o dump traz PFX cifrados),
  checklist de desastre e de go-live (TLS/HSTS no proxy, secrets fora de
  `.env`, criptografia em repouso, append-only da auditoria, retenção LGPD).
- WAL archiving/PITR (RPO ≤ 5 min) e secret manager plugável ficam como
  pendência documentada da Fase 5 avançada.

## [1.0.0-alpha] — 2026-09-03

### Adicionado (Fase 4 — NFS-e padrão Nacional, envelope)
- **`POST /v1/documentos-fiscais/nfse`**: mesmo contrato de payload das demais
  emissões (itens = serviços), numeração atômica por tenant/série/ambiente sob
  o modelo interno `115` (`ModelosDocumento.NFSeNacional` — não é código
  SEFAZ; o DPS Nacional não usa modelo), idempotência e fila idênticos.
- **Fluxo assíncrono completo em sandbox**: `ProcessarDocumentoJob` →
  `EmissorMock` → AUTORIZADA com chave/protocolo; PDF simplificado via
  `GET .../{id}/pdf` (DANFSe).
- **`EmissorNFSe`**: fora de sandbox falha alto (`NotImplementedException` →
  `ERRO_INTERNO`) até a transmissão DPS real (próxima sprint do roadmap
  NFS-e) — sem risco de gravar documento com chave fake.
- **Guards de eventos**: cancelamento por evento 110111 não se aplica a NFS-e
  (409 com orientação de substituição — a rota de substituição entra na
  sprint NFS-e).

### Alterado
- `GeradorPdfQuestPdf.GerarDanfseAsync` gera o PDF simplificado (antes
  falhava).

### Testes
- 61/61 passando (1 novo: emissão NFSe sandbox → AUTORIZADA + cancelamento
  409; DANFSe test atualizado).

## [0.9.0-alpha] — 2026-09-03

### Adicionado (Sprint 1.8 — Fase 3: observabilidade)
- **`GET /v1/status-servico`**: consulta o status do serviço SEFAZ da UF do
  tenant via Unimake (`NFe/NFCe.StatusServico`), com cache de 60 s
  (IMemoryCache) e resposta `503` padrão quando a SEFAZ não responde.
  `ConsultaStatusServicoMock` responde cStat 107 em sandbox/Testing.
- **`AlertarCertificadosVencendoJob`** (recorrente diário 12:00): certificados
  ativos com validade ≤ 15 dias disparam webhook `certificado.vencendo`
  (payload com diasRestantes; dedupe de 1 alerta por tenant/dia, via outbox).
- Métricas OpenTelemetry/Prometheus continuam pendentes (precisa de infra de
  coleta; ficam para a Fase 5).

### Testes
- 60/60 passando (23 de integração — 1 novo: status-servico mock + alerta de
  certificado gerando webhook).

## [0.8.0-alpha] — 2026-09-03

### Adicionado (Sprint 1.7 — Fase 3: Distribuição DFe + Manifestação do Destinatário)
- **`SincronizarDistribuicaoDFeJob`** (recorrente 60 s no Worker): consulta o
  Distribuição DFe por tenant + ambiente a partir do último NSU
  (`ultimo_nsu`), grava as NFe destinadas em `notas_recebidas` (resumo
  resNFe/procNFe descomprimido pelo pacote Unimake) e dispara webhook
  `nota.recebida` via outbox.
- **`ConsultaDistribuicaoUnimake`** (`IConsultaDistribuicaoDfe`): filtro de
  schemas NFe, parse defensivo (`ExtratorDfe` — chave por atributo `ChNFe`
  (resNFe) ou `Id` (procNFe); emitente/valor/data por elemento **ou**
  atributo). Mock em sandbox responde cStat 137 (sem documentos).
- **Manifestação do Destinatário**: `POST /v1/notas-recebidas/{id}/manifestacao`
  (210200 confirmação, 210210 ciência, 210220 desconhecimento, 210240 não
  realização — justificativa 15–1000), idempotente por
  `(tenant, nota, idempotency-key)`, 409 se a nota já está em estado final.
  `ProcessarManifestacaoJob` transmite via `RecepcaoEvento` (cOrgao = cUF da
  chave, `DetEventoManif`) com retry (max 6) e webhook
  `manifestacao.processada`.
- **Endpoints**: `GET /v1/notas-recebidas`, `GET .../{id}`,
  `GET .../{id}/xml-completo` (409 se só resumo).
- Migration `20260906000000_ManifestacaoDistribuicaoDfe` (notas_recebidas,
  manifestacoes, ultimo_nsu).

### Testes
- 59/59 passando (37 unitários — 3 novos do `ExtratorDfe`; 22 de integração —
  1 novo: sincronização com consulta fake → nota gravada + webhook →
  manifestação ciência → PROCESSADO + webhook).

## [0.7.0-alpha] — 2026-09-03

### Adicionado (Sprint 1.6 — Fase 3: PDF)
- **Novo projeto `Fiscal.Pdf`** (QuestPDF 2026.8, licença Community):
  `GeradorPdfQuestPdf : IGeradorPdf` com DANFE (modelo 55, A4) e DANFCe
  (modelo 65, A5) em layout **simplificado** — emitente, destinatário, itens,
  totais/ICMS, pagamento, chave de acesso formatada, protocolo e marca d'água
  "SEM VALOR FISCAL" em homologação. Sem código de barras/QR do leiaute
  oficial de 20 campos (limitação documentada).
- **`GET /v1/documentos-fiscais/{id}/pdf`**: PDF binário (`application/pdf`)
  ou `?formato=base64` → JSON `{ pdfBase }`. Disponível apenas para
  AUTORIZADA/CANCELADA (409 caso contrário).
- **`IGeradorPdf` agora recebe `Tenant`** (dados do emitente no PDF).
  `GerarDanfseAsync` falha alto até a Fase 4.

### Testes
- 55/55 passando (34 unitários — 3 novos do gerador PDF; 21 de integração).

## [0.6.0-alpha] — 2026-09-03

### Adicionado (Sprint 1.5 — Fase 3: webhooks com outbox + HMAC)
- **Outbox de webhooks** (`outbox_webhooks`): linha gravada na MESMA transação
  que muda o status do documento (`ProcessarDocumentoJob` → autorizado/
  rejeitado/denegado) e do evento (`ProcessarEventoJob` → cancelado/
  carta de correção) — não perde webhook se o processo cair depois do commit.
- **Assinatura HMAC-SHA256**: `X-Fiscal-Signature: sha256=<hex>` sobre
  `"{timestamp}.{payload}"` com o `webhook_secret` do tenant;
  `X-Fiscal-Timestamp` (unix seconds) vai no header **e** no payload — o
  consumidor valida janela de 5 min (proteção contra replay, errata §replay).
- **`ProcessarWebhookJob` + `VarrerWebhooksJob`** (recorrente a cada 60s):
  entrega POST com timeout de 10 s; retry próprio com backoff
  `1m/5m/15m/1h/6h/6h/24h` — 8 tentativas → `FALHA` (terminal). Retry de
  webhook separado do retry de SEFAZ (docs/planejamento-detalhado).
- **`DespachanteWebhookHttp`** (`IDespachanteWebhook`): implementação HTTP
  real no Worker; testes injetam fake (mesmo padrão dos emissores).
- **Payload do webhook**: `{ tipo, timestamp, documento { id, tipo, status,
  ambiente, serie, numero, chaveAcesso, protocoloAutorizacao, motivoStatus,
  criadoEm, atualizadoEm } }` (camelCase). Eventos: `documento.autorizado`,
  `documento.rejeitado`, `documento.denegado`, `documento.cancelado`,
  `documento.carta_correcao`.
- Migration `20260905000000_OutboxWebhooks`.

### Testes
- 52/52 passando (31 unitários — 3 novos da assinatura HMAC e do envelope;
  21 de integração — 1 novo: emissão → outbox gravada → entrega ENTREGUE
  com despachante fake).

## [0.5.0-alpha] — 2026-09-03

### Adicionado (Sprint 1.4 — transmissão de eventos à SEFAZ)
- **`TransmissorEventoUnimake`** (`ITransmissorEventoFiscal`): cancelamento
  (evento 110111, exige protocolo de autorização do documento), carta de
  correção (110110, com o `xCondUso` padrão da SEFAZ) e inutilização de faixa
  (`InutNFe`). NFe e NFC-e (`RecepcaoEvento`/`Inutilizacao` por modelo;
  eventos de NFC-e não exigem CSC — flag `exigirCsc` na factory).
- **`ProcessarEventoJob`**: ciclo `PENDENTE → PROCESSANDO → PROCESSADO/
  REJEITADO/ERRO`; sucesso no cancelamento move o documento para `CANCELADA`
  (rejeição → `ERRO_CANCELAMENTO`); erro de transmissão volta a `PENDENTE`
  com backoff 30s/1m/2m/5m/10m. Erros de mapeamento/config (novos campos
  obrigatórios, protocolo ausente) viram `ERRO` sem retry.
- **`VarrerEventosJob`** (recorrente a cada 30s no Worker): reenfileira
  eventos `PENDENTES` cuja `proxima_tentativa_em` venceu — mesmo padrão do
  `VarrerContingenciaJob`.
- **`EventosController` enfileira o job** nos três endpoints (cancelamento,
  CC-e, inutilização); replays idempotentes não reenfileiram. Inutilização
  grava os parâmetros (modelo/serie/faixa/ambiente) em
  `eventos_fiscais.dados_evento` (jsonb, novo).
- **`TransmissorEventoMock`**: em `ModoSandbox=true`/Testing responde
  `PROCESSADO` (cStat 135/102) sem falar com a SEFAZ.
- Migration `20260904000000_EventosTransmissao`: `protocolo`, `motivo_status`,
  `tentativas`, `proxima_tentativa_em`, `dados_evento` + índice do sweep.

### Alterado
- **`ConfiguracaoUnimakeFactory`** ganha `exigirCsc` (eventos de NFC-e não
  precisam de CSC).
- Evento processado/rejeitado agora grava auditoria (`EVENTO_PROCESSADO`/
  `EVENTO_REJEITADO`).

### Testes
- 48/48 passando (28 unitários — 4 novos: interpretação de cStat de eventos
  135/136/155/573/243/492, inutilização 102/203/411 e o `TransmissorEventoMock`;
  20 de integração — 1 novo: cancelamento autorizado → job → documento
  `CANCELADA` com evento `PROCESSADO`).

## [0.4.0-alpha] — 2026-09-03

### Adicionado (painel administrativo)
- **Admin panel completo** (React 19 + Vite + TS + Tailwind v4, pasta
  `frontend/`): login de operador, dashboard de contagens, CRUD de tenants
  (antes era SQL manual!), gestão de API keys (geração com exibição única,
  revogação), certificados com alerta de validade, listagem cross-tenant de
  documentos (XML assinado/retorno SEFAZ, cancelamento e CC-e) e
  **Playground** de emissão NF-e/NFC-e pelo caminho real da API (API key +
  `Idempotency-Key` automática) com acompanhamento de status ao vivo.
- **Auth de operador**: entidade `AdminUser` (tabela `admin_users`,
  PBKDF2 no mesmo formato das API keys), `POST /v1/admin/auth/login`
  (JWT HS256, 8h, role `admin`), policy `Admin` nos endpoints
  `/v1/admin/*` e seed do primeiro operador via envs
  `ADMIN_EMAIL`/`ADMIN_PASSWORD`. `ADMIN_JWT_SECRET` ausente = login
  desabilitado (API de tenant segue no ar).
- **Endpoints `/v1/admin/*`** (JWT de admin, escopo cross-tenant): CRUD de
  tenants, api-keys por tenant, certificados por tenant, listagem/detalhe de
  documentos com filtros/paginação, cancelamento/CC-e via painel e
  `GET /v1/admin/dashboard`.
- **CORS** para o dev do painel (`localhost:5173/4173`); em produção o SPA é
  servido pela própria API (`UseStaticFiles` + fallback para `index.html`,
  `frontend/dist` → `wwwroot` no Dockerfile via stage Node).
- **Hangfire dashboard protegido**: exigia `role: admin` (header
  `Authorization: Bearer` ou `?access_token=`). Antes estava **aberto sem
  auth**. Detalhe: `MapHangfireDashboard` usa `Map()`, que remove o prefixo
  do `Request.Path` dentro do branch — a leitura do token na query considera
  `PathBase + Path`.

### Corrigido (bugs de produção expostos pelo smoke test em Postgres)
- **`api_keys.key_hash` era `varchar(64)` mas o PBKDF2 tem ~91 chars**:
  `POST /v1/api-keys` (e o novo endpoint admin) **falhava em Postgres** com
  `DbUpdateException` — só funcionava em SQLite (testes, que não validam
  tamanho) e com hashes legados inseridos via SQL. Migration
  `ApiKeyHashMaxLength` alarga para `varchar(120)`.
- **Migrations hand-written nunca eram aplicadas pelo `Migrate()`**: o EF só
  descobre migrations com os atributos `[Migration]` **e**
  `[DbContext]` (que ficam no `.Designer.cs` gerado). `EnforceAuditoriaAppendOnly`
  e `PerfilFiscalEmitenteCscReciboLote` estavam invisíveis — qualquer deploy
  Postgres novo ficaria sem as colunas de perfil fiscal/CSC/recibo. Atributos
  adicionados (e mantidos nas próximas hand-written).
- **`Migrate()` travava no startup** com `PendingModelChangesWarning`
  (validação nova do EF 10 × migrations hand-written sem `BuildTargetModel`):
  warning suprimido no `AddDbContext`, com o snapshot mantido manualmente.
- **`dotnet publish` falhava com NETSDK1152** (appsettings duplicados
  Api/Worker — a Api referencia o Worker para os jobs): `ErrorOnDuplicatePublishOutputFiles=false`.

### Adicionado (Sprint 1.3 — integração real Unimake.DFe)
- **Emissão real de NF-e (modelo 55)**: `EmissorNFe` monta o `EnviNFe`
  (layout 4.00), assina e transmite via `NFeAutorizacao`; fluxo assíncrono
  em duas fases — recibo do lote persistido em `documentos_fiscais.recibo_lote`
  (nova coluna) e consulta via `NFeRetAutorizacao` no retry (cStat 105/106
  reconsulta sem reenviar o lote, evitando NF-e duplicada).
- **Emissão real de NFC-e (modelo 65)**: `EmissorNFCe` síncrono
  (`indSinc=1`); exige CSC/IdCSC por tenant — novas colunas
  `tenants.csc_id`/`tenants.csc_criptografado` (AES-GCM com a KEK,
  via `ICertificadoStore.CifrarTextoAsync/DecifrarTextoAsync`). QR code
  montado pelo adapter Unimake a partir do CSC.
- **`MapperEnviNFe`**: `PayloadEntrada` (JSON) → `EnviNFe`. Chave de acesso
  de 44 dígitos e cDV calculados pela Unimake a partir do Ide (`cNF` é
  derivado do id do documento). Suporta ICMS CST 00/40/41/50; CST 60, CSOSN
  e grupos 10/20/51/70/90 falham alto com mensagem explícita (sprint futura).
  Defaults documentados no README (`natOp`, `indFinal`, `indPres`, `modBC`,
  `transp`, unidade `UN`).
- **`ConfiguracaoUnimakeFactory` real**: `Configuracao` da Unimake por
  modelo/UF/ambiente + certificado + CSC.
- **Perfil fiscal do emitente por tenant**: novas colunas em `tenants`
  (inscrição estadual + endereço do emitente, exigidos pelo grupo
  `emit/enderEmit`) e endpoints `GET/PUT /v1/tenants/perfil`. Emissão real
  sem perfil completo falha alto (`ERRO_INTERNO`) em vez de transmitir errado.
- **`naturezaOperacao` opcional** no payload de emissão (default `"VENDA"`);
  `nomeMunicipio` opcional no endereço do destinatário.
- **Migration `20260903000000_PerfilFiscalEmitenteCscReciboLote`**.
- **`GET /v1/inutilizacoes/{eventoId}`**: consulta do estado do pedido de
  inutilização (para onde aponta o `Location` do `202`), com isolamento
  por tenant.

### Alterado
- **`IEmissorFiscal.EmitirAsync` recebe `Tenant` + `X509Certificate2`**:
  o job carrega o tenant (UF/CNPJ/IBGE do emitente) e repassa o certificado
  já descriptografado (antes carregava e descartava).
- **`ErroNaoRecuperavelException`**: falhas de mapeamento/config/certificado
  viram `ERRO_INTERNO` (sem retry); só erro de rede/SEFAZ entra em
  `CONTINGENCIA` — antes, qualquer exceção não-`NotImplementedException`
  reprocessava infinito.
- **`ResultadoEmissao`** ganhou `XmlGerado` (XML de envio pré-assinatura,
  gravado em `xml_gerado`) e `ReciboLote`. O job só sobrescreve campos não
  nulos (a consulta de recibo não apaga o XML da 1ª passada).
- **DTOs de emissão movidos para `Fiscal.Core.Contracts`** — o adapter
  deserializa o payload sem referenciar o projeto da API.
- **`EmissorNFCe` agora resolve `ICertificadoStore` via DI** (para decifrar
  o CSC).

### Corrigido
- **Sandbox não exige mais certificado**: `ProcessarDocumentoJob` pula a
  exigência de certificado ativo quando `Fiscal:ModoSandbox=true` (o
  `EmissorMock` não usa o X509). Em produção sem certificado continua
  falhando com `ERRO_INTERNO`. A auditoria
  `CERTIFICADO_DESCRIPTOGRAFADO` só é gravada quando há certificado.
- **`POST /v1/inutilizacoes` violava FK em Postgres**: gravava
  `DocumentoId = Guid.Empty`, que não existe em `documentos_fiscais`
  (500 no processamento). `eventos_fiscais.documento_id` agora é
  nullable (inutilização grava NULL) e `eventos_fiscais` ganhou
  `tenant_id` — a idempotência de eventos passou a filtrar por tenant
  (antes, a mesma `Idempotency-Key` de inutilização colidia entre
  tenants). Novo índice parcial único
  `(tenant_id, tipo_evento, idempotency_key) WHERE documento_id IS NULL`
  mantém a garantia de idempotência da inutilização.

### Testes
- 36 testes passando (17 unitários do Core + 19 de integração da API — 9
  novos do painel admin: login 401/200, 401 sem token, listagem de tenants,
  criar tenant + gerar/revogar API key (chave válida na API de tenant e
  morta após revogação), listagem cross-tenant de documentos, 422 de status
  inválido, cancelamento via painel com transição para
  `CANCELAMENTO_PENDENTE`, resumo do dashboard).

## [0.3.0-alpha] — 2026-09-02

### Alterado (dívida técnica + hardening)
- **`EmissorNFe`/`EmissorNFCe` agora falham alto fora de sandbox**:
  em vez de retornarem chave/protocolo fake (risco de gerar NF-e com chave
  inválida em produção), lançam `NotImplementedException`. Adapter real
  entra na próxima sprint.
- **`ProcessarDocumentoJob` distingue bug de transmissão**:
  `NotImplementedException` → `ERRO_INTERNO` (sem retry); erros de
  transmissão/timeout → `CONTINGENCIA` com backoff exponencial.
- **API Key com hash forte**: PBKDF2-SHA256 (100k iterações, salt 16 bytes)
  em vez de SHA-256 puro. `VerifyKey` aceita o novo formato e cai de volta
  para o legado (compat).
- **Tenant ausente agora falha alto**: `GetTenantId` lança
  `InvalidOperationException` em vez de retornar `Guid.Empty` silencioso.
- **Auditoria dos eventos de negócio** (`EventosController`): agora grava
  `apiKeyId`, `ipOrigem`, `detalhe` com justificativa resumida em 80 chars.
- **Validação de schema via FluentValidation** (`Validators/`):
  `EmissaoRequest`, `CancelamentoRequest`, `CartaCorrecaoRequest`,
  `InutilizacaoRequest`. Regras explícitas (15-1000 chars em
  justificativa, modelo 55/65, faixa de números).
- **Resolução do emissor documentada**: `mock` → `tipo` → `qualquer`.
  `FirstOrDefault() ?? First()` em vez de `First()` (não estoura quando
  o teste injeta um único fake).
- **ModoSandbox forçado em Testing**: garante que `appsettings.json` (false
  em prod) não desabilite o mock nos testes de integração.

### Adicionado (hardening de release)
- **CI `docker-publish.yml`**: builda `api-final` e `worker-final` em
  multi-arch (amd64/arm64), push para `ghcr.io` em tags `v*.*.*`.
- **Migration `20260902000000_EnforceAuditoriaAppendOnly`**: documenta o
  `REVOKE UPDATE, DELETE, TRUNCATE ON auditoria` que deve ser aplicado no
  role do banco em produção.
- **`CODE_OF_CONDUCT.md`** (Contributor Covenant 2.1).
- **Templates de issue/PR** em `.github/` (bug_report, feature_request, PR).
- **`SECURITY.md`**: aviso destacado de que `security@exemplo.com` é
  placeholder que precisa ser substituído antes de release público.

### Testes
- 19/19 passando (10 unitários no `Fiscal.Core.Tests`, 9 de integração no
  `Fiscal.Api.Tests`). +1 teste de DENEGADA via `DenegadaEmissorFake`.

## [0.2.0-alpha] — 2026-09-02

### Adicionado (Fase 2 — Assíncrono + fila + contingência + eventos)
- **Hangfire + Hangfire.PostgreSql** no `Fiscal.Worker`. Storage em
  Postgres, dashboard `/hangfire` na API (fora de Testing).
- **`HangfireFilaEmissao : IFilaEmissao`** — enfileira via cliente Hangfire.
- **`ProcessarDocumentoJob`**: pega doc PENDENTE/CONTINGENCIA,
  descriptografa certificado, chama `IEmissorFiscal`, transita
  PENDENTE → PROCESSANDO → AUTORIZADA/REJEITADA/CONTINGENCIA/DENEGADA/
  ERRO_INTERNO. Backoff exponencial (30s/1m/2m/5m/10m) só em erro de
  transmissão; REJEITADA nunca é reprocessada.
- **`VarrerContingenciaJob`** (recorrente a cada 30s): reenfileira docs
  em CONTINGENCIA cuja `proxima_tentativa_em` venceu.
- **`DocumentosFiscaisController` refatorado para 202 Accepted**:
  valida → reserva número → cria PENDENTE → enfileira → responde com
  `{id, status, links}`. Idempotência devolve 200 com estado atual.
- **`EventosController`**: `POST /v1/documentos-fiscais/{id}/cancelamento`,
  `/carta-correcao` (rejeita 409 para NFC-e/NFS-e), `POST /v1/inutilizacoes`.
  Todos exigem `Idempotency-Key`. Cancelamento exige documento AUTORIZADA.
- **`IRepositorioEventoFiscal`** + `RepositorioEventoFiscal` reaproveitando
  a constraint `UNIQUE(documento_id, tipo_evento, idempotency_key)`.
- **Testes**: 8 novos cenários de integração (202, idempotência, 403
  ambiente divergente, transição via `ProcessarDocumentoJob`, 409
  cancelamento fora de AUTORIZADA, 409 CC-e em NFC-e, 400 sem
  Idempotency-Key, 422 com campo em soma divergente).
- `NoopBackgroundJobClient` no test substitui o cliente real do Hangfire.

## [0.1.0-alpha] — 2026-08-31

### Adicionado (Fase 0 — Fundação)
- Solução .NET 10 com 5 src projects + 2 tests
- Domínio: `Tenant`, `ApiKey`, `Certificado`, `DocumentoFiscal`, `SequenciaNumeracao`, `EventoFiscal`, `Auditoria`
- Enums: `TipoDocumento`, `StatusDocumento`, `Ambiente`
- Interfaces: `IEmissorFiscal`, `IFilaEmissao`, `ICertificadoStore`, `IGeradorPdf`, repositórios
- `FiscalDbContext` com constraint `UNIQUE(tenant_id, tipo, idempotency_key)` (idempotência com `tipo`)
- Migration `InitialSchema`
- `EnvelopeEncryptionService` (AES-GCM, DEK/KEK via `X509CertificateLoader.LoadPkcs12`)
- `ApiKeyAuthenticationHandler` (SHA-256, prefixo `fk_live_/fk_test_`, ambiente atrelado)
- Endpoints `/v1/api-keys` e `/v1/certificados`
- Dockerfile multi-stage + docker-compose
- CI no GitHub Actions (Postgres service container, build + test + format)

### Adicionado (Fase 1 — NF-e/NFC-e síncrona)
- `ValidadorConsistenciaFiscal` (soma de itens, base × alíquota, CST vs valor)
- `EmissorNFe`, `EmissorNFCe` (compilam, integração real depende do mapper de payload — Sprint 1.3.1)
- `EmissorMock` (modo sandbox, chave determinística)
- `DocumentosFiscaisController` (`POST /v1/documentos-fiscais/nfe|nfce`, `GET /v1/documentos-fiscais/{id}`)
- 10 testes unitários + 5 de integração (15 total, todos passando)
