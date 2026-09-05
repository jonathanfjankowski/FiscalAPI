# Planejamento Detalhado — API Fiscal (NF-e / NFC-e / NFS-e Nacional)

Stack confirmada: **.NET 8 + PostgreSQL + processamento assíncrono + Unimake.DFe**

---

## 1. Modelo de dados PostgreSQL (detalhado)

### 1.1 `tenants`
```sql
CREATE TABLE tenants (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cnpj                VARCHAR(14) NOT NULL,
    razao_social        VARCHAR(200) NOT NULL,
    uf                  CHAR(2) NOT NULL,
    codigo_municipio_ibge VARCHAR(7),           -- necessário pra NFS-e Nacional
    regime_tributario  SMALLINT NOT NULL,        -- 1 Simples, 2 Simples excesso, 3 Normal
    ambiente_padrao    SMALLINT NOT NULL DEFAULT 2, -- 1 Produção, 2 Homologação
    webhook_url         TEXT,
    webhook_secret      TEXT,                    -- HMAC para assinar payloads do webhook
    ativo                BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em            TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (cnpj)
);
```

### 1.2 `certificados`
```sql
CREATE TABLE certificados (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    pfx_criptografado BYTEA NOT NULL,     -- pfx cifrado com chave de envelope
    senha_criptografada BYTEA NOT NULL,
    chave_dek_criptografada BYTEA NOT NULL, -- DEK (data encryption key) cifrada pela KEK mestra
    thumbprint      VARCHAR(64) NOT NULL,
    valido_ate       DATE NOT NULL,
    ativo             BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em         TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, thumbprint)
);
-- índice pra job diário de "certificados vencendo em N dias"
CREATE INDEX idx_certificados_validade ON certificados (valido_ate) WHERE ativo = TRUE;
```
> Padrão de envelope encryption: uma **DEK** (chave simétrica) por certificado, gerada aleatoriamente, cifra o `.pfx` e a senha; a DEK em si é cifrada por uma **KEK mestra** guardada fora do banco (variável de ambiente / secret manager). Rotacionar a KEK não exige re-cifrar todos os pfx, só as DEKs.

### 1.3 `sequencias_numeracao` (controle de numeração por tenant/série/modelo)
```sql
CREATE TABLE sequencias_numeracao (
    tenant_id     UUID NOT NULL REFERENCES tenants(id),
    modelo        SMALLINT NOT NULL,       -- 55 NFe, 65 NFCe
    serie         SMALLINT NOT NULL,
    ultimo_numero  BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (tenant_id, modelo, serie)
);
```

### 1.4 `documentos_fiscais` (núcleo)
```sql
CREATE TABLE documentos_fiscais (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id           UUID NOT NULL REFERENCES tenants(id),
    idempotency_key     VARCHAR(100) NOT NULL,     -- ver seção 2
    tipo                VARCHAR(10) NOT NULL,      -- NFE, NFCE, NFSE
    ambiente            SMALLINT NOT NULL,          -- 1 Produção, 2 Homologação
    modelo              SMALLINT,                   -- 55/65, null p/ NFSe
    serie               SMALLINT,
    numero              BIGINT,
    chave_acesso        VARCHAR(44),                -- NFe/NFCe
    payload_entrada     JSONB NOT NULL,             -- o que o ERP mandou
    xml_gerado           TEXT,
    xml_assinado          TEXT,
    xml_retorno_sefaz     TEXT,
    protocolo_autorizacao VARCHAR(20),
    status               VARCHAR(20) NOT NULL DEFAULT 'PENDENTE',
    -- PENDENTE, PROCESSANDO, AUTORIZADA, REJEITADA, CONTINGENCIA,
    -- CANCELADA, DENEGADA, ERRO_INTERNO
    motivo_status        TEXT,
    tentativas            INT NOT NULL DEFAULT 0,
    modo_contingencia     VARCHAR(20),               -- NULL, SVC, EPEC, OFFLINE, FSDA
    proxima_tentativa_em  TIMESTAMPTZ,
    criado_em             TIMESTAMPTZ NOT NULL DEFAULT now(),
    atualizado_em         TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, idempotency_key),
    UNIQUE (tenant_id, modelo, serie, numero) -- garante não duplicar numeração
);

CREATE INDEX idx_docfiscal_status_retry ON documentos_fiscais (status, proxima_tentativa_em)
    WHERE status IN ('PENDENTE','CONTINGENCIA');
CREATE INDEX idx_docfiscal_tenant_criado ON documentos_fiscais (tenant_id, criado_em DESC);
```

