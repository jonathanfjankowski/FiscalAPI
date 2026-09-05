# Complemento — Fila, PDF (Base64) e Cálculo de Impostos

## 1. Fila: recomendação final — **Hangfire para começar**

Recomendo começar com **Hangfire** e não RabbitMQ, pelos seguintes motivos aplicados ao seu contexto específico:

- **Menos infraestrutura pra rodar** — usa o próprio PostgreSQL como storage (`Hangfire.PostgreSql`), então quem for clonar o repo (contribuidor open source ou você em produção) sobe com `docker-compose up` sem precisar de um broker separado.
- Já resolve tudo que você precisa agora: fila, **retry com backoff**, **jobs recorrentes** (o job que varre `CONTINGENCIA` e o que varre certificados vencendo), dashboard de observação embutido (`/hangfire`) — útil pra debugar emissões travadas sem precisar de ferramenta externa.
- Volume de NF-e/NFC-e/NFS-e de um ERP típico raramente justifica a complexidade operacional de um broker dedicado logo de cara.

**Quando migrar pra RabbitMQ:** se algum dia precisar de múltiplos serviços consumindo o mesmo evento (ex.: um serviço de e-mail, um de BI, um de contabilidade, todos reagindo a "nota autorizada"), ou throughput muito maior que um único banco aguenta como fila. Isso é fase 4/5, não agora.

**Como não pagar esse preço depois:** isolar toda a lógica de enfileiramento atrás de uma interface própria (`IFilaEmissao.Enfileirar(documentoId)`), implementada hoje com Hangfire. Trocar para RabbitMQ no futuro vira "trocar a implementação", não reescrever a aplicação.

---

## 2. Geração de PDF (DANFE / DANFCe / DANFSe) em Base64

Concordo em **não depender da Unimake.Unidanfe** pra isso — ela aparenta ser fortemente orientada a Windows/EXE (histórico de produtos desktop da Unimake), o que não combina bem com uma API rodando em container Linux.

### 2.1 Biblioteca recomendada: **QuestPDF**
- 100% .NET, funciona perfeitamente em Docker/Linux (sem dependência de GDI+/Windows).
- Licença gratuita para empresas com faturamento até um teto (Community License) — como o projeto é open source, vale deixar isso **documentado claramente no README**, porque quem consumir o projeto comercialmente precisa saber se se enquadra na licença gratuita ou precisa da licença paga. Isso é importante pra transparência de um projeto open source.
- API fluente e produtiva pra montar layouts como DANFE (que tem um layout bem definido pelo Manual de Orientação do Contribuinte).

### 2.2 Onde a geração entra no fluxo
```
Documento AUTORIZADO
        │
        ▼
Worker gera o PDF (DANFE/DANFCe/DANFSe) a partir do XML autorizado
        │
        ▼
PDF convertido para Base64 e:
  a) incluído no payload do webhook (outbox_webhooks) OU
  b) disponibilizado via endpoint dedicado GET /documentos-fiscais/{id}/pdf
```

Recomendo **as duas opções coexistindo**:
- **Webhook** já traz o PDF em base64 junto do aviso de autorização — evita o ERP ter que fazer uma segunda chamada na maioria dos casos (facilita muito a vida do integrador, que é seu objetivo).
- **Endpoint dedicado** (`GET /documentos-fiscais/{id}/pdf` → `{ "pdfBase64": "..." }`) pra quando o ERP quiser reimprimir depois, sem guardar o base64 do lado dele.

### 2.3 Persistência do PDF
- Gerar o PDF é barato (é determinístico a partir do XML autorizado), então **não precisa guardar o binário do PDF no Postgres** — isso infla o banco desnecessariamente.
- Recomendo: gerar sob demanda (cache curto em memória/Redis se quiser evitar regerar em rajadas de reimpressão) a partir do `xml_retorno_sefaz` já salvo. Se preferir simplicidade máxima no início, pode gerar uma vez no momento da autorização e persistir em disco/object storage (não no banco relacional) com o caminho salvo em `documentos_fiscais.pdf_path`.

### 2.4 Ajuste no schema
```sql
ALTER TABLE documentos_fiscais ADD COLUMN pdf_path TEXT; -- opcional, se optar por persistir
```

### 2.5 NFS-e Nacional — atenção
O documento auxiliar da NFS-e Nacional (DANFSe) tem layout definido pelo próprio padrão nacional — vale seguir esse layout oficial à risca no QuestPDF, já que, diferente do DANFE (mais "livre" quanto a arranjo visual desde que contenha os elementos obrigatórios), o DANFSe Nacional tende a ser mais padronizado entre prefeituras justamente por ser nacional.

