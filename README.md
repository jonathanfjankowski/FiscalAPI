# FiscalAPI

[![CI](https://github.com/jonathanfjankowski/FiscalAPI/actions/workflows/build-and-test.yml/badge.svg)](../../actions)
[![Docker](https://github.com/jonathanfjankowski/FiscalAPI/actions/workflows/docker-publish.yml/badge.svg)](../../actions)
[![Licença: MIT](https://img.shields.io/badge/Licen%C3%A7a-MIT-yellow.svg)](LICENSE)

> **API open source para emissão de NF-e, NFC-e e NFS-e (padrão Nacional) — multi-tenant, assíncrona, .NET 10.**

> 🚧 **PROJETO EM DESENVOLVIMENTO — ALPHA (`1.3.0-alpha`).**
> Está funcional ponta a ponta em **modo sandbox** e a emissão real de
> NF-e/NFC-e está implementada, mas **ainda não passou por homologação
> contra a SEFAZ** (exige certificado A1) e o contrato de API **pode mudar**
> até o 1.0. **Não use em produção.** Acompanhe o estado em
> [docs/status-atual.md](docs/status-atual.md).

## O que é

A FiscalAPI é uma API REST que recebe dados **já calculados** de um sistema
integrador (ERP), monta o XML fiscal, assina com o certificado A1 do
emitente e transmite à SEFAZ — devolvendo o resultado de forma assíncrona
via consulta ou webhook. Arquitetura multi-tenant: várias empresas
(tenants) no mesmo deploy, cada uma com suas API keys, certificados e
numeração isolados.

> ⚠️ **Esta API não calcula tributos.** Os valores de impostos devem ser
> calculados pelo ERP e enviados já prontos. A API valida a estrutura e a
> aritmética informada antes de transmitir — nunca inventa valores.

**Principais capacidades:**

- **NF-e (55)** — emissão real via Unimake.DFe, fluxo assíncrono em duas
  fases (lote → recibo → consulta), layout 4.00 (ICMS CST 00/40/41/50).
- **NFC-e (65)** — emissão síncrona com CSC/IdCSC do tenant (cifrado) e QR code.
- **NFS-e Nacional (DPS)** — envelope REST completo com sandbox; transmissão
  real é a próxima sprint.
- **Eventos** — cancelamento, carta de correção e inutilização transmitidos
  à SEFAZ com retry próprio.
- **Contingência SVC-AN/SVC-RS** — em timeout/rede, consulta o protocolo e
  reemite via SVC automaticamente (sem duplicar nota).
- **Webhooks** — outbox transacional + assinatura HMAC-SHA256 + retry com
  backoff (separado do retry de SEFAZ).
- **DANFE/DANFCe/DANFSe** — PDF via QuestPDF (layout simplificado).
- **Distribuição DFe + Manifestação do Destinatário** — sincronização por
  NSU, notas recebidas e manifestação com webhook.
- **Status-serviço SEFAZ** com cache de 60 s e alerta de certificado vencendo.
- **Painel admin** (React) — tenants, API keys, certificados, documentos e
  playground de emissão.
- **Segurança** — PBKDF2 nas chaves, certificados em envelope AES-GCM,
  rate limit, auditoria append-only. Detalhes em
  [docs/revisao-seguranca.md](docs/revisao-seguranca.md).

> 📘 **Integrando um ERP?** Comece pelo
> [Guia de Integração](docs/integracao-api.md) — autenticação, fluxo de
> emissão e referência completa de endpoints e DTOs.

## Início rápido (Docker, modo sandbox — sem certificado)

Requisitos: [Docker](https://docs.docker.com/get-docker/) e `openssl`.

```bash
# 1. Gerar a chave mestra (KEK) que cifra os certificados
openssl rand -base64 32

# 2. Criar o .env
cp .env.example .env
# editar .env e colar o valor em KEK_MASTER_KEY (manter MODO_SANDBOX=true)
# definir também ADMIN_EMAIL, ADMIN_PASSWORD e ADMIN_JWT_SECRET (mín. 32 chars)

# 3. Subir Postgres + API + Worker
docker compose -f docker/docker-compose.yml --env-file .env up -d

# 4. Aguardar a API aplicar as migrations
curl http://localhost:8080/health/ready

# 5. Abrir o painel admin e criar o tenant + API key
#    http://localhost:8080 (login com ADMIN_EMAIL/ADMIN_PASSWORD do .env)
```

> Sem `ADMIN_EMAIL`/`ADMIN_PASSWORD` no `.env`, o seed do operador não roda
> e o painel fica sem login. Sem `ADMIN_JWT_SECRET`, o login fica
> desabilitado — as API keys de tenant continuam funcionando normalmente.

## Ambiente de desenvolvimento

### Backend (.NET 10)

```bash
dotnet restore
dotnet build
dotnet test              # 75 testes (unitários + integração)
dotnet run --project src/Fiscal.Api      # API em http://localhost:5039
dotnet run --project src/Fiscal.Worker   # host Hangfire (jobs)
```

Precisa de um Postgres local (ou use o do compose: apenas o serviço
`postgres`). Em dev, `MODO_SANDBOX=true` faz a emissão usar o `EmissorMock`
— sem certificado, sem CSC, sem SEFAZ. As migrations são aplicadas
automaticamente na subida.

### Frontend (painel admin — React 19 + Vite + Tailwind v4)

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173 (proxy /v1 → API em :5039)
npm run build      # tsc strict + vite build
npm run lint       # oxlint
```

Em produção o `dist/` é compilado dentro do build Docker e servido pela
própria API (mesma origem, sem CORS).

## Exemplo ponta a ponta (sandbox)

```bash
# 1. Criar tenant + API key pelo painel admin (http://localhost:8080)
#    — ou via SQL/curl como abaixo, se preferir:
docker exec -it fiscalapi-postgres-1 psql -U fiscal -d fiscal -c "
INSERT INTO tenants (id, cnpj, razao_social, uf, codigo_municipio_ibge,
                     regime_tributario, ambiente_padrao, ativo)
VALUES (gen_random_uuid(), '12345678000199', 'Empresa Teste', 'PR',
        '4106902', 3, 2, true);"

# 2. Criar uma API Key de homologação
curl -X POST http://localhost:8080/v1/api-keys \
  -H "Authorization: ApiKey <chave-bootstrap>" \
  -H "Content-Type: application/json" \
  -d '{"ambiente":"homologacao","descricao":"dev"}'
# → guardar a chave retornada em CHAVE

# 3. Emitir NF-e (resposta 202 Accepted — processamento assíncrono)
curl -X POST http://localhost:8080/v1/documentos-fiscais/nfe \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: pedido-1" \
  -H "Content-Type: application/json" \
  -d '{
    "ambiente":"homologacao",
    "serie":1,
    "destinatario":{"cnpjCpf":"11122233000144","nome":"Cliente Teste"},
    "itens":[{
      "codigo":"SKU1","descricao":"Produto","ncm":"12345678","cfop":"5102",
      "quantidade":2,"valorUnitario":50,"valorTotal":100,
      "impostos":[{"cst":"01","baseCalculo":100,"aliquota":1.65,"valor":1.65}]
    }],
    "totais":{"valorProdutos":100,"valorNota":100},
    "pagamento":[{"forma":"01","valor":100}]
  }'
# → 202 com { id, status:"PENDENTE", links:{ consulta: ... } }

# 4. Repetir com a mesma Idempotency-Key → mesmo id, sem novo documento (200)
curl -X POST http://localhost:8080/v1/documentos-fiscais/nfe ... # (mesmo payload, mesma key)

# 5. Acompanhar (poll ou webhook) até AUTORIZADA
curl -H "Authorization: ApiKey $CHAVE" http://localhost:8080/v1/documentos-fiscais/<id>
# → { id, status:"AUTORIZADA", chaveAcesso, protocoloAutorizacao, xmlAssinado, ... }
```

## Arquitetura

| Projeto | Responsabilidade |
|---|---|
| `Fiscal.Core` | Domínio (entidades, enums, interfaces) — **zero deps de Unimake/EF/Hangfire** |
| `Fiscal.Persistence` | EF Core + PostgreSQL, criptografia de certificados (envelope AES-GCM DEK/KEK) |
| `Fiscal.Adapters.Unimake` | Implementações de `IEmissorFiscal`: `EmissorNFe`, `EmissorNFCe`, `EmissorNFSe`, `EmissorMock` |
| `Fiscal.Api` | HTTP, auth por API Key, validação (FluentValidation + consistência), rate limit, health checks, dashboard `/hangfire` |
| `Fiscal.Worker` | Host Hangfire + jobs (`ProcessarDocumentoJob`, eventos, webhooks, DFe, contingência) |
| `Fiscal.Pdf` | DANFE/DANFCe/DANFSe via QuestPDF |
| `Fiscal.Core.Tests` / `Fiscal.Api.Tests` | xUnit + FluentAssertions + WebApplicationFactory |

O core não conhece infraestrutura: os adapters SEFAZ (Unimake), a fila
(Hangfire) e o PDF são implementações atrás de interfaces — substituíveis
sem tocar no domínio.

## Autenticação

API Key por tenant, hash PBKDF2-SHA256 (salt aleatório; compat com o legado
SHA-256), prefixo `fk_live_` (produção) ou `fk_test_` (homologação).
**O ambiente vem atrelado à chave** — uma chave de homologação não consegue
emitir em produção mesmo que o payload peça, retornando `403`.

```bash
# Header
Authorization: ApiKey fk_test_AbCdEf123...
```

O painel admin usa login de operador (`admin_users`) → JWT de 8h, criado no
startup via seed (`ADMIN_EMAIL`/`ADMIN_PASSWORD`, segredo em
`ADMIN_JWT_SECRET` — mín. 32 chars; sem valor, o login fica desabilitado).

## Endpoints

| Método | Path | Auth | Descrição |
|---|---|---|---|
| `GET` | `/health/live` | — | Processo no ar |
| `GET` | `/health/ready` | — | DB respondendo |
| `GET` | `/hangfire` | Admin JWT | Dashboard Hangfire (header `Authorization: Bearer` ou `?access_token=`) |
| `POST` | `/v1/admin/auth/login` | — | Login do operador do painel (`{email, senha}` → `{token, expiraEm}`) |
| `GET/POST/PUT/DELETE` | `/v1/admin/tenants` | Admin JWT | CRUD de tenants (DELETE = desativação soft) |
| `GET/POST/DELETE` | `/v1/admin/tenants/{tenantId}/api-keys` | Admin JWT | Gera/lista/revoga API keys de um tenant (chave em claro 1x) |
| `GET/POST` | `/v1/admin/tenants/{tenantId}/certificados` | Admin JWT | Lista/upload de certificado em nome do tenant |
| `GET` | `/v1/admin/documentos-fiscais` | Admin JWT | Listagem cross-tenant (`tenantId, status, modelo, de, ate, page, pageSize`) |
| `GET` | `/v1/admin/documentos-fiscais/{id}` | Admin JWT | Detalhe completo (XMLs, protocolo, tentativas) |
| `POST` | `/v1/admin/documentos-fiscais/{id}/cancelamento` | Admin JWT | Cancela via painel (mesmas regras do endpoint de tenant) |
| `POST` | `/v1/admin/documentos-fiscais/{id}/carta-correcao` | Admin JWT | CC-e via painel (só NF-e 55) |
| `GET` | `/v1/admin/dashboard` | Admin JWT | Contagens p/ o painel (docs por status, tenants, certificados) |
| `POST` | `/v1/api-keys` | ApiKey | Cria nova chave (retorna em texto puro uma vez) |
| `GET` | `/v1/api-keys` | ApiKey | Lista metadados |
| `DELETE` | `/v1/api-keys/{id}` | ApiKey | Revoga chave |
| `POST` | `/v1/certificados` | ApiKey | Upload de `.pfx` (multipart) — cifra com envelope AES-GCM |
| `GET` | `/v1/status-servico` | ApiKey | Status do serviço SEFAZ da UF do tenant (`?modelo=55|65&ambiente=`) — cache 60 s |
| `GET` | `/v1/tenants/perfil` | ApiKey | Perfil fiscal do emitente (dados usados na NF-e/NFC-e) |
| `PUT` | `/v1/tenants/perfil` | ApiKey | Atualiza emitente (IE + endereço) e CSC/IdCSC da NFC-e (cifrado) |
| `GET` | `/v1/certificados` | ApiKey | Lista metadados |
| `POST` | `/v1/documentos-fiscais/nfe` | ApiKey | Emite NF-e (assíncrono, `202 Accepted` com `{id, status, links}`) |
| `POST` | `/v1/documentos-fiscais/nfce` | ApiKey | Emite NFC-e (assíncrono, `202 Accepted`) |
| `POST` | `/v1/documentos-fiscais/nfse` | ApiKey | Emite NFS-e Nacional/DPS (assíncrono; sandbox via mock; transmissão real na próxima sprint NFS-e) |
| `GET` | `/v1/documentos-fiscais/{id}` | ApiKey | Consulta status e metadados |
| `GET` | `/v1/documentos-fiscais/{id}/pdf` | ApiKey | DANFE/DANFCe (PDF binário; `?formato=base64` para JSON) — só AUTORIZADA/CANCELADA |
| `POST` | `/v1/documentos-fiscais/{id}/cancelamento` | ApiKey | Cancela NF-e/NFC-e autorizada (até janela da SEFAZ) |
| `POST` | `/v1/documentos-fiscais/{id}/carta-correcao` | ApiKey | CC-e (apenas NF-e; `409` para NFC-e/NFS-e) |
| `POST` | `/v1/inutilizacoes` | ApiKey | Inutiliza faixa de numeração (NF-e/NFC-e) |
| `GET` | `/v1/inutilizacoes/{eventoId}` | ApiKey | Consulta status do pedido de inutilização |
| `GET` | `/v1/notas-recebidas` | ApiKey | Notas destinadas ao tenant (Distribuição DFe) |
| `GET` | `/v1/notas-recebidas/{id}` | ApiKey | Detalhe da nota recebida |
| `GET` | `/v1/notas-recebidas/{id}/xml-completo` | ApiKey | procNFe (409 enquanto só houver resumo) |
| `POST` | `/v1/notas-recebidas/{id}/manifestacao` | ApiKey | Ciência/confirmação/desconhecimento/não realização (202) |

Todos os `POST` que criam documento/evento exigem header `Idempotency-Key`.
Idempotência: 2ª chamada com mesma chave devolve `200` com o estado atual
(sem novo documento).

**Erros padronizados** (`application/problem+json`, RFC 7807):

| HTTP | Quando |
|---|---|
| `200` | Sucesso (ou idempotência: 2ª chamada retorna estado atual) |
| `202` | Aceito — processamento assíncrono iniciado |
| `400` | Header `Idempotency-Key` ausente ou payload inválido (FluentValidation) |
| `403` | API Key não autorizada para o ambiente solicitado |
| `404` | Documento não encontrado (ou de outro tenant) |
| `409` | Conflito (ex.: cancelar documento que não está `AUTORIZADA`, CC-e em NFC-e) |
| `422` | Inconsistência aritmética (soma de itens, base × alíquota, CST) — `campo` indica onde |

## Validação que a API faz (e o que **não** faz)

- ✅ Schema do payload de entrada (FluentValidation).
- ✅ Consistência aritmética: soma de itens, `base × alíquota`, coerência CST vs valor.
- ✅ Idempotência por `(tenant_id, tipo, idempotency_key)`.
- ✅ Numeração atômica por tenant/série/modelo/ambiente.
- ❌ **Cálculo de impostos** — responsabilidade do ERP/integrador.
- ❌ Validação de regras de negócio específicas por UF/regime (ex.: ST por
  NCM) — delegada à SEFAZ na transmissão.

## Contingência (SVC)

Config `Fiscal:Contingencia:Habilitada` (default `false`) + `Fiscal:Contingencia:Modo`
(`SVCAN` default, ou `SVCRS`). Com ela ativa, uma **falha ambígua de
transmissão** (timeout/rede) dispara, na ordem:

1. **Consulta de protocolo** pela chave determinística do documento — se a
   SEFAZ processou a tempo, o documento vira `AUTORIZADA` sem reenvio
   (elimina duplicidade por timeout);
2. se não processou, o documento recebe `modo_contingencia` e o retry (30 s)
   reemite via **SVC-AN/SVC-RS** (tpEmis 6/7) — a Unimake roteia ao
   webservice certo.

EPEC (nota pré-notificada offline) e NFC-e offline (tpEmis 9) ficam para
sprint futura.

## Webhooks (outbox + HMAC)

Configure `webhookUrl` e `webhookSecret` do tenant (painel admin). Quando um
documento transiciona, a outbox (`outbox_webhooks`) é gravada **na mesma
transação** da mudança de status e o `VarrerWebhooksJob` entrega com retry
próprio — o retry de webhook nunca mistura com o retry de SEFAZ.

Eventos: `documento.autorizado`, `documento.rejeitado`, `documento.denegado`,
`documento.cancelado`, `documento.carta_correcao`, `nota.recebida`,
`manifestacao.processada`, `certificado.vencendo`.

**Payload** (`application/json`):

```json
{
  "tipo": "documento.autorizado",
  "timestamp": 1700000000,
  "documento": { "id": "...", "tipo": "NFE", "status": "AUTORIZADA", "ambiente": "homologacao",
                  "serie": 1, "numero": 5, "chaveAcesso": "...", "protocoloAutorizacao": "...",
                  "motivoStatus": null, "criadoEm": "...", "atualizadoEm": "..." }
}
```

**Autenticação** — headers:

```
X-Fiscal-Timestamp: 1700000000
X-Fiscal-Signature: sha256=<hex>
```

Verificação no consumidor (rejeitar timestamps fora de uma janela de
**5 min** — proteção contra replay):

```csharp
var esperado = HMACSHA256Hex(webhookSecret, $"{timestamp}.{corpoCru}");
válido = ConstantTimeEquals(esperado, assinaturaDoHeader) && |agora - timestamp| <= 5min;
```

Seu endpoint deve responder `2xx` para confirmar a entrega. Qualquer outra
resposta, timeout (10 s) ou erro de rede conta como tentativa: backoff
`1m → 5m → 15m → 1h → 6h → 6h → 24h` e, após 8 tentativas, a entrega vai
para `FALHA` (terminal, visível na tabela `outbox_webhooks`).

## Limitações conhecidas

- 🚧 **Projeto em alpha** — sem homologação real contra SEFAZ ainda; o
  contrato pode mudar até o 1.0.
- **Certificado A1 apenas** — A3/HSM fora de escopo.
- **Mapper da NF-e cobre ICMS completo + grupos federais + item rico**
  (emissão real, não sandbox): CST `00–90` e **CSOSN do Simples Nacional
  `101–900`** (ST, FCP e DIFAL), **IPI/PIS/COFINS**, GTIN, CEST, unidade,
  desconto por item e frete/seguro/outras — via grupo `impostosV2`; a lista
  plana legada `impostos[]` segue suportada (CST 00/40/41/50). Ficam para as
  próximas fases: NF-ref/devolução (F4), reforma IBS/CBS/IS (F5) e
  transporte/volumes.
  Defaults adotados quando o payload não traz: `natOp` (`naturezaOperacao`
  opcional no payload, default `"VENDA"`), `tpNF` saída, `finNFe` normal,
  `indFinal` consumidor final, `indPres` presencial (internet quando a UF do
  destinatário difere), `modBC` 3, `transp` sem ocorrência. Combinações fora
  do contrato falham alto (`ERRO_INTERNO` com mensagem explícita) em vez de
  transmitir errado. A evolução está planejada em
  [docs/plano-evolucao-contrato-v2.md](docs/plano-evolucao-contrato-v2.md)
  (F1 concluída na 1.4.0-alpha).
- **NF-e (modelo 55) é assíncrona em duas fases**: lote via `NFeAutorizacao`
  → recibo persistido (`recibo_lote`) → consulta via `NFeRetAutorizacao`;
  "lote em processamento" (cStat 105) reconsulta dentro do fluxo de
  contingência. NFC-e é síncrona (`indSinc=1`) e **exige CSC/IdCSC** do
  tenant (`PUT /v1/tenants/perfil`; o CSC fica cifrado com a KEK). O QR code
  é montado pelo adapter Unimake a partir do CSC.
- **A chave de acesso (44 dígitos) e o cDV são calculados pela Unimake**
  a partir do Ide; o `cNF` é derivado deterministicamente do id do documento.
  A unicidade real da chave vem de série+número (únicos por tenant).
- **Emissão real exige perfil fiscal completo do emitente** (inscrição
  estadual + endereço) e falha alto sem isso — configure via
  `PUT /v1/tenants/perfil`. Em dev/sandbox, `MODO_SANDBOX=true` (EmissorMock)
  dispensa certificado, CSC e perfil.
- **NFS-e (padrão Nacional/DPS)**: transmissão DPS real implementada
  (`POST /nfse/dps`, layout 1.01 síncrono) + **substituição**
  (`POST {id}/substituicao`); a rota legada (`POST /nfse`) segue só para
  sandbox. A bateria de homologação exige certificado A1 e **credenciamento
  do prestador** na SEFAZ Nacional.
- **Distribuição DFe sincroniza por NSU** a cada 60 s no Worker; em produção
  exige certificado A1; em sandbox a consulta responde "sem documentos" (mock).
- **PDF (DANFE/DANFCe/DANFSe)** em layout **simplificado** — sem código de
  barras/QR do leiaute oficial de 20 campos (evolução no plano v2).
- **Webhooks** sem página de reenvio manual na outbox (consultável via
  banco: tabela `outbox_webhooks`).
- **Em homologação real** você precisa de um certificado A1 válido (a SEFAZ
  aceita o mesmo certificado de produção; não há certificado "de teste").

## Testes

```bash
dotnet test
```

75 testes (42 unitários no `Fiscal.Core.Tests`, 33 de integração no
`Fiscal.Api.Tests`), incluindo segurança (PBKDF2, envelope AES-GCM, API
keys, upload de certificado), contingência, distribuição DFe/manifestação e
webhooks. Classes de integração rodam serializadas (`[Collection]`) sobre o
mesmo SQLite compartilhado.

### Checklist de homologação real (fora do CI)

A emissão real fala com a SEFAZ e exige recursos que o pipeline não tem:

1. Certificado A1 válido do emitente (upload em `POST /v1/certificados`).
2. `PUT /v1/tenants/perfil` com IE e endereço do emitente (e CSC/IdCSC para NFC-e).
3. `Fiscal:ModoSandbox=false` no **Worker** (quem executa o job) e API key do ambiente correspondente.
4. Emitir em homologação (`ambiente: "homologacao"`), conferir `motivoStatus`,
   `chaveAcesso` e XMLs (`xmlAssinado`/`xmlRetornoSefaz`) no `GET /v1/documentos-fiscais/{id}`.

## Operações (backup / DR / go-live)

O Postgres concentra todo o estado (tenants, chaves, certificados cifrados,
XMLs autorizados, outbox). Estratégia mínima, scripts e checklist de
go-live estão em **[docs/backup-dr.md](docs/backup-dr.md)**:

- backup diário `pg_dump` (`docker/backup.sh`, retenção 7+4);
- restore com confirmação (`docker/restore.sh`) — **teste trimestral obrigatório**;
- a KEK dos certificados tem backup separado (cofre/secret manager), nunca no dump;
- go-live: TLS no proxy, secrets fora de `.env`, criptografia em repouso,
  role sem UPDATE/DELETE em `auditoria`, política de retenção LGPD.

## Documentação

| Doc | Conteúdo |
|---|---|
| [docs/integracao-api.md](docs/integracao-api.md) | **Guia de integração** — autenticação, fluxo, endpoints, DTOs |
| [docs/status-atual.md](docs/status-atual.md) | **Status do projeto** — o que está pronto e o que falta |
| [docs/arquitetura.md](docs/arquitetura.md) | Como funciona por dentro |
| [docs/revisao-seguranca.md](docs/revisao-seguranca.md) | Validação de segurança |
| [docs/plano-evolucao-contrato-v2.md](docs/plano-evolucao-contrato-v2.md) | Evolução do contrato (CSOSN, IPI/PIS/COFINS, reforma…) |
| [docs/roadmap.md](docs/roadmap.md) | Roadmap público |
| [docs/backup-dr.md](docs/backup-dr.md) | Backup, DR e checklist de go-live |
| [CHANGELOG.md](CHANGELOG.md) | Histórico de versões |

## Contribuir

- **[CONTRIBUTING.md](CONTRIBUTING.md)** — setup, padrões e regras de arquitetura.
- Itens do roadmap aceitam contribuição — abra uma issue com a label `feature`.
- Bugs de segurança: **[SECURITY.md](SECURITY.md)** (não abrir issue pública).

## Licença

MIT (sujeito à confirmação da licença do `Unimake.DFe`).
