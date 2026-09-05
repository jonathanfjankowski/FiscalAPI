# Contrato REST, Autenticação, Redis e Docker

## 1. API Key — sim, é a escolha certa aqui

Pra uma API **machine-to-machine** (ERP → API, sem usuário logando via browser), API Key é mais simples e mais adequado que OAuth/JWT completo. Recomendações de implementação:

### 1.1 Modelo
```sql
CREATE TABLE api_keys (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID NOT NULL REFERENCES tenants(id),
    prefixo       VARCHAR(8) NOT NULL,        -- ex: "fk_live_" ou "fk_test_" + 4 chars, exibido pro usuário
    key_hash      VARCHAR(64) NOT NULL,       -- SHA-256 da chave completa — nunca guardar em texto plano
    descricao     TEXT,                       -- "chave do ERP produção", etc.
    ambiente      SMALLINT NOT NULL,          -- amarra a key ao ambiente (produção/homologação) — ver seção 1.4
    ativa          BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em       TIMESTAMPTZ NOT NULL DEFAULT now(),
    revogado_em     TIMESTAMPTZ,
    UNIQUE (key_hash)
);
CREATE INDEX idx_api_keys_tenant ON api_keys (tenant_id) WHERE ativa = TRUE;
```

### 1.2 Formato da chave
- Ex.: `fk_live_9f8a2b7c1e4d...` — prefixo identifica ambiente (`fk_live_` / `fk_test_`) só de olhar, o que evita o clássico erro de usar chave de produção em homologação sem querer.
- Chave é exibida **uma única vez** na criação (padrão do mercado — Stripe, GitHub fazem assim); depois disso só o hash existe no banco.

### 1.3 Header e resolução do tenant
```
Authorization: ApiKey fk_live_9f8a2b7c1e4d...
```
- Middleware calcula SHA-256 da chave recebida, busca por `key_hash`, resolve o `tenant_id` — não precisa nem receber o CNPJ no payload, o tenant já vem resolvido da própria autenticação (mais seguro: elimina a classe de bug "mandei o CNPJ errado no body e acessei nota de outro tenant").
- Permitir **múltiplas chaves ativas por tenant** (facilita rotação sem downtime: cria a nova, atualiza o ERP, revoga a antiga depois).

### 1.4 Ambiente atrelado à chave (reforça a separação homolog/produção)
Amarrar `ambiente` à própria API Key (não só ao payload) é uma camada extra de segurança: uma chave de homologação **fisicamente não consegue** emitir em produção mesmo que o payload diga `"ambiente": "producao"` por engano — a API rejeita com `403` porque a chave não autoriza aquele ambiente. Isso é mais seguro que confiar só no campo do payload (que discutimos antes).

### 1.5 Rate limiting por chave
Usar o **`Microsoft.AspNetCore.RateLimiting`** (built-in no .NET 8) com **Redis** como backend de contadores (necessário se a API rodar em mais de uma instância — contador local em memória não funciona direito com múltiplas réplicas). Ver seção 2.

---

## 2. Redis — onde ele entra (sim, é útil aqui)

Você perguntou se julgo necessário: **sim, mas para um escopo bem específico** — não para fila (isso ficou com Hangfire/Postgres) nem para persistência principal.

| Uso | Por quê |
|---|---|
| **Rate limiting distribuído** | Contadores de requisições por API Key precisam ser compartilhados entre instâncias da API — Redis com TTL resolve isso de forma barata. |
| **Cache do status do serviço SEFAZ** | A consulta "SEFAZ está no ar?" (seção de contingência) não precisa ser feita a cada emissão — cachear por UF/serviço com TTL curto (ex.: 30-60s) evita sobrecarregar esse endpoint e acelera a decisão do worker. |
| **Cache de PDF gerado** | Reimpressão (ex.: ERP pedindo o mesmo DANFE várias vezes em sequência, PDV reimprimindo cupom) — cachear o base64 por `documento_id` com TTL de alguns minutos evita regerar o PDF repetidamente. |
| **Cache de configuração municipal de NFS-e** | Dados de webservice/layout por município (que mudam raramente) podem ser cacheados pra não bater toda hora numa fonte de configuração mais lenta. |

**O que Redis não faz aqui:** não é fonte de verdade de nada. Se o Redis cair, o sistema continua funcionando (só perde os ganhos de performance/cache) — isso é importante manter como princípio de design, pra Redis nunca virar um ponto único de falha crítico.

---

## 3. Docker / docker-compose

