# Cobertura de Operações — Matriz por Tipo de Documento

## Correção importante: Carta de Correção (CC-e) não existe para todos os tipos

No contrato REST desenhei `POST /v1/documentos-fiscais/{id}/carta-correcao` de forma genérica, mas isso está **errado** para NFC-e e NFS-e:

| Documento | Tem CC-e? | Como corrige erro pós-autorização |
|---|---|---|
| **NF-e** (modelo 55) | ✅ Sim | Carta de Correção Eletrônica — corrige dados não relacionados a valores/impostos/destinatário (ex.: dados de transporte, informações complementares). Até 20 CC-e por nota, cada uma sequencial e cumulativa. |
| **NFC-e** (modelo 65) | ❌ Não existe | Documento de consumidor final não tem CC-e no padrão nacional — se algo estiver errado, a única saída é **cancelar** (dentro da janela permitida, geralmente 30 min) e **emitir uma nova**. |
| **NFS-e Nacional** | ❌ Não tem "CC-e" no mesmo formato | Correção geralmente ocorre via **cancelamento + nova emissão**, ou, dependendo da regra do padrão Nacional/município, um evento de **substituição** (que cancela a original e vincula a nova automaticamente). |

**Ajuste no contrato REST:** o endpoint de carta de correção deve **rejeitar com `409 Conflict`** se chamado para NFC-e ou NFS-e, com mensagem explicando que esse tipo de documento não suporta CC-e — em vez de simplesmente aceitar e falhar silenciosamente lá na frente na SEFAZ.

---

## Matriz completa de operações por documento

| Operação | NF-e | NFC-e | NFS-e Nacional |
|---|:---:|:---:|:---:|
| Emissão | ✅ | ✅ | ✅ |
| Consulta de status | ✅ | ✅ | ✅ |
| Cancelamento | ✅ (até 24h, regra por UF) | ✅ (janela mais curta, ~30min) | ✅ |
| Carta de Correção (CC-e) | ✅ | ❌ não se aplica | ❌ não se aplica |
| Substituição (corrige via cancelar+reemitir vinculado) | — | — | ✅ |
| Inutilização de numeração | ✅ | ✅ | ❌ não se aplica (não tem numeração sequencial do mesmo jeito) |
| Contingência | ✅ (SVC/EPEC) | ✅ (offline nativo) | Fila com retry (sem modo formal maduro ainda) |
| PDF (documento auxiliar) | DANFE | DANFCe | DANFSe |
| XML autorizado | ✅ | ✅ | ✅ (ou o retorno padrão do sistema nacional) |

---

## O que ficou de fora de propósito (vale deixar explícito)

Essas operações existem no universo fiscal brasileiro, mas **não fazem sentido pro escopo desta API**, que é focada em **emissão** (você sendo o emitente), não em recebimento/gestão de notas de terceiros:

- **Manifestação do Destinatário** (confirmar/rejeitar operação, ciência da emissão) — isso é para quem **recebe** NF-e de fornecedores, não para quem emite. Fora de escopo.
- **Distribuição DFe / consulta por NSU** (baixar notas emitidas contra o seu CNPJ por terceiros) — mesmo motivo, é operação de quem recebe, não de quem emite.
- **Consulta Cadastro de Contribuinte** (verificar situação cadastral de um CNPJ/IE na SEFAZ) — útil em alguns fluxos, mas não é uma operação de emissão em si; pode entrar como funcionalidade auxiliar futura, não como parte do core do contrato.

Vale colocar isso também no README, na mesma seção de limitações onde já vai a nota sobre NFS-e — deixa claro que a API é **emissor**, não uma ferramenta completa de gestão fiscal bidirecional.

---

## Endpoint adicional que faltou: consulta de status do serviço SEFAZ

Mencionei isso internamente na lógica de contingência do worker, mas não expus como endpoint público — vale expor, porque é útil pro próprio ERP saber de antemão se vale a pena nem tentar (ou avisar o usuário do PDV que está em modo de contingência):

```
GET /v1/status-servico/{uf}?documento=nfe

{
  "uf": "PR",
  "documento": "nfe",
  "disponivel": true,
  "tempoMedioResposta": "00:00:02",
  "consultadoEm": "2026-08-28T15:00:00Z"   -- resultado cacheado no Redis, TTL curto
}
```

---

## Ajuste na tabela `documentos_fiscais.status` — adicionar estado

Faltou um status pra cobrir cancelamento em andamento (evento assíncrono também, não é instantâneo):

```
AUTORIZADA ──► CANCELAMENTO_PENDENTE ──► CANCELADA
                                    └──► ERRO_CANCELAMENTO (evento rejeitado, nota continua AUTORIZADA)
```

E o mesmo padrão vale pro evento de substituição da NFS-e.

---

## Resumo do ajuste

| Item | Ajuste |
|---|---|
| CC-e | Só NF-e; `409` para NFC-e/NFS-e |
| Substituição | Novo endpoint específico pra NFS-e (`POST /v1/documentos-fiscais/{id}/substituicao`) |
| Inutilização | Só NF-e/NFC-e |
| Manifestação do Destinatário / Distribuição DFe | Fora de escopo, documentar explicitamente |
| Consulta de status do serviço | Novo endpoint público `GET /v1/status-servico/{uf}` |
| Status de documento | Adicionar `CANCELAMENTO_PENDENTE` / `ERRO_CANCELAMENTO` |

Com isso a cobertura de operações fica completa e correta por tipo de documento. Quer que eu já atualize o contrato REST original consolidando esses ajustes num único documento final, ou seguimos assim mesmo (com os adendos separados)?