### 1.5 `eventos_fiscais` (cancelamento, CCe, inutilização)
```sql
CREATE TABLE eventos_fiscais (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    documento_id      UUID NOT NULL REFERENCES documentos_fiscais(id),
    tipo_evento       VARCHAR(30) NOT NULL, -- CANCELAMENTO, CCE, INUTILIZACAO
    idempotency_key   VARCHAR(100) NOT NULL,
    justificativa      TEXT,
    xml_retorno        TEXT,
    status              VARCHAR(20) NOT NULL DEFAULT 'PENDENTE',
    criado_em            TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (documento_id, tipo_evento, idempotency_key)
);
```

### 1.6 `outbox_webhooks` (padrão *outbox* pra notificar o ERP com garantia de entrega)
```sql
CREATE TABLE outbox_webhooks (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID NOT NULL,
    documento_id  UUID NOT NULL,
    payload       JSONB NOT NULL,
    enviado         BOOLEAN NOT NULL DEFAULT FALSE,
    tentativas      INT NOT NULL DEFAULT 0,
    criado_em       TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_outbox_pendente ON outbox_webhooks (enviado, criado_em) WHERE enviado = FALSE;
```

---

## 2. Idempotência (crítico com múltiplos CNPJs e fila assíncrona)

**Problema real**: ERP manda emitir, timeout de rede acontece, ERP reenvia — sem tratamento, isso gera **duas notas fiscais para a mesma venda**, o que é grave (retrabalho de cancelamento, risco fiscal).

**Solução:**
1. Todo `POST /documentos-fiscais` exige um header **`Idempotency-Key`** definido pelo cliente (ex.: o próprio ID do pedido no ERP).
2. Constraint `UNIQUE (tenant_id, idempotency_key)` no banco — se o ERP reenviar com a mesma chave, a API **não cria um novo registro**, apenas retorna o documento existente (status atual), com `200 OK` em vez de `201/202`.
3. Isso é resolvido **antes** de qualquer chamada à SEFAZ — a idempotência protege a camada de entrada, não só a chamada externa.
4. O mesmo padrão vale pra `eventos_fiscais` (cancelamento/CCe também precisam de idempotency key própria, senão um reenvio de cancelamento pode gerar erro de "evento duplicado" tratado incorretamente como falha).

**Fluxo:**
```
POST /nfe  (Idempotency-Key: pedido-8231)
  → SELECT documento WHERE tenant_id=X AND idempotency_key='pedido-8231'
  → se existe: retorna 200 com status atual (idempotente, sem reprocessar)
  → se não existe: INSERT com status PENDENTE dentro de uma transação,
     enfileira o job, retorna 202 Accepted
```

---

## 3. Concorrência — numeração sequencial seguro

Numeração de NF-e/NFC-e **não pode ter buracos nem duplicar** dentro da mesma série. Sob carga (múltiplos workers processando o mesmo tenant em paralelo), isso é um dos pontos mais delicados.

**Abordagem recomendada: obter o próximo número dentro de uma transação com lock a nível de linha.**

```sql
BEGIN;
SELECT ultimo_numero FROM sequencias_numeracao
  WHERE tenant_id = $1 AND modelo = $2 AND serie = $3
  FOR UPDATE;                      -- trava a linha até o commit

UPDATE sequencias_numeracao
  SET ultimo_numero = ultimo_numero + 1
  WHERE tenant_id = $1 AND modelo = $2 AND serie = $3;

-- usa ultimo_numero + 1 como número da nota, grava em documentos_fiscais
COMMIT;
```

- `FOR UPDATE` serializa apenas as transações que disputam **a mesma** combinação tenant/modelo/série — tenants diferentes não se bloqueiam entre si, então isso escala bem no multi-tenant.
- Mantenha essa transação **curta** (só a reserva do número), a chamada à SEFAZ acontece depois, fora dela — nunca segure o lock durante uma chamada de rede.
- A constraint `UNIQUE (tenant_id, modelo, serie, numero)` em `documentos_fiscais` é a **rede de segurança final** contra qualquer bug de concorrência — se algo escapar do lock, o banco rejeita o duplicado.
- **Se a SEFAZ rejeitar** a nota depois do número reservado, o número é "queimado" (comportamento normal e esperado em NF-e — não se reaproveita número de nota rejeitada).

