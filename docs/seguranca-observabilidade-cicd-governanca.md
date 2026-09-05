# Complemento Final — API Key Multi-Tenant, Segurança, Observabilidade, CI/CD, Backup e Governança

## 0. Sobre "meu ERP é um backend só, vai usar só uma API Key" — correção importante

Não recomendo isso, mesmo o ERP sendo um backend único. O motivo é o **isolamento por CNPJ**, não o número de sistemas que consomem a API.

**Modelo recomendado: uma API Key por tenant (CNPJ), e o seu backend guarda e usa N chaves — uma por CNPJ que ele administra.**

Isso é exatamente o modelo que plataformas tipo Stripe Connect, iFood (pra restaurantes) e a maioria dos ERPs multi-empresa usam: a plataforma é um sistema só, mas cada empresa/CNPJ tem sua própria credencial.

### Por que isso importa mesmo com um backend único
1. **Blast radius**: se uma chave vazar (log exposto, variável de ambiente comprometida, etc.), só aquele CNPJ é afetado — não todos os seus clientes de uma vez.
2. **Revogação cirúrgica**: se um CNPJ encerra contrato/é desabilitado, você revoga a chave dele sem tocar nos demais.
3. **Auditoria clara**: toda ação na SEFAZ fica naturalmente atrelada a uma chave = um tenant, sem precisar confiar em um campo `tenantId` que veio dentro do payload (que é mais fácil de errar/falsificar do que a própria credencial de autenticação).
4. **Já bate com o schema que desenhamos** (`api_keys.tenant_id`) — não é overhead adicional de design, é usar o que já está modelado.

### Isso não complica sua vida como integrador único
- Seu backend só precisa de uma tabela local `cnpj → api_key` (ou usar um cofre de segredos) e escolher a chave certa por requisição, dependendo de qual CNPJ está emitindo a nota. Isso é trivial de implementar — é uma consulta antes de montar o request HTTP.
- Se **no futuro** você quiser mesmo um modelo de "chave mestra da plataforma" (uma key só, que especifica o `tenantId` no payload/header, tipo Stripe Connect com `Stripe-Account`), dá pra oferecer isso como **opção adicional avançada** — mas eu não começaria por aí, porque perde a camada de segurança mais simples e mais robusta (isolamento por credencial).

**Resumindo a resposta direta:** não, não deveria ser uma key só — deveria ser uma key por CNPJ, mesmo com um único backend consumidor.

---

## 1. Segurança e LGPD

### 1.1 Dados pessoais envolvidos
- Destinatário/tomador: CPF/CNPJ, nome, endereço — dado pessoal quando pessoa física.
- Isso está espalhado em `documentos_fiscais.payload_entrada` (JSONB) e nos XMLs armazenados.

### 1.2 Medidas recomendadas
- **Criptografia em repouso** no nível do banco (Postgres com `pgcrypto` pra colunas sensíveis, ou disco criptografado no provedor de nuvem como piso mínimo) — não só o certificado, mas endereços/documentos de destinatário PF merecem o mesmo cuidado.
- **Política de retenção documentada**: nota fiscal tem obrigação legal de guarda (geralmente 5 anos), então "direito ao esquecimento" da LGPD não se aplica da mesma forma que em outros sistemas — isso precisa estar **explícito no README/política de privacidade** do projeto, porque é uma exceção legítima e comum de se perguntar.
- **Minimização**: não guardar campos que não são necessários pro fluxo fiscal (ex.: não pedir telefone do destinatário se a nota não exige).
- **Direito de portabilidade/acesso**: como projeto open source que outros vão hospedar, documentar isso como responsabilidade de quem opera a instância (você não controla os dados de quem faz fork e roda a própria instância) — deixar isso claro no README evita mal-entendido de responsabilidade.