```yaml
version: "3.9"
services:
  api:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ConnectionStrings__Postgres=Host=postgres;Database=fiscal;Username=fiscal;Password=${DB_PASSWORD}
      - Redis__ConnectionString=redis:6379
      - Certificados__ChaveMestraKEK=${KEK_MASTER_KEY}
      - Fiscal__ModoSandbox=${MODO_SANDBOX:-false}   # ativa o emissor mock (seção 5.4 do doc anterior)
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_started

  worker:
    build: .
    command: ["dotnet", "Fiscal.Worker.dll"]   # processo separado rodando o servidor Hangfire
    environment:
      - ConnectionStrings__Postgres=Host=postgres;Database=fiscal;Username=fiscal;Password=${DB_PASSWORD}
      - Redis__ConnectionString=redis:6379
      - Certificados__ChaveMestraKEK=${KEK_MASTER_KEY}
    depends_on:
      postgres:
        condition: service_healthy

  postgres:
    image: postgres:16-alpine
    environment:
      - POSTGRES_DB=fiscal
      - POSTGRES_USER=fiscal
      - POSTGRES_PASSWORD=${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U fiscal"]
      interval: 5s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    command: ["redis-server", "--maxmemory", "256mb", "--maxmemory-policy", "allkeys-lru"]

volumes:
  pgdata:
```

Pontos importantes:
- **`api` e `worker` como processos/containers separados** desde já, mesmo ambos lendo do mesmo código — permite escalar horizontalmente só o worker em picos de emissão sem escalar a API toda, e vice-versa.
- **`ModoSandbox`** como variável de ambiente facilita MUITO a vida de quem for contribuir com o projeto open source — sobe tudo com `docker-compose up` e já consegue testar emissão sem certificado real.
- **`redis` com `maxmemory` + `allkeys-lru`** — reforça que é cache descartável, não dado crítico (se precisar liberar memória, Redis descarta as chaves menos usadas sem quebrar nada).
- KEK mestra (chave de criptografia dos certificados) via variável de ambiente/secret — **nunca** commitada, documentar isso bem no `.env.example`.

---

## 4. Contrato REST

### 4.1 Convenções gerais
- Base: `/v1`
- Auth: `Authorization: ApiKey <key>` em todas as rotas.
- `Idempotency-Key` obrigatório em todo `POST` que cria documento/evento.
- Erros seguem `application/problem+json` (RFC 7807) — padronizado, já vem com suporte nativo no ASP.NET Core.

### 4.2 Emitir NF-e / NFC-e
```
POST /v1/documentos-fiscais/nfe
POST /v1/documentos-fiscais/nfce
Headers: Authorization: ApiKey ..., Idempotency-Key: pedido-8231

{
  "ambiente": "homologacao",
  "serie": 1,
  "destinatario": { "cnpjCpf": "...", "nome": "...", "endereco": { ... } },
  "itens": [
    {
      "codigo": "SKU123",
      "descricao": "Produto X",
      "ncm": "12345678",
      "cfop": "5102",
      "quantidade": 2,
      "valorUnitario": 49.90,
      "impostos": {
        "icms": { "cst": "060", "baseCalculo": 0, "aliquota": 0, "valor": 0 },
        "pis":  { "cst": "01", "baseCalculo": 99.80, "aliquota": 1.65, "valor": 1.65 },
        "cofins": { "cst": "01", "baseCalculo": 99.80, "aliquota": 7.60, "valor": 7.59 }
      }
    }
  ],
  "totais": { "valorProdutos": 99.80, "valorNota": 99.80 },
  "pagamento": [ { "forma": "01", "valor": 99.80 } ]
}
```

**Resposta imediata — `202 Accepted`** (fluxo assíncrono):
```json
{
  "id": "b3f1...",
  "status": "PENDENTE",
  "ambiente": "homologacao",
  "criadoEm": "2026-08-28T14:32:00Z",
  "links": {
    "consulta": "/v1/documentos-fiscais/b3f1...",
    "webhookConfigurado": true
  }
}
```

**Se `Idempotency-Key` já existir para o tenant** → `200 OK` com o estado atual do documento (não cria de novo).

**Se validação estrutural falhar antes de enfileirar** → `422 Unprocessable Entity`:
```json
{
  "type": "https://api.exemplo/erros/inconsistencia-valores",
  "title": "Inconsistência nos valores do item",
  "status": 422,
  "detail": "Item SKU123: base de cálculo × alíquota do PIS não confere com o valor informado (esperado 1.65, recebido 1.60)",
  "campo": "itens[0].impostos.pis.valor"
}
```

