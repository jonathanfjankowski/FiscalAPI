# FiscalAPI — Guia de Integração

Documentação para integradores que desejam emitir **NF-e** (modelo 55) e **NFC-e** (modelo 65) e operar **eventos fiscais** (cancelamento, carta de correção, inutilização) através da FiscalAPI.

> **Compatibilidade:** API `0.3.0-alpha`. Os exemplos usam a base `http://localhost:8080` (docker) — substitua pela URL do ambiente que for usar.
>
> **Novo por aqui?** Este documento é a referência completa. Para um roteiro
> prático do zero à primeira nota autorizada (com PDF) em modo sandbox, veja
> o [Guia da primeira emissão](guia-primeira-emissao.md).

---

## Índice

1. [Visão geral](#visão-geral)
2. [Conceitos básicos](#conceitos-básicos)
3. [Primeiros passos](#primeiros-passos)
4. [Autenticação](#autenticação)
5. [Fluxo de emissão](#fluxo-de-emissão)
6. [Referência de endpoints](#referência-de-endpoints)
7. [Referência de DTOs](#referência-de-dtos)
8. [Validações da API](#validações-da-api)
9. [Status de documentos e enumerações](#status-de-documentos-e-enumerações)
10. [Modelo de erros](#modelo-de-erros)
11. [Boas práticas para o integrador](#boas-práticas-para-o-integrador)
12. [Limitações conhecidas](#limitações-conhecidas)

---

## Visão geral

A FiscalAPI é uma API REST multi-tenant que cuida da parte burocrática da emissão fiscal: monta e assina o XML, transmite à SEFAZ, acompanha contingência/retries e devolve o resultado consolidado (chave de acesso, protocolo, XMLs).

O modelo de operação é **assíncrono**:

```
POST (emissão)  ──►  202 Accepted  ──►  processamento em background  ──►  GET (polling)  ──►  resultado
```

Você envia os dados do documento, recebe imediatamente um `id` com status `PENDENTE` e consulta periodicamente o documento até ele atingir um **estado terminal** (`AUTORIZADA`, `REJEITADA`, `DENEGADA`, `CANCELADA` ou `ERRO_INTERNO`).

**A API não calcula impostos.** Os valores de tributos (base de cálculo, alíquota, valor) são responsabilidade do seu sistema — a API valida a consistência aritmética do que você enviar e rejeita documentos incoerentes (detalhes na seção [Validações](#validações-da-api)).

**Convenções usadas neste guia:**

| Convenção | Valor |
|---|---|
| Content-Type de request/response | `application/json` (erros: `application/problem+json`) |
| Encoding de datas | ISO 8601 com offset (ex.: `2026-09-03T12:34:56.789+00:00`) |
| Casing de campos JSON | `camelCase` (ex.: `chaveAcesso`, `valorUnitario`) |
| Idempotência | Header `Idempotency-Key` obrigatório em todo POST que cria documento/evento |

---

## Conceitos básicos

### Tenant
Sua empresa emitente, isolada dentro da API. Todo documento, chave, certificado e evento pertence a um tenant — um tenant nunca enxerga dados de outro. Os dados cadastrais do tenant (CNPJ, razão social, UF, regime tributário, endereço do emitente) são criados via `POST /v1/admin/tenants` (operação administrativa; o campo opcional `criarApiKey` já cria a primeira API key junto com o tenant, na mesma transação) e atualizados pelo próprio tenant via `PUT /v1/tenants/perfil`.

### API Key por ambiente
Cada chave de API está **atrelada a um ambiente** e só opera nele:

| Prefixo | Ambiente | Valor de `ambiente` nos payloads |
|---|---|---|
| `fk_test_` | Homologação (SEFAZ de homologação) | `"homologacao"` |
| `fk_live_` | Produção | `"producao"` |

Uma chave `fk_test_` que tenta emitir com `"ambiente": "producao"` recebe `403`.

### Idempotência
Todo POST de emissão e de eventos exige o header `Idempotency-Key`. Ele garante que um retry de rede nunca crie dois documentos a partir do mesmo pedido:

- **1ª chamada** com uma chave nova → `202 Accepted` (documento criado).
- **Chamadas seguintes** com a mesma chave → `200 OK` com o **estado atual** do documento já existente (nenhum novo documento é criado, nenhum número é consumido).

### Numeração automática
Você informa apenas a **série**. O número do documento é reservado sequencialmente pela API por (tenant, modelo, série, ambiente) — não há como colidir ou pular número por concorrência. Para inutilizar faixas de numeração (ex.: notas canceladas antes da transmissão), use o endpoint de [inutilização](#post-v1inutilizacoes-).

### Sandbox vs produção
Quando a API roda com `Fiscal:ModoSandbox=true`, o emissor real (SEFAZ) é substituído por um **emissor mock**, que autoriza documentos de teste sem contato externo — útil para desenvolver a integração ponta a ponta. Em produção o modo sandbox fica desligado e a transmissão vai para a SEFAZ de verdade (homologação ou produção, conforme a chave).

---

## Primeiros passos

Sequência de configuração antes da primeira emissão:

### 1. Obtenha a primeira API Key

A primeira chave do tenant é fornecida administrativamente (bootstrap). Com ela, você pode criar novas chaves via API quando quiser (rotação, chaves por sistema etc.) — veja [API Keys](#api-keys).

### 2. Faça upload do certificado A1 (produção)

O certificado digital A1 (`.pfx`) é obrigatório em **produção**: sem um certificado ativo, o processamento do documento falha com status `ERRO_INTERNO` e motivo `"Nenhum certificado ativo para o tenant."`.

No **sandbox** o certificado é opcional — o emissor mock autoriza sem assinar nada. Você pode pular este passo enquanto desenvolve; para testar o fluxo completo (ou já deixar o ambiente pronto para produção), faça o upload:

```bash
BASE=http://localhost:8080
CHAVE=fk_test_sua-chave...

curl -X POST "$BASE/v1/certificados" \
  -H "Authorization: ApiKey $CHAVE" \
  -F "pfx=@certificado.pfx" \
  -F "senha=senhaDoCertificado"
```

Resposta `201 Created`:

```json
{
  "id": "9d2f1b3a-4c5d-4e6f-8a9b-0c1d2e3f4a5b",
  "thumbprint": "3A7B...",
  "validoAte": "2027-05-20",
  "ativo": true
}
```

O arquivo e a senha são armazenados **criptografados** (envelope AES-GCM) — a API nunca os devolve.

### 3. Emita um documento de teste

```bash
curl -X POST "$BASE/v1/documentos-fiscais/nfe" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "ambiente": "homologacao",
    "serie": 1,
    "destinatario": {
      "cnpjCpf": "11122233000144",
      "nome": "Cliente Teste Ltda"
    },
    "itens": [{
      "codigo": "SKU1",
      "descricao": "Produto de teste",
      "ncm": "12345678",
      "cfop": "5102",
      "quantidade": 2,
      "valorUnitario": 50,
      "valorTotal": 100,
      "impostos": [{
        "cst": "01",
        "baseCalculo": 100,
        "aliquota": 1.65,
        "valor": 1.65
      }]
    }],
    "totais": { "valorProdutos": 100, "valorNota": 100 },
    "pagamento": [{ "forma": "01", "valor": 100 }]
  }'
```

Resposta `202 Accepted`:

```json
{
  "id": "e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c",
  "status": "PENDENTE",
  "ambiente": "homologacao",
  "criadoEm": "2026-09-03T12:00:00.123+00:00",
  "links": {
    "consulta": "/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c"
  }
}
```

### 4. Consulte até o estado terminal

```bash
curl -H "Authorization: ApiKey $CHAVE" \
  "$BASE/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c"
```

Documento autorizado:

```json
{
  "id": "e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c",
  "tipo": "NFE",
  "status": "AUTORIZADA",
  "ambiente": "homologacao",
  "serie": 1,
  "numero": 42,
  "chaveAcesso": "41260912345678000199550010000000421012345678",
  "protocoloAutorizacao": "135260000042100",
  "xmlAssinado": "<NFe xmlns=\"...\">...</NFe>",
  "xmlRetornoSefaz": "<protNFe versao=\"4.00\">...</protNFe>",
  "motivoStatus": null,
  "criadoEm": "2026-09-03T12:00:00.123+00:00",
  "atualizadoEm": "2026-09-03T12:00:03.456+00:00"
}
```

Pronto — a partir daqui consulte a [referência de endpoints](#referência-de-endpoints) para o restante da superfície da API.

---

## Autenticação

Toda requisição de negócio (tudo sob `/v1`, exceto health checks) exige o header:

```
Authorization: ApiKey <chave>
```

- Chaves de homologação começam com `fk_test_`; chaves de produção com `fk_live_`.
- A chave não pode ser enviada com espaços extras ou prefixo diferente de `ApiKey ` (case-insensitive).

### Header opcional `X-Fiscal-Ambiente`

Você pode (opcionalmente) enviar `X-Fiscal-Ambiente: producao` ou `homologacao` para amarrar a requisição ao ambiente esperado. Se enviado e **divergente do ambiente da chave**, a requisição falha com `401` — uma proteção extra contra usar a chave errada.

### Códigos de autenticação

| Situação | HTTP |
|---|---|
| Header ausente ou chave inexistente/revogada | `401 Unauthorized` |
| `X-Fiscal-Ambiente` divergente do ambiente da chave | `401 Unauthorized` |
| Chave válida, mas `ambiente` do payload divergente do ambiente da chave | `403 Forbidden` |

---

## Fluxo de emissão

### Máquina de estados do documento

```
             POST /v1/documentos-fiscais/{nfe|nfce}     (202 Accepted)
                           │
                           ▼
                       PENDENTE
                           │  worker processa
                           ▼
                      PROCESSANDO
        ┌─────────────┬────────┼──────────────┬───────────────┐
        ▼             ▼        ▼              ▼               ▼
   AUTORIZADA    REJEITADA  DENEGADA     CONTINGENCIA    ERRO_INTERNO
   (terminal)    (terminal) (terminal)        │            (terminal)
                                    erro de transmissão:
                                    retry automático com
                                    backoff 30s→1m→2m→5m→10m
                                        │
                                        ▼
                                  PROCESSANDO ...
```

- `PENDENTE` → `PROCESSANDO`: transição automática, em geral em segundos.
- `CONTINGENCIA`: a SEFAZ (ou a conexão) falhou de forma transitoria. A API tenta novamente sozinha, com backoff crescente (30s, 1min, 2min, 5min, 10min). Você não precisa fazer nada além de continuar consultando.
- `REJEITADA` / `DENEGADA`: resposta definitiva da SEFAZ. **Não é reprocessado automaticamente** — leia `motivoStatus`, corrija o problema do lado do seu sistema e emita um novo documento.
- `ERRO_INTERNO`: falha não recuperável do lado da API (ex.: sem certificado ativo, configuração ausente). Consulte `motivoStatus` e, se necessário, acione o suporte.

### Estados via eventos

```
AUTORIZADA ── POST /cancelamento ──► CANCELAMENTO_PENDENTE ──► CANCELADA   (terminal)
```

Cancelamento só é aceito para documentos `AUTORIZADA` (senão `409`). Ver [Estado atual dos eventos](#limitações-conhecidas).

### Polling recomendado

1. Após o `202`, aguarde ~2–5 segundos antes da primeira consulta.
2. Consulte `GET /v1/documentos-fiscais/{id}` com backoff exponencial (ex.: 2s, 5s, 10s, 20s…, teto de 30–60s).
3. Pare quando `status` for um estado **terminal**:
   - `AUTORIZADA`, `REJEITADA`, `DENEGADA`, `CANCELADA`, `ERRO_INTERNO`.
4. Se `CONTINGENCIA`/`CANCELAMENTO_PENDENTE`, continue no loop — são transitórios.

Não há webhook para mudanças de status de documento: **polling é o mecanismo
oficial de acompanhamento da nota** — o webhook (capítulo próprio) cobre
eventos de negócio (`documento.autorizado`, `documento.cancelado`,
`certificado.vencendo`, etc.), não substitui a consulta pontual. Ver
[Limitações conhecidas](#limitações-conhecidas).

---

## Referência de endpoints

Todos os endpoints abaixo exigem `Authorization: ApiKey <chave>`, salvo os health checks. Os POSTs marcados com 🔒 exigem também `Idempotency-Key`.

### Health checks (sem autenticação)

#### `GET /health/live`

Processo no ar. `200` com corpo mínimo.

#### `GET /health/ready`

Pronto para receber tráfego (inclui verificação do banco de dados). `200` se o Postgres responde; `503` caso contrário. Use para probes de orquestrador — não para lógica de negócio.

---

### API Keys

#### `POST /v1/api-keys`

Cria uma nova chave para o tenant autenticado. **A chave completa é exibida uma única vez** na resposta.

Corpo:

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `descricao` | string | não | Identificação livre (ex.: `"ERP Filial SP"`) |
| `ambiente` | string | sim | `"producao"` ou `"homologacao"` |

```bash
curl -X POST "$BASE/v1/api-keys" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Content-Type: application/json" \
  -d '{"ambiente": "homologacao", "descricao": "ERP Filial SP"}'
```

`201 Created`:

```json
{
  "id": "b4c1d2e3-5f6a-7b8c-9d0e-1f2a3b4c5d6e",
  "chave": "fk_test_AbCdEfGhIjKlMnOpQrStUv",
  "prefixo": "fk_test_",
  "descricao": "ERP Filial SP",
  "ambiente": "homologacao",
  "criadoEm": "2026-09-03T12:00:00.123+00:00",
  "aviso": "Esta é a única vez que a chave completa é exibida. Guarde-a em local seguro."
}
```

Erros: `400` ambiente ausente; `422` ambiente inválido.

#### `GET /v1/api-keys`

Lista as chaves do tenant (só metadados — nunca a chave completa).

`200 OK`:

```json
[
  {
    "id": "b4c1d2e3-5f6a-7b8c-9d0e-1f2a3b4c5d6e",
    "prefixo": "fk_test_",
    "descricao": "ERP Filial SP",
    "ambiente": "homologacao",
    "ativa": true,
    "criadoEm": "2026-09-03T12:00:00.123+00:00"
  }
]
```

#### `DELETE /v1/api-keys/{id}`

Revoga a chave. `204 No Content` em caso de sucesso; `404` se o id não pertencer ao tenant. Chaves revogadas passam a autenticar com `401` imediatamente.

---

### Certificados

#### `POST /v1/certificados`

Faz upload do certificado A1. Formato **`multipart/form-data`**:

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `pfx` | arquivo | sim | Arquivo `.pfx`, até **10 MB** |
| `senha` | texto | sim | Senha do `.pfx` |

```bash
curl -X POST "$BASE/v1/certificados" \
  -H "Authorization: ApiKey $CHAVE" \
  -F "pfx=@/caminho/certificado.pfx" \
  -F "senha=senhaDoCertificado"
```

`201 Created`:

```json
{
  "id": "9d2f1b3a-4c5d-4e6f-8a9b-0c1d2e3f4a5b",
  "thumbprint": "3A7B9C2D...",
  "validoAte": "2027-05-20",
  "ativo": true
}
```

Erros: `400` arquivo ou senha ausentes; `422` `.pfx` não abre com a senha informada. O PFX e a senha são guardados criptografados (envelope AES-GCM com chave mestra do provedor); só metadados são retornáveis.

**Rotação**: só há **1 certificado ativo por tenant**. O upload desativa
automaticamente o certificado ativo anterior (auditoria
`CERTIFICADO_SUBSTITUIDO`) — para renovar, basta subir o novo `.pfx`.

#### `DELETE /v1/certificados/{id}`

Desativa o certificado (soft-delete — o histórico permanece listado com
`ativo: false`). Idempotente: desativar um certificado já inativo devolve
`204` de novo. `404` se não existir para o tenant.

#### `POST /v1/certificados/{id}/ativar`

Reativa um certificado anterior, desativando os demais ativos (volta atrás
numa rotação). Idempotente: ativar um certificado já ativo só devolve o
estado atual. `200 OK` com o mesmo corpo do upload; `404` se não existir.

#### `GET /v1/certificados`

Lista os certificados do tenant.

`200 OK`:

```json
[
  {
    "id": "9d2f1b3a-4c5d-4e6f-8a9b-0c1d2e3f4a5b",
    "thumbprint": "3A7B9C2D...",
    "validoAte": "2027-05-20",
    "ativo": true,
    "criadoEm": "2026-09-03T11:00:00.123+00:00"
  }
]
```

---

### Emissão

#### `POST /v1/documentos-fiscais/nfe` 🔒

Emite uma **NF-e** (modelo 55). Header `Idempotency-Key` obrigatório.

Corpo: [`EmissaoRequest`](#emissaorequest) — referência completa de campos na seção de DTOs.

**Respostas:**

| Código | Quando |
|---|---|
| `202 Accepted` | Documento aceito, processamento iniciado. Header `Location` aponta para a consulta. |
| `200 OK` | **Replay de idempotência**: mesma `Idempotency-Key` já usada → devolve o estado atual do documento existente. |
| `400` | `Idempotency-Key` ausente/vazia, ou payload inválido (validação de campos). |
| `403` | `ambiente` do payload divergente do ambiente da API key. |
| `422` | `ambiente` não é `"producao"`/`"homologacao"`, ou inconsistência aritmética nos valores (extensão `campo` indica onde). |

Exemplo de `202`:

```json
{
  "id": "e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c",
  "status": "PENDENTE",
  "ambiente": "homologacao",
  "criadoEm": "2026-09-03T12:00:00.123+00:00",
  "links": {
    "consulta": "/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c"
  }
}
```

#### `POST /v1/documentos-fiscais/nfce` 🔒

Idêntico ao anterior, para **NFC-e** (modelo 65). Mesmo corpo (`EmissaoRequest`), mesmas respostas.

> NFC-e exige o CSC (código de segurança do consumidor) fornecido pela SEFAZ do estado — configure-o junto com o perfil fiscal do emitente via `PUT /v1/tenants/perfil` (o CSC fica cifrado com a KEK).

#### `POST /v1/documentos-fiscais/nfse/dps` 🔒

NFS-e padrão Nacional com **transmissão DPS real** (layout 1.01, autorização
síncrona). Header `Idempotency-Key` obrigatório. Corpo próprio:

```json
{
  "ambiente": "homologacao",
  "serie": 1,
  "dataCompetencia": "2026-09-05",
  "tomador": {
    "cnpjCpf": "11122233000144",
    "nome": "Cliente Serviço Ltda",
    "endereco": {
      "codigoMunicipioIbge": "3550308", "cep": "01001000",
      "logradouro": "Praça da Sé", "numero": "1", "bairro": "Sé"
    }
  },
  "servico": {
    "codigoTributarioNacional": "010701",
    "descricaoServico": "Desenvolvimento de software",
    "codigoNbs": "112011000"
  },
  "valores": {
    "valorServicos": 1000,
    "tributacaoIssqn": 1,
    "retencaoIssqn": 1,
    "aliquotaIssqn": 5
  },
  "ibscbs": {
    "finalidade": 0,
    "indicadorFinal": 1,
    "codigoIndicadorOperacao": "000001",
    "indicadorDestinatario": 0,
    "gibbsCbs": { "cst": "101", "cClassTrib": "000001" }
  },
  "informacoesComplementares": "texto livre"
}
```

Regras: `valorServicos` ≥ 0; `cTribNac` e `xDescServ` obrigatórios; cNBS 9
dígitos; bloco `ibscbs` (RTC) validado quando informado. O prestador é o
perfil fiscal do tenant (`PUT /v1/tenants/perfil` — inscrição municipal
obrigatória na prática). Autorização síncrona: `chaveAcesso` de 50 dígitos
(prefixo `NFS`). Fora de sandbox exige certificado A1 e credenciamento na
SEFAZ Nacional.

#### `POST /v1/documentos-fiscais/{id}/substituicao` 🔒

Substituição de NFS-e autorizada: emite o DPS **substituto** (corpo:
`{ "dps": { ...mesmo formato de /nfse/dps... }, "cMotivo": 5,
"xMotivo": "Rejeitada pelo tomador" }`). A SEFAZ desativa a original quando
autoriza a substituta; o novo documento segue o fluxo normal (consulta por
`GET /{id}` do novo id). `cMotivo` aceita 1–5 e 99 (99 exige `xMotivo`).

| Código | Quando |
|---|---|
| `202 Accepted` | Substituta aceita e enfileirada (`substituidaId` + novo `id`). |
| `400` | `Idempotency-Key` ausente. |
| `409` | Documento do path não está AUTORIZADA. |
| `422` | Documento não é NFS-e; `cMotivo` inválido; DPS inconsistente. |

#### `GET /v1/documentos-fiscais/{id}`

Consulta o estado atual do documento. `id` é o GUID retornado no `202`.

`200 OK` — [`EmissaoResponse`](#emissaoresponse):

```json
{
  "id": "e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c",
  "tipo": "NFE",
  "status": "REJEITADA",
  "ambiente": "homologacao",
  "serie": 1,
  "numero": 42,
  "chaveAcesso": "41260912345678000199550010000000421012345678",
  "protocoloAutorizacao": null,
  "xmlAssinado": "<NFe xmlns=\"...\">...</NFe>",
  "xmlRetornoSefaz": "<retEnviNFe xmlns=\"...\"><cStat>539</cStat>...</retEnviNFe>",
  "motivoStatus": "Rejeição 539: Duplicidade de NF-e...",
  "criadoEm": "2026-09-03T12:00:00.123+00:00",
  "atualizadoEm": "2026-09-03T12:00:02.789+00:00"
}
```

`404` se o documento não existir ou pertencer a outro tenant (sem corpo).

O campo `xmlRetornoSefaz` traz a resposta literal da SEFAZ — em rejeições é ali que está o detalhe técnico completo; `motivoStatus` é o resumo legível.

---

### Tenants — perfil fiscal e webhooks

#### `GET /v1/tenants/perfil` 🔒 / `PUT /v1/tenants/perfil` 🔒

Perfil fiscal do emitente (exigido para emissão real). Campos nulos no PUT
não são alterados. O `csc`/`cscId` (NFC-e) é opcional e o CSC é armazenado
cifrado — a resposta só indica `cscCadastrado`.

#### `GET /v1/tenants/webhooks` 🔒

Configuração de webhook do tenant:

```json
{
  "webhookUrl": "https://integrador.example.com/hook",
  "webhookSecretCadastrado": true
}
```

O segredo nunca é devolvido — só o booleano indica que existe.

#### `PUT /v1/tenants/webhooks` 🔒

Define a URL de entrega e o segredo HMAC (self-service — dispensa o painel
admin). Campos nulos não são alterados:

```json
{
  "webhookUrl": "https://integrador.example.com/hook",
  "webhookSecret": "um-segredo-de-ao-menos-16-chars"
}
```

| Código | Quando |
|---|---|
| `200 OK` | Configuração atualizada (`webhookUrl` + `webhookSecretCadastrado`). |
| `400` | Corpo sem nenhum campo. |
| `422` | `webhookUrl` não é URL absoluta http(s); `webhookSecret` < 16 ou > 200 caracteres. |

O segredo é armazenado **cifrado em repouso** (mesmo envelope AES-GCM do CSC
e dos certificados) e assina as entregas com HMAC-SHA256
(`X-Fiscal-Signature`, janela anti-replay de 5 min).

---

### Eventos fiscais

Os POSTs de evento exigem `Idempotency-Key`. Um evento é persistido, associado
ao documento e **transmitido à SEFAZ** pelo Worker (`TransmissorEventoUnimake` —
cancelamento 110111, CC-e 110110, inutilização). O status do evento acompanha
no `GET /v1/documentos-fiscais/{id}` (`eventos[].status`).

#### `POST /v1/documentos-fiscais/{id}/cancelamento` 🔒

Solicita o cancelamento de um documento autorizado.

Corpo:

| Campo | Tipo | Obrigatório | Regra |
|---|---|---|---|
| `justificativa` | string | sim | 15 a 1000 caracteres (regra SEFAZ) |

```bash
curl -X POST "$BASE/v1/documentos-fiscais/e7a1c9f2-.../cancelamento" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"justificativa": "Pedido cancelado pelo cliente antes do faturamento"}'
```

`200 OK`:

```json
{
  "documentoId": "e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c",
  "eventoId": "1a2b3c4d-5e6f-4a5b-8c9d-0e1f2a3b4c5d",
  "tipo": "CANCELAMENTO",
  "status": "PENDENTE",
  "criadoEm": "2026-09-03T12:30:00.123+00:00"
}
```

O documento passa a `CANCELAMENTO_PENDENTE`. Erros: `400` sem `Idempotency-Key` ou justificativa fora de 15–1000 chars; `404` documento inexistente/de outro tenant; `409` documento não está `AUTORIZADA` (o `detail` informa o status atual).

#### `POST /v1/documentos-fiscais/{id}/carta-correcao` 🔒

Aplica carta de correção (**CC-e**) — válida **apenas para NF-e (modelo 55)**. Para NFC-e, cancele e reemita.

Corpo:

| Campo | Tipo | Obrigatório | Regra |
|---|---|---|---|
| `correcao` | string | sim | 15 a 1000 caracteres |

```bash
curl -X POST "$BASE/v1/documentos-fiscais/e7a1c9f2-.../carta-correcao" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"correcao": "Corrige o valor do frete destacado no item 1"}'
```

`200 OK` — mesmo formato do cancelamento, com `"tipo": "CCE"`.

Erros adicionais: `409` se o documento não for NF-e (modelo 55).

#### `POST /v1/inutilizacoes` 🔒

Inutiliza uma faixa de numeração (ex.: notas que pularam número). Não está ligada a um documento existente.

Corpo:

| Campo | Tipo | Obrigatório | Regra |
|---|---|---|---|
| `ambiente` | string | sim | `"producao"` ou `"homologacao"` (divergente da chave → `403`) |
| `modelo` | número | sim | `55` ou `65` |
| `serie` | número | sim | 1 a 999 |
| `numeroInicial` | número | sim | ≥ 1 |
| `numeroFinal` | número | sim | ≥ `numeroInicial` (senão `422`) |
| `justificativa` | string | sim | 15 a 1000 caracteres |

```bash
curl -X POST "$BASE/v1/inutilizacoes" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "ambiente": "homologacao",
    "modelo": 55,
    "serie": 1,
    "numeroInicial": 10,
    "numeroFinal": 12,
    "justificativa": "Numeracao pulada por falha de rede no balcao"
  }'
```

`202 Accepted` (header `Location: /v1/inutilizacoes/{eventoId}`):

```json
{
  "eventoId": "6f5e4d3c-2b1a-4c5d-8e9f-0a1b2c3d4e5f",
  "tipo": "INUTILIZACAO",
  "status": "PENDENTE",
  "criadoEm": "2026-09-03T12:40:00.123+00:00",
  "modelo": 55,
  "serie": 1,
  "numeroInicial": 10,
  "numeroFinal": 12
}
```

Erros: `400` sem `Idempotency-Key`; `403` ambiente divergente da chave; `422` ambiente inválido ou `numeroFinal < numeroInicial`.

#### `GET /v1/inutilizacoes/{eventoId}`

Consulta o estado de um pedido de inutilização — é para onde aponta o header `Location` do `202`. `404` se o evento não existir ou pertencer a outro tenant.

```bash
curl -H "Authorization: ApiKey $CHAVE" \
  "$BASE/v1/inutilizacoes/6f5e4d3c-2b1a-4c5d-8e9f-0a1b2c3d4e5f"
```

`200 OK`:

```json
{
  "eventoId": "6f5e4d3c-2b1a-4c5d-8e9f-0a1b2c3d4e5f",
  "tipo": "INUTILIZACAO",
  "status": "PENDENTE",
  "criadoEm": "2026-09-03T12:40:00.123+00:00"
}
```

---

## Referência de DTOs

### `EmissaoRequest`

Corpo do POST de emissão (`/nfe` e `/nfce`):

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `ambiente` | string | sim | `"producao"` ou `"homologacao"` |
| `serie` | número | sim | 1 a 999 |
| `destinatario` | objeto | não | [`Destinatario`](#destinatario) |
| `itens` | array | sim | 1 ou mais [`Item`](#item) |
| `totais` | objeto | sim | [`Totais`](#totais) |
| `pagamento` | array | não | [`Pagamento`](#pagamento) |
| `naturezaOperacao` | string | não | Ex.: `"Venda de mercadoria"` |
| `finalidade` | string | não | `normal` (default) \| `complementar` \| `ajuste` \| `devolucao` (finNFe) |
| `tipoOperacao` | string | não | `saida` (default) \| `entrada` (tpNF) |
| `indicadorPresenca` | string | não | `presencial` \| `internet` \| `teleatendimento` \| `entrega_domicilio` \| `fora_estabelecimento` \| `outros` (indPres) |
| `indicadorConsumidorFinal` | string | não | `sim` (default) \| `nao` (indFinal) |
| `nfesReferenciadas` | array | devolução | Chaves de 44 dígitos (`[{ "chaveAcesso": "..." }]`) → grupo `NFref`. **Obrigatória quando `finalidade = "devolucao"`** |
| `indicadorIntermediador` | int | não | NT 2020.006 (indIntermed, **só NF-e 55**): `0` = operação sem intermediador (default) \| `1` = operação em site/plataforma de terceiros. SEFAZ-PR rejeita com **434** quando ausente na NF-e |
| `cnpjIntermediador` | string | quando `indicadorIntermediador = 1` | CNPJ do intermediador da transação → grupo `infIntermed` |

```json
{
  "ambiente": "homologacao",
  "serie": 1,
  "naturezaOperacao": "Venda de mercadoria",
  "destinatario": { "...": "ver Destinatario" },
  "itens": [ { "...": "ver Item" } ],
  "totais": { "valorProdutos": 100, "valorNota": 100 },
  "pagamento": [ { "forma": "01", "valor": 100 } ]
}
```

### `Destinatario`

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `cnpjCpf` | string | sim | Só dígitos, até 14 (CNPJ) ou 11 (CPF) |
| `nome` | string | sim | Até 200 |
| `inscricaoEstadual` | string | não | Isento: `"ISENTO"` |
| `endereco` | objeto | não | [`Endereco`](#endereco) |

### `Endereco`

Todos os campos são opcionais, mas um endereço consistente é necessário para emissão real.

| Campo | Tipo | Regras |
|---|---|---|
| `cep` | string | Até 8, só dígitos |
| `logradouro` | string | Até 100 |
| `numero` | string | Até 10 |
| `complemento` | string | Até 100 |
| `bairro` | string | Até 100 |
| `codigoMunicipioIbge` | string | Até 7 (código IBGE) |
| `uf` | string | Até 2 (sigla, ex.: `"PR"`) |
| `nomeMunicipio` | string | Nome por extenso |

### `Item`

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `codigo` | string | sim | Até 60 (seu SKU) |
| `descricao` | string | sim | Até 200 |
| `ncm` | string | não | 8 dígitos |
| `cfop` | string | não | 4 dígitos |
| `quantidade` | número | sim | > 0 |
| `valorUnitario` | número | sim | ≥ 0 |
| `valorTotal` | número | sim | ≥ 0 — deve bater com `quantidade × valorUnitario` (tolerância 0,01) |
| `impostos` | array | não | [`Imposto`](#imposto) — **legado** |
| `impostosV2` | objeto | não | [`ImpostosV2`](#impostosv2) — grupos tipados (v2) |
| `cest` | string | não | 7 dígitos (v2 F2) |
| `gtin` | string | não | EAN 8/12/13/14 (v2 F2) — default `"SEM GTIN"` |
| `unidade` | string | não | Até 6 (uCom/uTrib) — default `"UN"` (v2 F2) |
| `valorDesconto` | número | não | ≥ 0 — vDesc do item (v2 F2) |

> **`impostos` OU `impostosV2`, nunca os dois no mesmo item** — enviar os dois → `400`.

### `Imposto` (legado)

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `cst` | string | sim | Até 3 dígitos — suporta `00`, `40`, `41`, `50` |
| `baseCalculo` | número | não | |
| `aliquota` | número | não | Em %, ex.: `18` para 18% |
| `valor` | número | não | Deve bater com `baseCalculo × aliquota / 100` (tolerância 0,01) |

Regra especial: CSTs de isenção (`40`, `41`, `50`, `60`) não podem ter `valor > 0`.

### `impostosV2` — ICMS completo + CSOSN (contrato v2)

Grupo tipado que cobre **todo o ICMS do layout 4.00**: CST `00/10/20/40/41/51/60/70/90`
e CSOSN do **Simples Nacional** `101/102/103/201/202/203/300/400/500/900`, com ST,
FCP e DIFAL. Referência: [docs/plano-evolucao-contrato-v2.md](plano-evolucao-contrato-v2.md).

```json
{
  "impostosV2": {
    "icms": {
      "origem": 0,
      "csosn": "102"
    }
  }
}
```

**`impostosV2.icms`** — informe `cst` (regime normal) **ou** `csosn` (Simples Nacional), nunca os dois:

| Campo | Tipo | Regras |
|---|---|---|
| `origem` | int | 0–8 (tabela A) — default `0` (nacional) |
| `cst` | string | `00`, `10`, `20`, `40`, `41`, `51`, `60`, `70`, `90` |
| `csosn` | string | `101`, `102`, `103`, `201`, `202`, `203`, `300`, `400`, `500`, `900` |
| `modBc` | string | 0–3 — default `3` (valor da operação) |
| `percentualReducaoBc` | número | CST 20/51/70 |
| `baseCalculo` / `aliquota` / `valor` | número | Trio da tributação própria (obrigatório em 00/10/20/70) |
| `percentualCreditoSimples` / `valorCreditoSimples` | número | pCredSN/vCredICMSSN — CSOSN 101/201/900 |
| `fcpPercentual` / `valorFcp` | número | FCP próprio (base = `baseCalculo`) |
| `valorIcmsOperacao` / `percentualDiferimento` / `valorIcmsDiferido` | número | CST 51 (diferimento) |
| `st` | objeto | [`IcmsSt`](#icmsst) — ST própria (10/70/90, CSOSN 201/202/203/900) ou retida (60/500) |
| `difal` | objeto | [`Difal`](#difal) — interestadual consumidor final |

**`impostosV2.icms.st`** (`IcmsSt`):

| Campo | Tipo | Regras |
|---|---|---|
| `modBcSt` | string | 0–6 — obrigatório na ST própria |
| `percentualMva` / `percentualReducaoBcSt` | número | Opcionais da ST própria |
| `baseCalculoSt` / `aliquotaSt` / `valorSt` | número | Trio da ST própria (obrigatório em 10/70, CSOSN 201/202/203) |
| `fcpPercentualSt` / `valorFcpSt` | número | FCP da ST própria |
| `baseCalculoStRetido` / `aliquotaStRetida` / `valorStRetido` | número | vBCSTRet/pST/vICMSSTRet — CST 60, CSOSN 500 |
| `valorIcmsSubstituto` | número | vICMSSubstituto |
| `fcpPercentualStRetido` / `valorFcpStRetido` | número | FCP-ST retido |

**`impostosV2.icms.difal`** (`Difal`) — CST interestadual + consumidor final
(partilha 100% destino, Convênio 190/2017):

| Campo | Tipo | Regras |
|---|---|---|
| `aliquotaInterestadual` | int | 4, 7 ou 12 (pICMSInter) — obrigatória |
| `baseDestino` / `aliquotaDestino` / `valorIcmsDestino` / `valorIcmsOrigem` | número | vBCUFDest/pICMSUFDest/vICMSUFDest/vICMSUFRemet. Fórmula do MOC (rejeições SEFAZ **815/816**): `valorIcmsDestino = baseDestino × (aliquotaDestino − aliquotaInterestadual)`; `valorIcmsOrigem = 0` na partilha vigente |
| `fcpPercentualDestino` / `valorFcpDestino` | número | pFCPUFDest/vFCPUFDest |

**`impostosV2.ipi`** (`Ipi`) — só NF-e (NFC-e rejeita):

| Campo | Tipo | Regras |
|---|---|---|
| `cst` | string | `00/49/50/99` tributado (exige trio); `01–05/51` não tributado |
| `cEnq` | string | 3 dígitos — default `999` |
| `baseCalculo` / `aliquota` / `valor` | número | trio do IPI |

**`impostosV2.pis`** e **`impostosV2.cofins`** — só NF-e:

| Campo | Tipo | Regras |
|---|---|---|
| `cst` | string | `01/02` tributado (exige trio); `04–09` isento (sem valor); `99` outras. `03` (por quantidade) não suportado |
| `baseCalculo` / `aliquota` / `valor` | número | trio |

**`impostosV2.ibsCbs`** (v2 F5 — reforma, LC 214/2025 / NT 2025.x):

| Campo | Tipo | Regras |
|---|---|---|
| `cstIbsCbs` | string | 3 dígitos (tabela SEPEC) |
| `cClassTrib` | string | 6 dígitos (tabela SEPEC) — obrigatório |
| `baseCalculo` | número | vBC do bloco |
| `aliquotaIbsEstadual` / `valorIbsEstadual` | número | gIBSUF |
| `aliquotaIbsMunicipal` / `valorIbsMunicipal` | número | gIBSMun |
| `aliquotaCbs` / `valorCbs` | número | gCBS |

**`impostosV2.is`** (v2 F5 — Imposto Seletivo):

| Campo | Tipo | Regras |
|---|---|---|
| `cstIs` | string | 2 dígitos (SEPEC) |
| `cClassTribIs` | string | 6 dígitos — obrigatório |
| `baseCalculo` / `aliquota` / `valor` | número | trio do IS |
| `unidadeTributavel` / `quantidadeTributavel` | string/número | IS por quantidade — sempre juntos |

Exemplo completo — CST 10 (tributada + ST):

```json
{
  "impostosV2": {
    "icms": {
      "origem": 0,
      "cst": "10",
      "baseCalculo": 100, "aliquota": 18, "valor": 18,
      "st": { "modBcSt": "4", "baseCalculoSt": 130, "aliquotaSt": 18, "valorSt": 23.4 }
    }
  }
}
```

### `Totais`

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `valorProdutos` | número | sim | ≥ 0 |
| `valorNota` | número | sim | ≥ 0 — regra abaixo (tolerância 0,01) |
| `valorDesconto` | número | não | ≥ 0 (v2 F2) |
| `valorFrete` | número | não | ≥ 0 — compõe o total da nota; `modFrete` vira CIF (v2 F2) |
| `valorSeguro` | número | não | ≥ 0 (v2 F2) |
| `outrasDespesas` | número | não | ≥ 0 (v2 F2) |

**Fórmula do `valorNota`** (determinística):

- Payload atual (sem campos novos): `valorNota = Σ valorTotal` dos itens.
- **Fórmula v2** (qualquer campo novo preenchido, incluindo desconto por
  item): `valorNota = Σ brutos − descontos + frete + seguro + outras + ST +
  FCP-ST + IPI` (FCP próprio e DIFAL não compõem o total, como na SEFAZ).

### `Pagamento`

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `forma` | string | sim | Até 2 dígitos — tabela da SEFAZ (`01` dinheiro, `03` cartão de crédito, `04` cartão de débito…) |
| `valor` | número | sim | ≥ 0 |

### `EmissaoResponse`

Resposta do `GET /v1/documentos-fiscais/{id}` (e do replay de idempotência):

| Campo | Tipo | Descrição |
|---|---|---|
| `id` | string (GUID) | Identificador do documento na API |
| `tipo` | string | `"NFE"`, `"NFCE"` ou `"NFSE"` |
| `status` | string | Ver [tabela de status](#statusdocumento) |
| `ambiente` | string | `"producao"` ou `"homologacao"` |
| `serie` | número | Série informada na emissão |
| `numero` | número | Número reservado (sequencial por série) |
| `chaveAcesso` | string \| null | Chave de acesso de 44 dígitos (após autorização) |
| `protocoloAutorizacao` | string \| null | Protocolo SEFAZ (após autorização) |
| `xmlAssinado` | string \| null | XML da NF-e assinado com o certificado |
| `xmlRetornoSefaz` | string \| null | Retorno literal da SEFAZ (protocolo ou rejeição) |
| `motivoStatus` | string \| null | Motivo legível do status atual (rejeição, erro etc.) |
| `criadoEm` | string (data) | Criação do documento |
| `atualizadoEm` | string (data) | Última atualização |

Campos `null` são comuns antes da autorização: um documento `PENDENTE` tem chave, protocolo e XMLs vazios.

---

## Validações da API

### O que gera `400` (validação de campos)

Aplicada antes de qualquer efeito, em forma de `ValidationProblemDetails` (ver [Modelo de erros](#modelo-de-erros)):

- Campos obrigatórios ausentes (`ambiente`, `itens`, `totais`, `cnpjCpf`/`nome` do destinatário quando enviado, `codigo`/`descricao` do item, `cst`, `forma`…).
- Limites de tamanho excedidos (ex.: `descricao` > 200, `ncm` > 8).
- Ranges violados (`serie` fora de 1–999, `quantidade` ≤ 0, valores negativos).
- Item com `impostos` **e** `impostosV2` simultâneos (ambíguo).
- `justificativa`/`correcao` de eventos fora de 15–1000 caracteres.
- `Idempotency-Key` ausente nos POSTs que a exigem.

### O que gera `422` (validação semântica)

- `ambiente` com valor diferente de `"producao"`/`"homologacao"`.
- **Inconsistência aritmética** (tolerância de R$ 0,01):
  1. Soma dos `valorTotal` dos itens ≠ `totais.valorNota` → `campo: "valorTotal"`.
  2. `quantidade × valorUnitario` ≠ `valorTotal` do item → `campo: "itens[i].valorTotal"`.
  3. `baseCalculo × aliquota / 100` ≠ `valor` do imposto → `campo: "impostos[i].valor"` (só quando os três campos são informados). No `impostosV2` a aritmética se estende a ST, FCP, FCP-ST e DIFAL.
  4. CST/CSOSN de isenção (`40`, `41`, `50`, `60`; CSOSN `300`, `400`) com `valor > 0` → `campo: "impostos[i].valor"`.
- **Fórmula v2 do `valorNota`** com campo novo presente e total incoerente (título `"Inconsistência no total da nota"`).
- **Regras declarativas do `impostosV2`** (título `"Inconsistência nos grupos de imposto v2"`):
  1. `cst` e `csosn` no mesmo grupo (ou nenhum dos dois).
  2. `cst`/`csosn` fora das listas suportadas (ex.: CST `30`, `ICMSPart` — fail-loud).
  3. Campos obrigatórios por código: CST 00 sem trio, CST 10/70 sem `st` completa, CST 20/70 sem `percentualReducaoBc`, CST 51 sem `valorIcmsOperacao`, CSOSN 201/202/203 sem `st`…
  4. `difal.aliquotaInterestadual` fora de 4/7/12 ou partilha incompleta, ou `valorIcmsDestino` ≠ `baseDestino × (aliquotaDestino − aliquotaInterestadual)`.
- `numeroFinal < numeroInicial` na inutilização.
- `.pfx` que não abre com a senha informada (upload de certificado).

O `422` por aritmética vem com a extensão `campo` apontando o local exato:

```json
{
  "title": "Inconsistência nos valores do documento",
  "status": 422,
  "detail": "Item SKU1: 2 × 50,00 = 100,00, recebido 90,00.",
  "campo": "itens[0].valorTotal"
}
```

### O que a API **não** faz

- **Não calcula tributos.** Base, alíquota e valor chegam prontos do seu sistema; a API só confere a aritmética.
- **Não valida** NCM, CFOP, CST ou forma de pagamento contra as tabelas oficiais — valores inválidos serão rejeitados pela SEFAZ no processamento (visível via `motivoStatus`/`xmlRetornoSefaz`).
- **Não valida** dígito verificador de CNPJ/CPF.
- **Não cadastra produtos nem clientes** — o request é autocontido.

---

## Status de documentos e enumerações

### `StatusDocumento`

| Valor | Terminal? | Significado |
|---|---|---|
| `PENDENTE` | não | Aceito, na fila de processamento |
| `PROCESSANDO` | não | Em transmissão/consulta com a SEFAZ |
| `AUTORIZADA` | **sim** | Autorizada pela SEFAZ — use `chaveAcesso` e XMLs |
| `REJEITADA` | **sim** | Rejeitada — leia `motivoStatus`, corrija e reemita (novo POST, nova Idempotency-Key) |
| `CONTINGENCIA` | não | Erro de transmissão transitório; retry automático em andamento |
| `CANCELAMENTO_PENDENTE` | não | Cancelamento solicitado, aguardando conclusão |
| `CANCELADA` | **sim** | Cancelamento confirmado |
| `ERRO_CANCELAMENTO` | — | Reservado (ainda não produzido pela API) |
| `DENEGADA` | **sim** | Uso denegado pela SEFAZ (não circula, mas o número foi consumido) |
| `ERRO_INTERNO` | **sim** | Falha não recuperável do lado da API — veja `motivoStatus` |

> Em `REJEITADA`/`DENEGADA`/`ERRO_INTERNO` o número da série já foi consumido. Uma reemissão cria um documento com um **novo número**; a faixa perdida pode ser limpa com [inutilização](#post-v1inutilizacoes-).

### Outras enumerações

| Enum | Valores |
|---|---|
| `ambiente` (payload) | `"producao"`, `"homologacao"` |
| `tipo` (response) | `"NFE"`, `"NFCE"`, `"NFSE"` |
| `tipo` (eventos) | `"CANCELAMENTO"`, `"CCE"`, `"INUTILIZACAO"` |
| status de evento | `"PENDENTE"`, `"PROCESSANDO"`, `"PROCESSADO"`, `"REJEITADO"`, `"ERRO"` |
| status de documento | `"PENDENTE"`, `"PROCESSANDO"`, `"AUTORIZADA"`, `"REJEITADA"`, `"CONTINGENCIA"`, `"CANCELAMENTO_PENDENTE"`, `"CANCELADA"`, `"ERRO_CANCELAMENTO"`, `"DENEGADA"`, `"ERRO_INTERNO"`, `"FALHA_EMISSAO"` |
| tipos de webhook | `documento.autorizado`, `documento.rejeitado`, `documento.denegado`, `documento.falha_emissao`, `documento.cancelado`, `documento.carta_correcao`, `nota.recebida`, `manifestacao.processada` |

---

### Webhooks (outbox do tenant)

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| `GET` | `/v1/webhooks?page=1&pageSize=50&status=FALHA` | ApiKey | Lista entregas de webhook do tenant (mais recentes primeiro; filtro `status`: PENDENTE, ENTREGANDO, ENTREGUE, FALHA) |
| `POST` | `/v1/webhooks/{id}/reenviar` | ApiKey | Reenvio manual: FALHA volta a PENDENTE com ciclo novo (8 tentativas); PENDENTE tem a tentativa adiantada para agora; ENTREGUE/ENTREGANDO → 409. Retorna 202 com o estado da entrega |

### NFC-e offline (tpEmis 9)

`POST /v1/documentos-fiscais/nfce` aceita `contingenciaOffline: true` — o XML é
gerado com tpEmis 9 e a transmissão segue pelo fluxo de contingência (janela de
**24h**; fora dela o documento vai para `FALHA_EMISSAO`). Indicado quando o ERP
precisa registrar a venda antes da autorização.

### Campos do backlog v2 (§7 do plano-evolucao-contrato-v2)

- `transporte`: `modalidadeFrete` (0–9), `transportadora` (cnpjCpf, nome, IE,
  endereço) e `volumes[]` (quantidade, espécie, marca, numeração, pesos, lacres[]).
- `pagamento[]`: `tipoIntegracao` ("1"/"2"), `bandeira` (código tBand),
  `autorizacao` (cAut), `cnpjCredenciadora` — informados juntos formam o grupo card.
- `impostosV2.icms` CST 10 partilha: `percentualBcOperacao` (pBCOp) e/ou `ufSt`
  (UFST) presentes → grupo ICMSPart (ST própria passa a ser opcional).
- `itens[].dis[]`: grupo DI por item importado (número, datas, local/UF de
  desembaraço, via transporte 1–12, forma intermediação 1–3, exportador,
  adicoes[]) + `impostosV2.ii` (vBC, vDespAdu, vII, vIOF — vII soma nos totais).

## Modelo de erros

Erros de negócio seguem **RFC 7807** (`application/problem+json`):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Documento não está em estado válido para este evento.",
  "status": 409,
  "detail": "Status atual: REJEITADA. Apenas documentos AUTORIZADA podem receber cancelamento/CC-e."
}
```

- `title`: resumo curto do problema.
- `detail`: explicação contextual (status atual, valores recebidos).
- Extensões extras podem aparecer (ex.: `campo` nos `422` de aritmética).

Erros de validação de campos usam o formato `ValidationProblemDetails`, com o dicionário `errors` apontando cada campo e mensagem:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Itens": ["Itens must contain at least 1 item."]
  }
}
```

### Tabela de códigos HTTP

| HTTP | Causa típica |
|---|---|
| `200 OK` | Sucesso em consulta ou replay de idempotência |
| `201 Created` | Recurso criado (API key, certificado) |
| `202 Accepted` | Emissão/evento aceito, processamento assíncrono |
| `204 No Content` | Revogação de API key |
| `400 Bad Request` | `Idempotency-Key` ausente; campos inválidos; multipart malformado |
| `401 Unauthorized` | Sem header, chave inválida/revogada, `X-Fiscal-Ambiente` divergente |
| `403 Forbidden` | Chave válida, mas ambiente do payload divergente do ambiente da chave |
| `404 Not Found` | Documento/recurso inexistente **ou pertencente a outro tenant** |
| `409 Conflict` | Estado incompatível (cancelar fora de `AUTORIZADA`; CC-e em NFC-e) |
| `422 Unprocessable Entity` | Semântica inválida: ambiente, aritmética, faixa de inutilização, `.pfx` inválido |
| `500` | Erro não tratado — não esperado em operação normal |

### Erros assíncronos

Problemas que acontecem **depois** do `202` não viram erro HTTP — aparecem como status do documento + `motivoStatus` no polling (ex.: rejeição SEFAZ). É por isso que o polling até estado terminal é obrigatório no fluxo de integração.

---

## Boas práticas para o integrador

1. **Uma `Idempotency-Key` por tentativa lógica de emissão.** Gere um UUID quando o pedido entrar na fila de faturamento e reutilize-o em qualquer retry daquela emissão. Trocar a key a cada retry pode criar documentos duplicados (e consumir números).
2. **Persista o `id` do `202` imediatamente** e associe-o ao pedido do seu ERP — é a chave de toda consulta e evento posterior.
3. **Faça polling com backoff** (2s → 5s → 10s → … teto ~60s). Não consulte em loop apertado; um documento normalmente resolve em segundos, mas `CONTINGENCIA` pode levar minutos por design.
4. **Trate todos os estados terminais explicitamente.** `REJEITADA`/`DENEGADA` pedem intervenção do operador; `ERRO_INTERNO` pede verificação de `motivoStatus` (muitas vezes: certificado expirado/ausente).
5. **Armazene os XMLs devolvidos** (`xmlAssinado`, `xmlRetornoSefaz`) assim que autorizado — são a obrigação acessória do seu lado. A API os mantém disponíveis na consulta, mas guardá-los no seu domínio simplifica auditoria.
6. **Retry de rede**: em timeout/5xx/429, repita a chamada com **a mesma** `Idempotency-Key` — é seguro.
7. **Não reutilize séries entre ambientes** sem saber: a numeração é sequencial por (modelo, série, ambiente); use séries distintas para homologação e produção se quiser manter históricos limpos.
8. **Monitore a validade do certificado** via `GET /v1/certificados` (`validoAte`) — a emissão falha (`ERRO_INTERNO`) quando ele vence.

---

## Limitações conhecidas

Versão atual (`1.12.2-alpha`) — considere no desenho da sua integração:

- **Homologação real em andamento** (SEFAZ-PR, A1 real, 2026-09-17):
  status-serviço e transmissão validados; autorização ponta a ponta pendente
  de IE real do emitente (roteiro e rejeições vistas na seção 9 do
  guia-primeira-emissao.md).
- **NFS-e** (modelo de serviço, padrão Nacional/DPS): transmissão DPS real
  implementada (Unimake, layout 1.01 síncrono); ainda sem bateria de
  homologação dedicada.
- **Mapper NF-e cobre ICMS completo** (CST 00–90, CSOSN 101–900, ST, FCP,
  DIFAL via `impostosV2`); **IPI/PIS/COFINS, desconto/frete/seguro, GTIN/
  unidade configuráveis, NF-ref e transporte** entram nas fases seguintes do
  contrato v2 (docs/plano-evolucao-contrato-v2.md). Unidade fixa `UN` e
  GTIN `SEM GTIN` por enquanto.
- **DANFE simplificado** (sem código de barras/QR do leiaute oficial).
- **EPEC e NFC-e offline (tpEmis 9)** ficam para sprint futura.
- **Certificado**: apenas A1 (`.pfx`). A3/HSM não são suportados.

Dúvidas sobre o roadmap: consulte o `CHANGELOG.md` e o `README.md` na raiz do repositório.