### 1.3 Segurança operacional adicional
- **HTTPS obrigatório** (rejeitar HTTP puro), HSTS.
- **Validação de payload rígida** (tamanho máximo de campos, content-type, limite de rate por IP além do rate por API Key).
- **Secrets nunca em variável de ambiente em texto legível em produção real** — variável de ambiente é aceitável pra dev/homologação, mas em produção o ideal é secret manager (Azure Key Vault, AWS Secrets Manager, Vault) — vale deixar isso como opção plugável, já que é open source e nem todo mundo terá acesso a esses serviços.

---

## 2. Auditoria (log de auditoria, não log de aplicação)

Auditoria é **diferente** de log técnico — é sobre "quem fez o quê, quando", com foco em rastreabilidade legal/de segurança, não em debug.

```sql
CREATE TABLE auditoria (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id     UUID,
    api_key_id    UUID,               -- qual credencial foi usada
    acao          VARCHAR(50) NOT NULL, -- "CERTIFICADO_DESCRIPTOGRAFADO", "DOCUMENTO_CANCELADO", "API_KEY_CRIADA", etc.
    recurso_id    UUID,                -- id do documento/certificado/etc. afetado
    ip_origem     INET,
    detalhe        JSONB,
    criado_em      TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

**Eventos que obrigatoriamente devem gerar auditoria:**
- Toda vez que um certificado é descriptografado (mesmo que só pra assinar, sem erro).
- Criação/revogação de API Key.
- Cancelamento e Carta de Correção (são atos com efeito legal).
- Qualquer acesso a `GET /documentos-fiscais/{id}/xml` (é dado sensível/fiscal sendo exportado).
- Falhas de autenticação repetidas (possível tentativa de força bruta na API Key).

Essa tabela é **append-only** (nunca update/delete) — reforçar isso a nível de permissão de banco (usuário da aplicação só tem `INSERT`, nunca `UPDATE`/`DELETE` nessa tabela).

---

## 3. Licença do projeto

Antes de bater o martelo na licença do seu projeto, **confirme a licença da Unimake.DFe** — se ela for restritiva de alguma forma (ex.: exigir atribuição específica, ou ter cláusula sobre uso comercial), sua licença precisa ser compatível. A prática mais comum em projetos .NET open source de infraestrutura é **MIT** (permissiva, simples, atrai mais contribuidores/adoção comercial) — mas essa é uma decisão que depende de checar os termos reais da Unimake antes.

---

## 4. Observabilidade

### 4.1 Logs estruturados
- **Serilog** com sink pra arquivo/stdout (JSON) — permite agregação depois em qualquer ferramenta (ELK, Grafana Loki, Seq).
- Todo log deve carregar `documento_id`, `tenant_id`, `correlation_id` — isso permite reconstruir o caminho completo de uma emissão (API → fila → worker → SEFAZ → webhook) filtrando por um único ID.

### 4.2 Métricas (recomendo OpenTelemetry + Prometheus/Grafana)
Métricas específicas de domínio fiscal, não só técnicas (CPU/memória):
- Taxa de rejeição por UF/tipo de documento (se um estado começa a rejeitar muito, é sinal de problema de schema/regra).
- Tempo médio entre `PENDENTE` → `AUTORIZADA`.
- **Quantidade de documentos em `CONTINGENCIA` há mais de N minutos** — isso é o alerta mais importante do sistema inteiro, porque significa nota parada sem chegar na SEFAZ.
- Taxa de sucesso de entrega de webhook.

### 4.3 Alertas
- Certificado vencendo em ≤ 15 dias (por tenant).
- Fila de contingência crescendo (não drenando).
- Taxa de erro interno (`ERRO_INTERNO`) acima de um limiar — isso indica bug, não instabilidade externa.

### 4.4 Health checks
```
GET /health/live   → processo está de pé
GET /health/ready   → consegue falar com Postgres e Redis
```
Usado pelo Docker/orquestrador pra saber se reinicia o container.

---

## 5. Testes e CI/CD

### 5.1 Camadas de teste
| Tipo | O quê | Quando roda |
|---|---|---|
| Unitário | Validação de consistência (soma de itens, base×alíquota), lógica de idempotência, cálculo de próximo número | Todo PR |
| Integração (sandbox/mock) | Fluxo completo API → fila → worker → emissor mock → webhook | Todo PR |
| Integração real (homologação SEFAZ) | Emissão de fato contra o ambiente de homologação, com certificado de teste | Pipeline manual/nightly (não em todo PR — depende de rede externa e é mais lento/instável) |
| Contrato | Validar que os payloads de exemplo do contrato REST continuam válidos contra o schema JSON documentado | Todo PR |

### 5.2 GitHub Actions (sugestão de pipeline)
```
build-and-test.yml:
  - restore/build
  - dotnet test (unitário + integração mock)
  - lint (dotnet format --verify-no-changes)