---

## 3. Cálculo de impostos — recomendação: **não calcular, apenas validar estrutura**

Essa é uma decisão de escopo importante e a resposta curta é: **receba os valores já calculados do integrador, e faça validação estrutural/consistência — não um motor de cálculo tributário.**

### Por quê

1. **Cálculo de imposto não é "regra fixa", é um motor de decisão de negócio** — depende do regime tributário do emitente (Simples Nacional com todos os seus anexos, Lucro Presumido, Lucro Real), de benefícios fiscais e regimes especiais por estado, de NCM/CFOP combinados, de ST (Substituição Tributária) com tabelas por UF que mudam constantemente, e agora da **Reforma Tributária (IBS/CBS)** em transição plurianual. Manter isso correto e atualizado é essencially manter um produto à parte (tipo um "IOB" ou "Sittax") — é um projeto em si, não um módulo de uma API de transmissão fiscal.
2. **Responsabilidade legal**: se a API calcular o imposto errado, o erro fiscal (e a responsabilidade de correção/retificação) recai sobre quem manteve a lógica de cálculo. Como projeto open source mantido por vocês, isso é um risco desproporcional ao benefício.
3. **O ERP quase sempre já tem essa lógica** — geralmente é o próprio ERP que conhece o cadastro fiscal do produto/cliente (regra de tributação, exceções, benefícios) melhor do que uma API genérica conseguiria genericamente sem esse contexto.
4. Isso **também facilita mais pro integrador**, não menos: uma API que exige "me dê o produto e eu calculo" obriga o integrador a te dar contexto tributário completo (regime, NCM, benefícios, tabela de ST vigente) — na prática ele teria que implementar a lógica de qualquer forma só pra te alimentar direito. É mais simples e mais previsível ele mandar os valores já calculados.

### O que a API **deve** fazer (validação, não cálculo)
- **Validação estrutural via schema XSD** — isso a Unimake já faz ao montar o XML (throws se faltar campo obrigatório, tipo errado, etc.).
- **Validação de consistência aritmética** antes de montar o XML — conferências que são simples soma/subtração, não "conhecimento tributário":
  - Soma dos itens = valor total da nota.
  - Base de cálculo × alíquota = valor do imposto informado (com tolerância de centavos por arredondamento) — isso é **aritmética**, não é decidir a alíquota correta, então é seguro e útil validar.
  - Consistência entre CST/CSOSN informado e presença/ausência dos campos esperados pra aquele código (ex.: se CST indica isento, não pode vir valor de imposto — isso é uma checagem estrutural do XML, não uma decisão fiscal).
- Retornar erro **claro e específico** (`422` com detalhamento de qual campo está inconsistente) antes de gastar um envio pra SEFAZ com dado obviamente errado — bom tanto pra UX do integrador quanto pra evitar rejeições desnecessárias.

### Documentar isso explicitamente
Vale deixar bem claro no README/contrato da API, logo no topo: **"Esta API não calcula tributos. Os valores de impostos devem ser calculados pelo sistema integrador e enviados já calculados. A API realiza validações estruturais e de consistência antes da transmissão."** — isso evita expectativa errada de quem for integrar.

### Espaço pra evoluir (não é MVP)
Se no futuro fizer sentido, dá pra oferecer isso como **serviço opcional plugável** (ex.: endpoint `/calculadora/icms-st` isolado, best-effort, claramente marcado como "auxiliar, não substitui apuração fiscal") — mas como módulo separado e opcional, nunca como parte obrigatória do fluxo de emissão.

---

## 4. Atualização do resumo de decisões

| Decisão | Escolha |
|---|---|
| Fila | Hangfire (Postgres storage), abstraída atrás de interface |
| Numeração | Lock transacional `FOR UPDATE` + constraint única |
| Idempotência | Header `Idempotency-Key` + constraint única por tenant |
| PDF | QuestPDF, gerado sob demanda, entregue em Base64 (webhook + endpoint) |
| Impostos | Recebidos já calculados; API só valida estrutura/consistência |
| NFS-e | Padrão Nacional |
| Certificados | Upload próprio, envelope encryption |

Quer que eu desenhe agora o **contrato REST completo** (endpoints, JSON de request/response, incluindo o campo de PDF base64 e os erros de validação de consistência)?