---

## 4. Contingência (o ponto que você pediu pra detalhar)

Contingência é o que acontece quando a SEFAZ (ou a conexão até ela) está indisponível e a operação do lojista/emissor não pode simplesmente parar.

### 4.1 NF-e — modos de contingência suportados
| Modo | Quando usar | Como funciona |
|---|---|---|
| **SVC-AN / SVC-RS** (SEFAZ Virtual de Contingência) | Webservice da SEFAZ de origem está fora, mas o SVC está de pé | Reenvia a mesma NF-e para o endpoint do SVC em vez do endpoint normal. Unimake já tem os endpoints do SVC mapeados por UF — é troca de destino, não de lógica de negócio. |
| **EPEC** (Evento Prévio de Emissão em Contingência) | SEFAZ **e** SVC indisponíveis | Envia um evento simplificado avisando que a operação ocorreu, com envio da NF-e completa depois que os sistemas voltarem. Mais usado quando não dá pra esperar. |
| **Offline / FS-DA (Formulário de Segurança)** | Situações raras hoje em dia, mais histórico | Emissão em papel de formulário de segurança — praticamente não se usa mais desde que SVC ficou robusto, mas vale deixar mapeado. |

### 4.2 NFC-e — contingência é mais simples e mais comum
- NFC-e tem um modo de contingência **offline** nativo: o próprio emissor (seu sistema no PDV) gera e assina o XML localmente, imprime o cupom com uma mensagem de "emitida em contingência" e o QR Code aponta pra um modo de consulta offline.
- Quando a conexão volta, a API transmite o lote pendente pra SEFAZ.
- Isso significa que, arquiteturalmente, a **fila assíncrona já ajuda demais aqui**: se a SEFAZ estiver fora, o worker simplesmente não consegue processar o item, ele fica em `CONTINGENCIA` com backoff, e quando a SEFAZ volta o worker drena a fila — sem o PDV nunca perceber a instabilidade (desde que o PDV opere com o "documento pendente de autorização" localmente).

### 4.3 Máquina de estados sugerida para `documentos_fiscais.status`
```
PENDENTE ──► PROCESSANDO ──► AUTORIZADA
                 │                
                 ├──► REJEITADA (erro de schema/regra de negócio — não reprocessa sozinho)
                 │
                 ├──► CONTINGENCIA (SEFAZ fora do ar) ──► retry automático ──► AUTORIZADA
                 │                                                     └──► DENEGADA
                 │
                 └──► ERRO_INTERNO (bug/exceção — vai pra fila de análise manual)
```

### 4.4 Política de retry/backoff no worker
- Detectar **timeout/erro de conexão** com a SEFAZ → status `CONTINGENCIA`, definir `proxima_tentativa_em` com backoff exponencial (ex.: 30s, 1min, 2min, 5min, 10min, depois fixo em 10min).
- Detectar **rejeição de negócio** (schema inválido, CNPJ não habilitado, duplicidade) → status `REJEITADA`, **não** reprocessa automaticamente — precisa de correção no payload e reenvio (nova idempotency key, é uma nota nova).
- Job periódico (Hangfire recurring job, ex. a cada 15s) varre `WHERE status='CONTINGENCIA' AND proxima_tentativa_em <= now()` e reenfileira.
- Consultar o **status do serviço da SEFAZ** (endpoint de "status do serviço") antes de tentar, pra evitar bater inutilmente numa SEFAZ que já sinalizou indisponibilidade — a Unimake expõe esse tipo de consulta.

### 4.5 NFS-e Nacional e contingência
- O padrão Nacional de NFS-e ainda está em consolidação, mas conceitualmente segue o mesmo princípio: a **DPS** (Declaração de Prestação de Serviços) é enviada, e a prefeitura/sistema nacional retorna a **NFS-e**. Trate indisponibilidade da mesma forma — fila com retry, sem modo de contingência formalizado tão maduro quanto o de NF-e ainda (isso deve evoluir; vale isolar essa lógica num adapter próprio pra não travar o resto do sistema quando mudar).

---

## 5. Homologação vs Produção