### 4.3 Consultar status
```
GET /v1/documentos-fiscais/{id}
```
```json
{
  "id": "b3f1...",
  "tipo": "NFE",
  "status": "AUTORIZADA",
  "chaveAcesso": "35260812345678000199550010000001231000001236",
  "protocolo": "135260000012345",
  "numero": 123,
  "serie": 1,
  "modoContingencia": null,
  "atualizadoEm": "2026-08-28T14:32:45Z"
}
```

### 4.4 PDF em Base64
```
GET /v1/documentos-fiscais/{id}/pdf
```
```json
{
  "documentoId": "b3f1...",
  "tipo": "DANFE",
  "formato": "pdf",
  "conteudoBase64": "JVBERi0xLjQKJ..."
}
```
E, como combinado, o mesmo `conteudoBase64` já vem embutido no **webhook de autorização** (evita chamada extra na maioria dos casos):
```json
{
  "evento": "documento.autorizado",
  "documentoId": "b3f1...",
  "tenantId": "...",
  "status": "AUTORIZADA",
  "chaveAcesso": "...",
  "protocolo": "...",
  "pdf": { "formato": "pdf", "conteudoBase64": "..." },
  "xmlAutorizadoBase64": "...",
  "assinaturaHmac": "..."   // HMAC do payload com o webhook_secret do tenant, pro ERP validar autenticidade
}
```

### 4.5 XML autorizado
```
GET /v1/documentos-fiscais/{id}/xml   →  { "xmlBase64": "..." }
```

### 4.6 Cancelamento
```
POST /v1/documentos-fiscais/{id}/cancelamento
Headers: Idempotency-Key: cancel-8231

{ "justificativa": "Erro de digitação no valor do item" }
```

### 4.7 Carta de Correção (NF-e)
```
POST /v1/documentos-fiscais/{id}/carta-correcao
Headers: Idempotency-Key: cce-8231-1

{ "correcao": "Correção do endereço de entrega, sem efeito sobre valores." }
```

### 4.8 Emitir NFS-e (padrão Nacional)
```
POST /v1/documentos-fiscais/nfse
Headers: Idempotency-Key: servico-991

{
  "ambiente": "homologacao",
  "tomador": { "cnpjCpf": "...", "nome": "...", "codigoMunicipioIbge": "..." },
  "servico": {
    "codigoTributacaoNacional": "...",
    "discriminacao": "Serviço de consultoria X",
    "valorServico": 1500.00,
    "issqn": { "aliquota": 3.0, "valor": 45.00 }
  }
}
```
Mesmo padrão de resposta assíncrona (`202` → `PENDENTE` → webhook `documento.autorizado` com DANFSe em base64).

### 4.9 Inutilização de numeração (NF-e/NFC-e)
```
POST /v1/inutilizacoes
Headers: Idempotency-Key: inut-2026-serie1-100-105

{
  "ambiente": "producao",
  "modelo": 55,
  "serie": 1,
  "numeroInicial": 100,
  "numeroFinal": 105,
  "justificativa": "Pulo de numeração por erro de sistema"
}
```

### 4.10 Erros comuns padronizados
| HTTP | Situação |
|---|---|
| `202` | Aceito, processamento assíncrono iniciado |
| `200` | Idempotência: já existia, retorna estado atual |
| `422` | Falha de validação estrutural/consistência (não chegou a ir pra fila) |
| `403` | API Key não autorizada para o ambiente solicitado |
| `404` | Documento não encontrado (ou não pertence ao tenant da key) |
| `409` | Conflito (ex.: tentando cancelar documento que não está `AUTORIZADA`) |
| `429` | Rate limit excedido |

---

## 5. Resumo atualizado das decisões

| Item | Decisão |
|---|---|
| Autenticação | API Key por tenant, hash SHA-256, prefixo indica ambiente, múltiplas chaves ativas |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` + Redis (contadores distribuídos) |
| Cache | Redis — status SEFAZ, PDF recente, config municipal — nunca fonte de verdade |
| Containers | `api` e `worker` separados, `postgres`, `redis`, tudo via docker-compose |
| PDF | Base64 embutido no webhook + endpoint dedicado |
| Webhook | Assinado com HMAC (`webhook_secret` por tenant) pra autenticidade |

Próximo passo natural seria o **Dockerfile** (multi-stage build) e a estrutura de pastas da solução .NET (projetos/camadas). Quer que eu monte isso também?
