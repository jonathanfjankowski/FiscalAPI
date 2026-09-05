# Adição — Manifestação do Destinatário e Distribuição DFe

Revertendo a decisão anterior: **Manifestação do Destinatário entra no escopo.** Isso muda a natureza da API de "só emissora" para **emissora + receptora**, então vale desenhar com o mesmo cuidado do resto.

Importante: isso se aplica **apenas a NF-e** (modelo 55) — não existe manifestação de destinatário para NFC-e (documento de consumidor final) nem para NFS-e (é outro universo, municipal/nacional de serviços).

---

## 1. Conceito

Para manifestar, primeiro é preciso **saber que uma NF-e foi emitida contra o seu CNPJ** — isso vem do serviço de **Distribuição DFe** da SEFAZ, consultado via **NSU** (Número Sequencial Único, um cursor incremental por CNPJ). O fluxo é:

1. Job periódico consulta a Distribuição DFe do tenant a partir do último NSU processado.
2. Para cada NF-e nova encontrada (emitida por terceiro, contra o CNPJ do tenant), grava localmente e dispara webhook.
3. O ERP (ou o próprio tenant) decide a manifestação e chama a API.
4. A API transmite o evento de manifestação à SEFAZ.

### Tipos de manifestação
| Tipo | Significado | Prazo |
|---|---|---|
| **Ciência da Operação** | "Estou ciente que essa nota existe" — não confirma nem nega o conteúdo | Até 10 dias da emissão (senão a SEFAZ preenche automaticamente) |
| **Confirmação da Operação** | Confirma que a operação ocorreu como descrito | Sem prazo rígido, mas normalmente após conferência da mercadoria/nota |
| **Operação não Realizada** | A operação descrita não ocorreu (nota emitida indevidamente) | — |
| **Desconhecimento da Operação** | O destinatário não reconhece a operação (nunca comprou/contratou) | — |

---

## 2. Modelo de dados adicional

```sql
CREATE TABLE notas_recebidas (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id         UUID NOT NULL REFERENCES tenants(id),  -- o destinatário
    chave_acesso      VARCHAR(44) NOT NULL,
    nsu               BIGINT NOT NULL,
    cnpj_emitente     VARCHAR(14) NOT NULL,
    nome_emitente     VARCHAR(200),
    valor_total       NUMERIC(15,2),
    xml_resumo        TEXT,
    xml_completo      TEXT,
    situacao_manifestacao VARCHAR(25) NOT NULL DEFAULT 'SEM_MANIFESTACAO',
    recebido_em         TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, chave_acesso)
);
CREATE INDEX idx_notas_recebidas_situacao ON notas_recebidas (tenant_id, situacao_manifestacao);

CREATE TABLE ultimo_nsu_processado (
    tenant_id   UUID PRIMARY KEY REFERENCES tenants(id),
    nsu          BIGINT NOT NULL DEFAULT 0,
    atualizado_em TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE manifestacoes (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nota_recebida_id  UUID NOT NULL REFERENCES notas_recebidas(id),
    tipo               VARCHAR(25) NOT NULL,
    justificativa       TEXT,
    idempotency_key      VARCHAR(100) NOT NULL,
    status                VARCHAR(20) NOT NULL DEFAULT 'PENDENTE',
    protocolo             VARCHAR(20),
    xml_retorno           TEXT,
    criado_em              TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (nota_recebida_id, tipo, idempotency_key)
);
```

---

## 3. Endpoints novos

### 3.1 Listar notas recebidas
```
GET /v1/notas-recebidas?situacao=SEM_MANIFESTACAO&desde=2026-08-01
```

### 3.2 Manifestar
```
POST /v1/notas-recebidas/{id}/manifestacao
Headers: Idempotency-Key: manifest-8231

{ "tipo": "ciencia" }
```
```
POST /v1/notas-recebidas/{id}/manifestacao
Headers: Idempotency-Key: manifest-8232

{ "tipo": "desconhecimento", "justificativa": "Nunca realizamos compra com este fornecedor" }
```

### 3.3 Baixar XML completo
```
GET /v1/notas-recebidas/{id}/xml-completo   →  { "xmlBase64": "..." }
```

### 3.4 Webhooks novos
```json
{ "evento": "nota.recebida", "tenantId": "...", "notaRecebidaId": "...", "chaveAcesso": "...", "cnpjEmitente": "...", "valorTotal": 4500.00, "prazoCienciaLimite": "2026-08-30T23:59:59Z" }
```
```json
{ "evento": "manifestacao.processada", "tenantId": "...", "notaRecebidaId": "...", "tipo": "confirmacao", "status": "CONFIRMADA", "protocolo": "..." }
```

---

## 4. Worker — novo job recorrente

```
SincronizarDistribuicaoDFeJob (roda a cada N minutos, por tenant ativo)
  1. Lê ultimo_nsu_processado do tenant
  2. Consulta Distribuição DFe a partir desse NSU (via Unimake)
  3. Para cada resNFe novo: grava em notas_recebidas (idempotente por chave_acesso), dispara webhook
  4. Atualiza ultimo_nsu_processado com o maior NSU retornado
```

- Mesma lógica de contingência/retry dos outros jobs se a SEFAZ estiver indisponível.
- Reaproveita o mesmo certificado do tenant já usado pra emissão.
- Alertar quando uma nota está próxima do prazo de 10 dias sem manifestação de Ciência — tem efeito prático real (perda de prazo pode implicar restrição de crédito fiscal pro tenant).

---

## 5. Ajuste no roadmap

1. Fase 0 — Fundação
2. Fase 1 — NF-e/NFC-e síncrono simples
3. Fase 2 — Assíncrono + fila + contingência + cancelamento/CCe/inutilização
4. Fase 3 — Produção/observabilidade
5. **Fase 4 — Manifestação do Destinatário + Distribuição DFe** (nova)
6. Fase 5 — NFS-e (padrão Nacional)
7. Fase 6 — Documentação e comunidade

---

## 6. Atualização da matriz de operações

| Operação | NF-e | NFC-e | NFS-e Nacional |
|---|:---:|:---:|:---:|
| Emissão | ✅ | ✅ | ✅ |
| Cancelamento | ✅ | ✅ | ✅ |
| Carta de Correção | ✅ | ❌ | ❌ |
| Substituição | — | — | ✅ |
| Inutilização | ✅ | ✅ | ❌ |
| Manifestação do Destinatário | ✅ (novo) | ❌ | ❌ |
| Distribuição DFe (consulta NSU) | ✅ (novo) | ❌ | ❌ |

Frase revisada pro README: **"A API cobre emissão de NF-e/NFC-e/NFS-e e, para NF-e, também o recebimento e manifestação de notas emitidas por terceiros contra o CNPJ do tenant (Distribuição DFe)."**