Isso precisa ser tratado como **dimensão de primeira classe** no modelo de dados e na configuração, não como detalhe de infraestrutura.

### 5.1 Regras
- Campo `ambiente` (1=Produção, 2=Homologação) existe tanto em `tenants` (ambiente padrão) quanto em `documentos_fiscais` (o ambiente **da nota**, permitindo tenant emitir teste mesmo já em produção, e vice-versa quando fizer sentido pro seu caso de uso).
- **Nunca reaproveitar numeração entre ambientes** — homologação e produção mantêm sequências independentes (na prática a Unimake e a própria SEFAZ já tratam isso como universos separados; garanta isso também na sua tabela `sequencias_numeracao` incluindo `ambiente` na chave se quiser reforçar).
- Em homologação, a razão social do destinatário retorna sempre como **"NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"** — isso é automático da SEFAZ, não precisa tratar no código, mas é bom validar nos testes que esse texto aparece (confirma que está batendo no ambiente certo).
- Endpoints da SEFAZ e do SVC são **diferentes** por ambiente — a Unimake já resolve isso via configuração, você só passa o `tpAmb` corretamente.

### 5.2 Certificado em homologação
- SEFAZ **aceita o mesmo certificado A1 de produção** para homologação (não existe certificado "de teste" separado) — isso simplifica: seu módulo de certificados não precisa de lógica especial por ambiente, só precisa que o tenant tenha certificado válido, independente do ambiente que for emitir.
- Exceção: alguns estados/serviços de homologação de NFC-e podem usar CNPJs de teste específicos divulgados pela SEFAZ — vale documentar isso por UF na wiki do projeto conforme forem aparecendo casos, em vez de tentar prever tudo de antemão.

### 5.3 No contrato da API
- Endpoint recebe explicitamente o campo `"ambiente": "producao" | "homologacao"` no payload (não inferir só da config do tenant) — dá previsibilidade pro ERP, especialmente útil pra vocês (projeto open source) testarem em homologação sem mexer em configuração de tenant.
- Toda resposta (e o texto do DANFE/DANFSE gerado) deve deixar **visualmente óbvio** quando é homologação, pra evitar que alguém confunda uma nota de teste com uma real.

### 5.4 Ambiente de desenvolvimento para contribuidores (open source)
- Vale disponibilizar um **modo "mock"** de emissor (implementação fake de `IEmissorFiscal`) que simula respostas da SEFAZ sem precisar de certificado real — isso baixa MUITO a barreira de entrada pra quem quiser contribuir com o projeto e não tem certificado de teste à mão. Documentar isso claramente como "modo sandbox local", distinto do ambiente de homologação real da SEFAZ.

---

## 6. Fluxo de vida completo de um documento (juntando tudo)

1. **ERP envia** `POST /nfe` com `Idempotency-Key` e `ambiente`.
2. API verifica idempotência → se novo, reserva número (lock transacional) → grava `PENDENTE` → enfileira → responde `202`.
3. **Worker** pega o job:
   - Descriptografa certificado do tenant em memória.
   - Monta XML via Unimake, assina.
   - Verifica status do serviço da SEFAZ (se souber que está fora, já marca `CONTINGENCIA` sem nem tentar).
   - Envia. Trata resposta:
     - Autorizada → grava protocolo, status `AUTORIZADA`, gera evento no `outbox_webhooks`.
     - Rejeitada (regra de negócio) → status `REJEITADA`, evento no outbox, **sem** retry automático.
     - Timeout/erro de conexão → status `CONTINGENCIA`, agenda retry com backoff.
4. **Job de outbox** (separado, roda a cada poucos segundos) entrega os webhooks pendentes ao ERP com retry próprio (não misturar retry de SEFAZ com retry de webhook — são falhas de naturezas diferentes).
5. Certificado descartado da memória assim que a assinatura termina.

---

## 7. Resumo do que ainda falta decidir com você

- Confirmar: **Hangfire** como ponto de partida (mais simples pra contribuidores rodarem localmente) ou já ir de **RabbitMQ**?
- Layout do **contrato REST** (endpoints, payload de entrada da NF-e/NFC-e/NFS-e) — posso detalhar em seguida.
- Estratégia de **DANFE/DANFSE** (geração de PDF) entra em qual fase?

Quer que eu já desenhe o contrato REST (endpoints + JSON de entrada/saída) em cima desse modelo?
