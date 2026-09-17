# FiscalAPI — Guia da primeira emissão (sandbox)

Roteiro prático de onboarding para um **ERP/integrador** que precisa ir do
zero até a primeira nota **autorizada com PDF (DANFE/DANFCe/DANFSe)** usando a
FiscalAPI em **modo sandbox** (`MODO_SANDBOX=true` → `EmissorMock`, sem
certificado A1 e sem contato com a SEFAZ).

Este guia é o **caminho feliz**, passo a passo, com os payloads mínimos que o
código aceita. A **referência completa** — todos os endpoints, DTOs,
validações e estados — continua em [docs/integracao-api.md](integracao-api.md);
quando uma seção tiver mais detalhe, há um link para lá.

> Compatibilidade: API `1.x-alpha`. Base usada nos exemplos:
> `http://localhost:8080` (stack Docker). Troque pela URL do seu ambiente.
> Os exemplos em bash usam `curl`; em todo POST que **cria documento/evento**
> o header `Idempotency-Key` é obrigatório.

---

## Índice do roteiro

| # | Passo | Resultado |
|---|---|---|
| 1 | [Subir a stack](#1-subir-a-stack-docker-modo-sandbox) | API + Worker + Postgres no ar |
| 2 | [Criar a empresa (tenant)](#2-criar-a-empresa-tenant) | `tenantId` (GUID) — e a 1ª API key, com o atalho `criarApiKey` |
| 3 | [Gerar a API key e autenticar](#3-gerar-a-api-key-e-autenticar) | `fk_test_…` + header `Authorization` |
| 4 | [(Opcional) Certificado A1](#4-opcional-certificado-a1) | dispensa no sandbox |
| 5 | [Emitir a 1ª nota](#5-emitir-a-primeira-nota) | `202` com `id` + `status: PENDENTE` |
| 6 | [Consultar até autorizar](#6-consultar-até-a-autorização) | `status: AUTORIZADA` + chave/protocolo |
| 7 | [Baixar o PDF](#7-baixar-o-pdf-danfedanfcedanfse) | `GET …/{id}/pdf` → PDF binário |
| 8 | [(Opcional) Webhook + HMAC](#8-opcional-webhook-para-avisar-sem-polling) | notificação `documento.autorizado` |

Tempo estimado: ~10 minutos com Docker no ar.

---

## 1. Subir a stack (Docker, modo sandbox)

Requisitos: [Docker](https://docs.docker.com/get-docker/) e `openssl` (só
para gerar a chave). O passo a passo completo está no **Início rápido** do
[README](../README.md) — aqui o essencial:

```bash
# 1. Gerar a chave mestra (KEK) que cifra certificados/CSC/segredos
openssl rand -base64 32

# 2. Criar o .env a partir do modelo e preencher
cp .env.example .env
#    KEK_MASTER_KEY=<valor gerado acima>     (exatamente 32 bytes em Base64)
#    MODO_SANDBOX=true                       (EmissorMock — sem certificado)
#    ADMIN_EMAIL=admin@fiscal.local          (primeiro operador do painel)
#    ADMIN_PASSWORD=<senha forte>            (seed roda se admin_users vazio)
#    ADMIN_JWT_SECRET=<mín. 32 caracteres>   (assinatura dos JWT do painel)

# 3. Subir Postgres + API + Worker
docker compose -f docker/docker-compose.yml --env-file .env up -d

# 4. Aguardar a API aplicar as migrations e ficar pronta
curl http://localhost:8080/health/ready     # → 200
```

O que cada variável faz (modelo em `.env.example`):

| Variável | Papel | Observação |
|---|---|---|
| `DB_PASSWORD` | Senha do Postgres local | |
| `KEK_MASTER_KEY` | Chave-mestra que cifra certificados, CSC e segredos de webhook | Gerar com `openssl rand -base64 32` |
| `MODO_SANDBOX=true` | Registra o `EmissorMock` no lugar dos adapters SEFAZ | É o que dispensa certificado A1 e contato externo |
| `ADMIN_EMAIL` / `ADMIN_PASSWORD` | Primeiro operador do painel (seed no startup) | Sem eles o painel não tem login |
| `ADMIN_JWT_SECRET` | Assina os JWT do painel admin | **Mín. 32 caracteres**; sem valor, login do painel desabilitado |

> O compose sobe **dois** serviços da aplicação — a **API** (HTTP, porta
> 8080) e o **Worker** (host Hangfire). Quem executa a fila e autoriza a nota
> é o Worker: o modo sandbox precisa estar `true` para ele também (o
> `MODO_SANDBOX` do `.env` é lido pelos dois serviços).
>
> No sandbox o processamento é **interno**: o `EmissorMock` "autoriza" todo
> documento com chave/protocolo determinísticos e XML com `cStat 100`
> (`Autorizado o uso da NF-e (MOCK)`). Nada sai para a SEFAZ, nenhum
> certificado é usado.

Fontes: `.env.example`; `README.md` (Início rápido); `docker/docker-compose.yml`;
`src/Fiscal.Api/Program.cs` (seed do admin em Development — que é o ambiente do
compose; health checks `/health/live` e `/health/ready`).

---

## 2. Criar a empresa (tenant)

O **tenant** é a sua empresa emitente dentro da API: todo documento, chave,
certificado e evento pertence a um tenant e é isolado dos demais.

### 2.1 Pelo painel admin (caminho recomendado)

1. Abra `http://localhost:8080` e entre com `ADMIN_EMAIL` / `ADMIN_PASSWORD`
   do `.env` (se estiver rodando o frontend em dev: `http://localhost:5173`,
   com proxy para a API).
2. Vá em **Tenants → “+ Novo tenant”** e preencha:
   - **CNPJ** (14 dígitos) e **Razão social** — obrigatórios;
   - **UF** (2 letras, ex.: `PR`) — obrigatória;
   - **Código município IBGE** (7 dígitos, ex.: `4106902` = Curitiba) e
     **Nome do município**;
   - **Regime tributário**: `1` Simples Nacional · `2` Simples (excesso de
     sublimite) · `3` Regime Normal (padrão);
   - **Ambiente padrão**: Homologação (para o sandbox, use homologação);
   - Inscrição Estadual e endereço (logradouro/número/bairro/CEP) — usados
     como dados do **emitente** na emissão real;
   - Webhook URL (opcional agora — o passo 8 permite configurar por API).
3. Salve — o painel abre a página do tenant (`/tenants/{id}`). O `id`
   (GUID) na URL **é o `tenantId`** usado nas chamadas admin a seguir.

### 2.2 Pela API admin (alternativa — scriptável)

Login do operador (retorna o JWT usado nas rotas `/v1/admin/*`):

```bash
BASE=http://localhost:8080

curl -X POST "$BASE/v1/admin/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@fiscal.local","senha":"SUA_ADMIN_PASSWORD"}'
# → 200 { "token": "eyJhbGciOi...", "expiraEm": "...", "email": "admin@fiscal.local" }
# JWT válido por 8 horas (padrão Admin:TokenHoras).
```

Criação do tenant (auth com o token acima — `Authorization: Bearer <token>`).
**Atalho de 1 request:** informe `criarApiKey` (`"homologacao"` ou
`"producao"`) e o próprio `POST` já cria a primeira API key — tenant e chave
nascem na **mesma transação**:

```bash
TOKEN=eyJhbGciOi...   # token do login

curl -X POST "$BASE/v1/admin/tenants" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "cnpj": "12345678000199",
    "razaoSocial": "Empresa Teste Ltda",
    "uf": "PR",
    "codigoMunicipioIbge": "4106902",
    "regimeTributario": 3,
    "ambientePadrao": "homologacao",
    "criarApiKey": "homologacao"
  }'
# → 201 Created
# {
#   "tenant": { "id": "…", "razaoSocial": "…", "apiKeysAtivas": 1, … },
#   "apiKey": { "id": "…", "chave": "fk_test_…", "prefixo": "fk_test_…",
#               "ambiente": "homologacao",
#               "aviso": "Esta é a única vez que a chave completa é exibida.
#                         Guarde-a em local seguro." }
# }
# Guarde tenant.id como TENANT_ID e apiKey.chave como CHAVE e siga direto
# para o passo 3.3 (a chave já autentica — dispensa o 3.1/3.2).
```

A chave completa aparece **uma única vez**, nessa resposta (mesma regra do
passo 3). **Alternativa (2 chamadas):** omita `criarApiKey` e o `POST`
devolve só o `tenantId` — aí gere a chave no passo 3.2:

```bash
curl -X POST "$BASE/v1/admin/tenants" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ … mesmo corpo, sem o campo "criarApiKey" … }'
# → 201 Created (corpo: o GUID do tenant) — guarde como TENANT_ID
```

Campos aceitos pelo `POST /v1/admin/tenants` (todos em camelCase):
`cnpj`, `razaoSocial`, `uf`, `codigoMunicipioIbge`, `regimeTributario`
(1–3, default 3), `ambientePadrao` (`"producao"`/`"homologacao"`, default
homologação), `inscricaoEstadual`, `logradouro`, `numero`, `complemento`,
`bairro`, `cep`, `nomeMunicipio`, `webhookUrl`, `webhookSecret`, `ativo`,
`criarApiKey` (opcional — `"producao"`/`"homologacao"`; cria a 1ª API key
junto com o tenant e o response traz o bloco `apiKey`).

Erros comuns: `422` (CNPJ sem 14 dígitos, UF ≠ 2 letras, regime fora de 1–3,
`ambientePadrao` inválido, `criarApiKey` fora de `"producao"`/`"homologacao"`
— nesse caso nada é criado), `409` (já existe tenant com o mesmo CNPJ).

### 2.3 Via SQL (alternativa para laboratório local)

O README traz um insert direto no Postgres do compose — cria o **tenant**,
sem chave (a primeira chave sempre nasce pelo painel/admin API, ver passo 3):

```bash
docker exec -it fiscalapi-postgres-1 psql -U fiscal -d fiscal -c "
INSERT INTO tenants (id, cnpj, razao_social, uf, codigo_municipio_ibge,
                     regime_tributario, ambiente_padrao, ativo)
VALUES (gen_random_uuid(), '12345678000199', 'Empresa Teste', 'PR',
        '4106902', 3, 2, true);"
```

`ambiente_padrao` é numérico no banco: `1` produção, `2` homologação.

Fontes: `src/Fiscal.Api/Controllers/Admin/AdminAuthController.cs` (`POST
v1/admin/auth/login`); `src/Fiscal.Api/Controllers/Admin/AdminTenantsController.cs`
(`POST /v1/admin/tenants` + regras de `Cnpj`/`Uf`/`RegimeTributario`/`AmbientePadrao`);
`src/Fiscal.Api/Authentication/AdminTokenService.cs` (JWT 8 h);
`frontend/src/pages/Tenants.tsx` (formulário do painel); `README.md` (insert SQL).

---

## 3. Gerar a API key e autenticar

Cada API key está **atrelada a um ambiente**: o prefixo `fk_test_` indica
homologação e `fk_live_` produção. Uma chave `fk_test_` não consegue emitir
com `"ambiente": "producao"` (a API devolve `403`). Para o sandbox, gere uma
chave de **homologação**.

> Usou o atalho `criarApiKey` no passo 2.2? Já tem `TENANT_ID` e `CHAVE` em
> mãos — siga direto para o [3.3](#33-header-de-autenticação-exato).

### 3.1 Pelo painel

Na página do tenant (`/tenants/{id}`), aba **API Keys → “Gerar chave”**,
informe a descrição (ex.: “ERP Matriz”) e o ambiente **Homologação**. A chave
completa (`fk_test_…`) aparece **uma única vez** — copie e guarde (o painel só
mostra o prefixo depois; a revogação é imediata e as integrações passam a
receber `401`).

### 3.2 Pela API admin (mesma coisa, scriptável)

```bash
TENANT_ID=<guid do passo 2>

curl -X POST "$BASE/v1/admin/tenants/$TENANT_ID/api-keys" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"ambiente": "homologacao", "descricao": "ERP Matriz"}'
# → 201 Created
# {
#   "id": "b4c1d2e3-...",
#   "chave": "fk_test_AbCdEfGh...",     ← única vez em claro
#   "prefixo": "fk_test_AbCd",
#   "descricao": "ERP Matriz",
#   "ambiente": "homologacao",
#   "criadoEm": "2026-09-09T12:00:00.123+00:00",
#   "aviso": "Esta é a única vez que a chave completa é exibida. Guarde-a em local seguro."
# }
```

> **Rotação/mais chaves:** com uma chave em mãos, o próprio tenant cria novas
> chaves em `POST /v1/api-keys` (mesmo corpo e resposta). O caminho
> **admin → tenant → chave** é o único bootstrap: não existe seed de chave via
> SQL/código (elas são hashadas com PBKDF2 antes de ir ao banco).

### 3.3 Header de autenticação (exato)

Toda rota de negócio (`/v1/*`, exceto health checks) exige:

```
Authorization: ApiKey fk_test_AbCdEfGh...
```

- Prefixo do esquema `ApiKey ` (case-insensitive) + a chave, **sem espaços extras**;
- chaves têm no mínimo 16 caracteres (prefixo `fk_…` + 24 bytes aleatórios em Base64);
- opcional: `X-Fiscal-Ambiente: homologacao` amarra a requisição ao ambiente —
  se divergir do ambiente da chave, a API responde `401` (proteção extra).

Exporte a chave numa variável e confirme que autentica:

```bash
CHAVE=fk_test_AbCdEfGh...
curl -H "Authorization: ApiKey $CHAVE" "$BASE/v1/api-keys"
# → 200 [ ... ] (lista as chaves do tenant; só metadados)
```

Resumo dos códigos: `401` sem header/chave inválida/revogada ou
`X-Fiscal-Ambiente` divergente; `403` chave válida mas `ambiente` do payload
divergente.

Fontes: `src/Fiscal.Api/Controllers/Admin/AdminApiKeysController.cs` e
`src/Fiscal.Api/Controllers/ApiKeysController.cs` (criação: corpo
`{descricao, ambiente}`, resposta com `chave` única vez);
`src/Fiscal.Api/Authentication/ApiKeyAuthenticationHandler.cs` (header exato,
prefixos `fk_test_`/`fk_live_`, `X-Fiscal-Ambiente`);
`frontend/src/pages/TenantDetail.tsx` (aba API Keys do painel);
`src/Fiscal.Api/Authentication/AdminTokenService.cs`.

---

## 4. (Opcional) Certificado A1

**No sandbox o certificado é opcional** — o `EmissorMock` não assina nada e o
job de processamento só exige certificado fora do sandbox
(`if (cert is null && !_sandbox) → ERRO_INTERNO`). Você pode pular este passo
e ir direto à emissão.

Quando for testar o fluxo completo (ou preparar produção), o upload é
`multipart/form-data`:

```bash
curl -X POST "$BASE/v1/certificados" \
  -H "Authorization: ApiKey $CHAVE" \
  -F "pfx=@certificado.pfx" \
  -F "senha=senhaDoCertificado"
# → 201 { "id": "...", "thumbprint": "3A7B...", "validoAte": "2027-05-20", "ativo": true }
```

- `.pfx` até 10 MB; senha obrigatória. Arquivo e senha são armazenados
  **cifrados** (envelope AES-GCM com a `KEK_MASTER_KEY`) — a API nunca os devolve.
- `GET /v1/certificados` lista os certificados do tenant (metadados).
- **Ciclo de vida**: só há **1 certificado ativo por tenant** — subir um novo
  `.pfx` desativa o anterior automaticamente (rotação; auditoria
  `CERTIFICADO_SUBSTITUIDO`). `DELETE /v1/certificados/{id}` desativa sem
  apagar (soft-delete; idempotente) e `POST /v1/certificados/{id}/ativar`
  reativa um anterior desativando os demais. Mesmas rotas no admin:
  `GET/POST /v1/admin/tenants/{tenantId}/certificados` +
  `DELETE|POST …/certificados/{id}` (`…/{id}/ativar`).
- No painel admin: aba Certificados do tenant → upload em nome do tenant
  (`POST /v1/admin/tenants/{tenantId}/certificados`, mesmo multipart).
- Em homologação **real** (fora do sandbox) a SEFAZ aceita o mesmo A1 de
  produção — não existe certificado "de teste".

Fontes: `src/Fiscal.Api/Controllers/CertificadosController.cs` e
`src/Fiscal.Api/Controllers/Admin/AdminCertificadosController.cs` (multipart
`pfx` + `senha`, limite 10 MB, `422` p/ pfx inválido, rotação/`DELETE`/`ativar`);
`src/Fiscal.Worker/Jobs/ProcessarDocumentoJob.cs` (cert opcional no sandbox).

---

## 5. Emitir a primeira nota

A emissão é **assíncrona**: o `POST` devolve `202` com um `id` e
`status: "PENDENTE"`; o Worker processa em background e você acompanha por
polling (passo 6) ou webhook (passo 8). O header `Idempotency-Key` é
obrigatório — repetir o mesmo `POST` com a mesma chave **não cria nota nova**:
devolve `200` com o estado atual (seguro para retry de rede).

### 5.1 NF-e (modelo 55)

```bash
curl -X POST "$BASE/v1/documentos-fiscais/nfe" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: pedido-2026-0001" \
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
      "impostos": [{ "cst": "00", "baseCalculo": 100, "aliquota": 18, "valor": 18 }]
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
  "criadoEm": "2026-09-09T12:00:00.123+00:00",
  "links": { "consulta": "/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c" }
}
```

O `id` acima é o **identificador do documento** — persista-o junto ao pedido
do seu ERP; é a chave da consulta, do PDF e dos eventos.

**Payload mínimo** que a validação aceita (demais campos são opcionais no
`EmissaoRequest`): `ambiente` (`"producao"`/`"homologacao"`), `serie` (1–999),
`itens[]` (ao menos 1) e `totais`. Num item, são obrigatórios `codigo`,
`descricao`, `quantidade` (> 0), `valorUnitario` e `valorTotal` (≥ 0); o
destinatário (`cnpjCpf`, `nome`) e `pagamento[]` são opcionais no contrato,
mas fazem parte de uma nota fiscal de verdade.

**Aritmética conferida pela API (tolerância R$ 0,01)** — sem isso, `422` com
o campo exato na extensão `campo`:

1. `quantidade × valorUnitario` = `valorTotal` do item;
2. soma dos `valorTotal` dos itens = `totais.valorNota`;
3. `baseCalculo × aliquota / 100` = `valor` do imposto (quando os 3 vêm);
4. CST de isenção (`40`, `41`, `50`, `60`) não pode ter `valor` > 0.

A API **não calcula tributos**: os valores chegam prontos do seu ERP. Ela
também não valida NCM/CFOP/CST contra tabelas oficiais (rejeições assim
apareceriam da SEFAZ no status, não no `POST`).

### 5.2 NFC-e (modelo 65)

Mesmo corpo e mesmo fluxo, só muda a rota:

```bash
curl -X POST "$BASE/v1/documentos-fiscais/nfce" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: venda-balcao-0001" \
  -H "Content-Type: application/json" \
  -d '{
    "ambiente": "homologacao",
    "serie": 1,
    "destinatario": { "cnpjCpf": "11122233000144", "nome": "Cliente Teste" },
    "itens": [{
      "codigo": "SKU1", "descricao": "Produto de teste", "ncm": "12345678",
      "cfop": "5102", "quantidade": 2, "valorUnitario": 50, "valorTotal": 100,
      "impostos": [{ "cst": "00", "baseCalculo": 100, "aliquota": 18, "valor": 18 }]
    }],
    "totais": { "valorProdutos": 100, "valorNota": 100 },
    "pagamento": [{ "forma": "01", "valor": 100 }]
  }'
# → 202 Accepted (mesma forma: { id, status: "PENDENTE", links: {...} })
```

> No sandbox, NFC-e funciona sem o **CSC** (código de segurança do
> consumidor). Na emissão **real** (fora do sandbox) a NFC-e exige CSC/IdCSC
> do tenant, configurado em `PUT /v1/tenants/perfil` (o CSC fica cifrado com
> a KEK).

### 5.3 NFS-e padrão Nacional (DPS)

A NFS-e tem contrato próprio (`NfseDpsRequest`), mais enxuto — serviço e
tomador:

```bash
curl -X POST "$BASE/v1/documentos-fiscais/nfse/dps" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Idempotency-Key: nfse-2026-0001" \
  -H "Content-Type: application/json" \
  -d '{
    "ambiente": "homologacao",
    "serie": 1,
    "dataCompetencia": "2026-09-09",
    "tomador": {
      "cnpjCpf": "11122233000144",
      "nome": "Cliente Serviço Ltda"
    },
    "servico": {
      "codigoTributarioNacional": "010701",
      "descricaoServico": "Desenvolvimento de software"
    },
    "valores": {
      "valorServicos": 1000,
      "tributacaoIssqn": 1,
      "retencaoIssqn": 1,
      "aliquotaIssqn": 5
    }
  }'
# → 202 Accepted
```

Regras do corpo (`NfseDpsRequest`): `tomador` é **obrigatório na prática**
(`cnpjCpf` com 11 ou 14 dígitos; validador rejeita payload sem `tomador`);
`servico.codigoTributarioNacional` (cTribNac) e `servico.descricaoServico`
são obrigatórios; `valores.tributacaoIssqn` (1–4) e `retencaoIssqn` (1–3)
obrigatórios, `valorServicos` ≥ 0; `dataCompetencia` no formato `yyyy-MM-dd`
(opcional — default hoje UTC); `codigoNbs` com 9 dígitos quando informado. O
bloco `ibscbs` (reforma IBS/CBS) é opcional, mas se informado exige
`codigoIndicadorOperacao` (6 díg.) e `gibbsCbs.cst` (3 díg.) +
`gibbsCbs.cClassTrib` (6 díg.). Autorização síncrona: no sandbox o mock
autoriza na hora.

> A rota `POST /v1/documentos-fiscais/nfse` (corpo `EmissaoRequest`) existe
> como **rota legada** e só faz sentido em sandbox; a transmissão real usa
> `/nfse/dps`. Para o guia sandbox, prefira `/nfse/dps`.

### 5.4 Códigos de resposta do POST

| HTTP | Significado |
|---|---|
| `202 Accepted` | Aceito — `{id, status: "PENDENTE", ambiente, criadoEm, links.consulta}` |
| `200 OK` | **Replay de idempotência** — mesma `Idempotency-Key` já usada: estado atual, nada é criado |
| `400` | `Idempotency-Key` ausente (ou > 100 chars) / payload fora do schema |
| `403` | `ambiente` do payload ≠ ambiente da chave |
| `422` | `ambiente` inválido ou inconsistência aritmética (extensão `campo`) |

Fontes: `src/Fiscal.Api/Controllers/DocumentosFiscaisController.cs` (rotas
`POST /v1/documentos-fiscais/nfe|nfce|nfse/dps`, header `Idempotency-Key`,
validações `422`, resposta `202`);
`src/Fiscal.Core/Contracts/EmissaoDtos.cs` e
`src/Fiscal.Core/Contracts/NfseDpsDtos.cs` (campos acima);
`src/Fiscal.Api/Validators/EmissaoRequestValidator.cs` (schema 400);
`src/Fiscal.Core/Services/ValidadorConsistenciaFiscal.cs` +
`ValidadorNfseDps.cs` (aritmética e regras `422`).

---

## 6. Consultar até a autorização

O documento anda sozinho: `PENDENTE → PROCESSANDO → AUTORIZADA` (no sandbox,
em geral em segundos — o mock não depende de SEFAZ). Consulte com o `id` do
`202`:

```bash
curl -H "Authorization: ApiKey $CHAVE" \
  "$BASE/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c"
```

Resposta `200 OK` quando autorizada:

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
  "xmlAssinado": "<nfeProc ...>...</nfeProc>",
  "xmlRetornoSefaz": "<retEnviNFe ...><cStat>100</cStat>...</retEnviNFe>",
  "motivoStatus": null,
  "criadoEm": "2026-09-09T12:00:00.123+00:00",
  "atualizadoEm": "2026-09-09T12:00:02.456+00:00"
}
```

O `numero` (42 no exemplo) é **reservado pela API** — sequencial por
(tenant, modelo, série, ambiente). Você informa só a série.

**Polling recomendado:** aguarde ~2–5 s após o `202`, consulte com backoff
(2 s → 5 s → 10 s … teto ~60 s) e **pare quando o `status` for terminal**:
`AUTORIZADA`, `REJEITADA`, `DENEGADA`, `CANCELADA` ou `ERRO_INTERNO`.
`CONTINGENCIA` (erro de transmissão transitório) é não-terminal: a API
reprocessa sozinha com backoff 30 s → 1 m → 2 m → 5 m → 10 m — continue
consultando. No sandbox isso praticamente não acontece.

Estados não terminais: `PENDENTE`, `PROCESSANDO`, `CONTINGENCIA`,
`CANCELAMENTO_PENDENTE`. Em `REJEITADA`/`DENEGADA`/`ERRO_INTERNO`, leia
`motivoStatus` (e o detalhe técnico em `xmlRetornoSefaz`), corrija e **reemita
com uma nova `Idempotency-Key`** — o número da tentativa anterior já foi
consumido.

Fontes: `src/Fiscal.Api/Controllers/DocumentosFiscaisController.cs`
(`GET /v1/documentos-fiscais/{id}` e resposta `EmissaoResponse`);
`src/Fiscal.Core/Contracts/EmissaoDtos.cs` (`EmissaoResponse`);
`src/Fiscal.Worker/Jobs/ProcessarDocumentoJob.cs` (PENDENTE → PROCESSANDO →
AUTORIZADA/REJEITADA/…, backoff, certificado opcional no sandbox);
`src/Fiscal.Adapters.Unimake/EmissorMock.cs` (autorização mock, XML `cStat 100`).

---

## 7. Baixar o PDF (DANFE/DANFCe/DANFSe)

O DANFE só existe para documentos **`AUTORIZADA` ou `CANCELADA`** (outro
status → `409`):

```bash
# PDF binário (application/pdf) — salve em arquivo
curl -H "Authorization: ApiKey $CHAVE" \
  "$BASE/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c/pdf" \
  -o danfe.pdf
```

Para embutir no seu sistema sem lidar com binário, use `?formato=base64`:

```bash
curl -H "Authorization: ApiKey $CHAVE" \
  "$BASE/v1/documentos-fiscais/e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c/pdf?formato=base64"
# → 200 { "pdfBase64": "JVBERi0xLjQK...", "contentType": "application/pdf" }
```

O tipo de PDF é escolhido pelo **modelo do documento**: modelo 55 → **DANFE**,
modelo 65 → **DANFCe**, NFS-e Nacional → **DANFSe**. O leiaute é o
**simplificado** (gerado por QuestPDF, com código de barras da chave e QR no
DANFCe).

Fontes: `src/Fiscal.Api/Controllers/DocumentosFiscaisController.cs`
(`GET /v1/documentos-fiscais/{id}/pdf`, query `formato=base64`, restrição de
status); `src/Fiscal.Pdf/` (gerador QuestPDF); `src/Fiscal.Api/Program.cs`
(registro `IGeradorPdf`).

---

## 8. (Opcional) Webhook para avisar sem polling

O polling do passo 6 é o mecanismo oficial; o webhook é o atalho para não
ficar consultando. A configuração é **self-service** (o painel também tem,
mas o tenant configura sozinho):

```bash
curl -X PUT "$BASE/v1/tenants/webhooks" \
  -H "Authorization: ApiKey $CHAVE" \
  -H "Content-Type: application/json" \
  -d '{
    "webhookUrl": "https://integrador.exemplo.com/fiscal/hook",
    "webhookSecret": "segredo-com-ao-menos-16-caracteres"
  }'
# → 200 { "atualizado": true, "webhookUrl": "https://...", "webhookSecretCadastrado": true }
```

Regras: URL absoluta `http(s)` (senão `422`); segredo com **16–200**
caracteres (`422` fora disso); campos nulos não são alterados; corpo vazio →
`400`. O segredo é guardado **cifrado** (envelope KEK) e nunca é devolvido —
`GET /v1/tenants/webhooks` responde `{webhookUrl, webhookSecretCadastrado}`.

Quando o documento transiciona, a API grava a entrega na **mesma transação**
da mudança de status (outbox) e o Worker faz o `POST` assinado para a sua URL:

```
POST https://integrador.exemplo.com/fiscal/hook
Content-Type: application/json
X-Fiscal-Timestamp: 1700000000
X-Fiscal-Signature: sha256=<hex>
```

```json
{
  "tipo": "documento.autorizado",
  "timestamp": 1700000000,
  "documento": {
    "id": "e7a1c9f2-8b3d-4f2a-9c1e-5d6b7a8f9e0c",
    "tipo": "NFE", "status": "AUTORIZADA", "ambiente": "homologacao",
    "serie": 1, "numero": 42, "chaveAcesso": "4126...", "protocoloAutorizacao": "1000...",
    "motivoStatus": null, "criadoEm": "...", "atualizadoEm": "..."
  }
}
```

**Verificação no seu endpoint** (proteção contra replay e fraude):

1. Recalcule `esperado = HMAC-SHA256(webhookSecret, "{X-Fiscal-Timestamp}.{corpoCru}")`
   e compare com o valor de `X-Fiscal-Signature` (formato `sha256=<hex>`,
   comparação em tempo constante);
2. rejeite timestamps fora de uma janela de **5 minutos**;
3. responda `2xx` para confirmar a entrega. Timeout (10 s), erro de rede ou
   resposta ≠ 2xx contam como tentativa: retry com backoff
   `1m → 5m → 15m → 1h → 6h → 6h → 24h`; após **8 tentativas** a entrega vai
   para `FALHA` (terminal, na tabela `outbox_webhooks`).

Eventos disponíveis: `documento.autorizado`, `documento.rejeitado`,
`documento.denegado`, `documento.cancelado`, `documento.carta_correcao`,
`nota.recebida`, `manifestacao.processada`, `certificado.vencendo`.

> A configuração do webhook também aceita **só** a URL ou **só** o segredo
> (atualização parcial). Para limpar a URL, envie `"webhookUrl": ""`.

Fontes: `src/Fiscal.Api/Controllers/TenantsController.cs` (`GET/PUT
/v1/tenants/webhooks` — regras de URL/segredo);
`src/Fiscal.Worker/Webhooks/DespachanteWebhookHttp.cs` (headers
`X-Fiscal-Timestamp`/`X-Fiscal-Signature`, timeout 10 s);
`src/Fiscal.Core/Services/Webhooks.cs` (HMAC sobre `"{timestamp}.{payload}"`,
payloads e eventos); `src/Fiscal.Worker/Jobs/ProcessarWebhookJob.cs` (retry
1m→…→24h, máx. 8 tentativas → FALHA); `src/Fiscal.Worker/Jobs/ProcessarDocumentoJob.cs`
(outbox na mesma transação do status).

---

## Checklist final

```bash
# 1. Stack no ar (sandbox)
curl http://localhost:8080/health/ready                        # 200

# 2. Tenant criado
curl -H "Authorization: Bearer $TOKEN" "$BASE/v1/admin/tenants"  # lista com o seu

# 3. Chave de homologação em mãos
echo "$CHAVE"                                                     # fk_test_...

# 4. Autentica
curl -H "Authorization: ApiKey $CHAVE" "$BASE/v1/api-keys"        # 200

# 5. Nota emitida (id do 202)
# 6. AUTORIZADA no polling
curl -H "Authorization: ApiKey $CHAVE" "$BASE/v1/documentos-fiscais/$DOC_ID"
# 7. PDF baixado
curl -H "Authorization: ApiKey $CHAVE" "$BASE/v1/documentos-fiscais/$DOC_ID/pdf" -o danfe.pdf
# 8. (Opcional) webhook configurado
curl -H "Authorization: ApiKey $CHAVE" "$BASE/v1/tenants/webhooks"  # webhookSecretCadastrado: true
```

Depois do primeiro ciclo em sandbox, siga a seção 9: a bateria de
homologação real com certificado A1. Para a superfície completa da API —
eventos (cancelamento, CC-e, inutilização), `impostosV2`, códigos de erro e
boas práticas — veja [docs/integracao-api.md](integracao-api.md).

---

## 9. Homologação real (fora do sandbox) — primeiro contato com a SEFAZ

O ciclo do "Checklist final" roda inteiro no `EmissorMock`. Esta bateria usa
os mesmos endpoints com os **emissores Unimake de verdade** — validada contra
a SEFAZ-PR com um A1 real (status-serviço `cStat 107` e emissões
transmitidas, 2026-09-17).

### 9.1 Pré-requisitos

- **Certificado A1** (`.pfx` + senha) do emitente. A3/HSM não é suportado.
  O mesmo A1 de produção vale na homologação — não existe certificado
  "de teste".
- **IE real do emitente**: a SEFAZ valida a Inscrição Estadual **mesmo em
  homologação** (na PR, IE de teste retorna `Rejeição 209 — IE do emitente
  invalida`). Não adianta inventar.
- **NFC-e**: além do certificado, credenciamento na SEFAZ da UF + **CSC/IdCSC**
  (`PUT /v1/tenants/perfil`, armazenados cifrados).

### 9.2 Subir a stack com emissores reais

```bash
# sobrepõe o .env só nesta subida — vale para API E Worker (quem assina e
# transmite é o Worker)
MODO_SANDBOX=false docker compose -f docker/docker-compose.yml --env-file .env up -d
curl http://localhost:8080/health/ready                       # 200
docker exec docker-api-1   printenv Fiscal__ModoSandbox       # False
docker exec docker-worker-1 printenv Fiscal__ModoSandbox      # False
```

### 9.3 Roteiro (com o tenant e a API key `fk_test_` do passo 3)

1. **Upload do A1** (`POST /v1/certificados`, passo 4). O upload mantém a
   invariante de **1 certificado ativo por tenant**: subir um novo desativa
   o anterior (rotação); `DELETE /v1/certificados/{id}` revoga (soft-delete)
   e `POST /v1/certificados/{id}/ativar` volta atrás na rotação.
2. **Perfil do emitente com IE real** + endereço (`PUT /v1/tenants/perfil`).
3. **Smoke de conectividade**:
   `GET /v1/status-servico?modelo=55&ambiente=homologacao` → `cStat 107`
   ("Servico em Operacao"). Só esse teste já exercita a cadeia inteira:
   KEK → decifra o PFX do banco → assinatura/SSL (Unimake) → webservice da
   SEFAZ → parse da resposta.
4. **Emissão** com `"ambiente": "homologacao"` + polling (passos 5–6).
   O lote vai **síncrono** (`indSinc=1`) — lote unitário assíncrono é
   rejeitado com `452` na PR.
5. **Eventos** contra a SEFAZ real: cancelamento, CC-e, inutilização;
   consulta de protocolo e distribuição DF-e.

### 9.4 Diagnosticando rejeições

`GET /v1/documentos-fiscais/{id}` traz `motivoStatus` (ex.: "Rejeição 209:
IE do emitente invalida") e `xmlRetornoSefaz` (resposta original da SEFAZ,
com o `cStat`/`xMotivo` oficiais). Rejeição **não é falha da esteira**: o
documento chega a `REJEITADA` com o XML assinado preservado para auditoria —
corrija o payload e emita de novo (nova nota, novo número).

Rejeições vistas na bateria de 2026-09-17 (SEFAZ-PR):

| Rejeição | Causa | Estado |
|---|---|---|
| `452` — resposta assíncrona para lote com 1 NF-e | a PR exige `indSinc=1` em lote unitário | **corrigida** (`MapperEnviNFe` sempre síncrono) |
| `209` — IE do emitente inválida | IE de teste não existe no cadastro da PR | usar a IE real do emitente |

---

## Anexo — onde cada seção está no código

| Seção do guia | Rotas/campos | Fonte no código |
|---|---|---|
| 1. Stack / variáveis | `KEK_MASTER_KEY`, `MODO_SANDBOX`, `ADMIN_*`, health | `.env.example`, `README.md`, `docker/docker-compose.yml`, `src/Fiscal.Api/Program.cs` |
| 2. Login admin | `POST /v1/admin/auth/login` (`{email, senha}` → `{token, expiraEm, email}`) | `src/Fiscal.Api/Controllers/Admin/AdminAuthController.cs`, `AdminTokenService.cs` (JWT 8 h) |
| 2. Criar tenant | `POST /v1/admin/tenants` (`AdminTenantRequest`; CNPJ 14, UF 2, regime 1–3, ambiente; `criarApiKey` opcional cria a 1ª key) | `src/Fiscal.Api/Controllers/Admin/AdminTenantsController.cs`, `frontend/src/pages/Tenants.tsx`, `README.md` (SQL) |
| 3. API key | `POST /v1/admin/tenants/{id}/api-keys` e `POST /v1/api-keys` (`{descricao, ambiente}`) | `AdminApiKeysController.cs`, `ApiKeysController.cs`, `frontend/src/pages/TenantDetail.tsx` |
| 3. Header auth | `Authorization: ApiKey <chave>`, `X-Fiscal-Ambiente` | `src/Fiscal.Api/Authentication/ApiKeyAuthenticationHandler.cs` |
| 4. Certificado | `POST/GET /v1/certificados`, `DELETE/{id}`, `POST/{id}/ativar` (multipart: `pfx`, `senha`; 10 MB; rotação no upload) | `CertificadosController.cs`, `AdminCertificadosController.cs`; opcional no sandbox: `ProcessarDocumentoJob.cs` |
| 5. Emissão | `POST /v1/documentos-fiscais/nfe\|nfce` (`EmissaoRequest`) e `nfse/dps` (`NfseDpsRequest`) + `Idempotency-Key` | `DocumentosFiscaisController.cs`, `src/Fiscal.Core/Contracts/EmissaoDtos.cs`, `NfseDpsDtos.cs` |
| 5. Validações 400/422 | aritmética e regras declarativas | `Validators/EmissaoRequestValidator.cs`, `src/Fiscal.Core/Services/ValidadorConsistenciaFiscal.cs`, `ValidadorImpostosV2.cs`, `ValidadorNfseDps.cs` |
| 6. Consulta | `GET /v1/documentos-fiscais/{id}` (`EmissaoResponse`); máquina de estados | `DocumentosFiscaisController.cs`, `EmissaoDtos.cs`, `ProcessarDocumentoJob.cs`, `EmissorMock.cs` |
| 7. PDF | `GET /v1/documentos-fiscais/{id}/pdf` (+`?formato=base64`); só AUTORIZADA/CANCELADA | `DocumentosFiscaisController.cs`, `src/Fiscal.Pdf/` |
| 8. Webhook | `GET/PUT /v1/tenants/webhooks`; headers `X-Fiscal-Timestamp`/`X-Fiscal-Signature`; eventos | `TenantsController.cs`, `src/Fiscal.Worker/Webhooks/DespachanteWebhookHttp.cs`, `src/Fiscal.Core/Services/Webhooks.cs`, `src/Fiscal.Worker/Jobs/ProcessarWebhookJob.cs` |