docker-publish.yml (em tag/release):
  - build da imagem Docker (api + worker)
  - push pro GitHub Container Registry (ghcr.io) — bom pra projeto open source, sem custo de registry privado
```
- Badge de "build passing" no README ajuda credibilidade pra atrair contribuidores.

---

## 6. Backup e Disaster Recovery

Isso é **crítico** aqui especificamente porque o banco guarda **certificados digitais** — perder o banco sem backup significa todos os tenants ficarem impossibilitados de emitir até reenviarem o certificado manualmente (impacto operacional direto no negócio deles).

- Backup do Postgres: `pg_dump` agendado + WAL archiving (point-in-time recovery) se o volume justificar.
- **Testar restore periodicamente** (backup que nunca foi restaurado em teste não é backup confiável — é só uma esperança).
- A KEK mestra (chave que descriptografa os certificados) deve ter **backup próprio, separado do backup do banco** — se as duas coisas estiverem no mesmo lugar e esse lugar for perdido, os certificados ficam irrecuperáveis mesmo com o banco restaurado.
- RTO/RPO: definir isso é decisão de negócio, mas pra uma API que emite documento fiscal (onde atraso vira problema de contingência real do lojista), vale mirar RPO baixo (poucos minutos de perda máxima aceitável).

---

## 7. Governança do projeto open source

- `CONTRIBUTING.md` — como rodar localmente (docker-compose + modo sandbox já ajuda muito aqui), padrão de commit, como rodar os testes antes de abrir PR.
- `CODE_OF_CONDUCT.md` — padrão (Contributor Covenant é o mais usado).
- Templates de Issue (bug report / feature request) e PR no `.github/`.
- `SECURITY.md` — como reportar vulnerabilidade **de forma privada** (importante num projeto que lida com certificado digital e dado fiscal — não quer que alguém abra uma issue pública descrevendo uma falha de segurança).
- Roadmap público (pode ser o próprio GitHub Projects) — dá transparência de pra onde o projeto está indo, o que ajuda contribuidor a saber onde encaixar esforço.

---

## 8. DANFE vs DANFCe vs DANFSe — layouts diferentes no módulo de PDF

Vale deixar isso explícito porque não é o mesmo template com dados diferentes — são **layouts fisicamente diferentes**:

| Documento | Formato | Papel |
|---|---|---|
| DANFE (NF-e) | A4, retrato, várias seções (destaques fiscais, tabela de itens, dados de transporte) | Impressora comum |
| DANFCe (NFC-e) | Bobina térmica, ~80mm (ou 58mm), layout vertical estreito, foco no QR Code | Impressora térmica de cupom |
| DANFSe (NFS-e Nacional) | Layout definido pelo padrão nacional, mais próximo de A4/relatório de serviço | Impressora comum |

No QuestPDF isso significa **três `IDocument` diferentes**, não um só parametrizado — a estrutura de dados de entrada até pode ser compartilhada em parte, mas o layout de página (tamanho, orientação, densidade de informação) é próprio de cada um. Vale planejar isso como três templates desde o início, não como "um DANFE genérico que depois vira os outros dois".

---

## 9. Próximo passo

Com tudo isso mapeado, o que ficou pendente tecnicamente é o **Dockerfile multi-stage** e a **estrutura de pastas/projetos da solução .NET** (que geralmente reflete os módulos que já desenhamos: API, Core, Adapter Unimake, Worker, Persistence, PDF). Seguimos pra isso?
