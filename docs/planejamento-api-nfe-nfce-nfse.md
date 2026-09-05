# Planejamento — API de Emissão de NF-e, NFC-e e NFS-e (multi-CNPJ)

## 1. Contexto e decisões já tomadas
- Projeto **open source**.
- API vai ser **consumida pelo seu ERP** (não pelo usuário final direto).
- Precisa suportar **múltiplos CNPJs** (multi-tenant fiscal).
- Certificados A1: **upload feito pelo cliente/ERP e armazenado por vocês** (banco/storage criptografado).
- Biblioteca: **Unimake.DFe** (decisão já tomada).

---

## 2. Unimake.DFe vs Zeus (DFe.NET) — por que Unimake faz sentido aqui

| Critério | Unimake.DFe | Zeus DFe.NET |
|---|---|---|
| Cobertura de documentos | NFe, NFCe, CTe, CTeOS, MDFe, GNRe, **NFSe (inclusive NFSe Nacional)**, eSocial, EFD-Reinf, NF3e, NFCom | NFe, NFCe; CTe/MDFe ainda em evolução; suporte a NFSe é bem mais limitado |
| Adequação à Reforma Tributária (IBS/CBS) | Já trabalhando ativamente nisso (releases recentes tratam tags `gIBSCredPres`/`gCBSCredPres`) | Suporte parcial iniciado em 2025.10, ainda em issue de acompanhamento |
| Ritmo de releases | Muito ativo (múltiplos releases por mês no NuGet) | Ativo, mas com escopo mais restrito |
| NFS-e | Suporte explícito, inclusive ao padrão **Nacional** (ABRASF está sendo substituído gradualmente pelo padrão nacional) | Não é o foco principal |
| Documentação/exemplos | Boa quantidade de exemplos no próprio repo | Também tem, comunidade mais antiga (era o famoso "DFe.NET" histórico) |
| Licença | Verificar no repo (MIT costuma aparecer) | LGPL |

**Ponto crítico pro seu caso:** como você quer emitir **NF-e + NFC-e + NFS-e com uma única lib**, a Unimake é a escolha mais coerente — é a única das duas com suporte real a NFS-e (incluindo o padrão Nacional, que é o caminho oficial daqui pra frente, substituindo o ABRASF município a município).

---

## 3. Arquitetura proposta

```
┌─────────────┐      ┌──────────────────────────────────────────┐
│   Seu ERP   │ ───▶ │              API Gateway/REST             │
└─────────────┘      │        (ASP.NET Core Web API .NET 8)      │
                      └───────────────┬────────────────────────────┘
                                       │
              ┌────────────────────────┼─────────────────────────┐
              ▼                        ▼                          ▼
      ┌───────────────┐      ┌─────────────────┐       ┌──────────────────┐
      │  Módulo NF-e /  │      │  Módulo NFS-e   │       │ Módulo Certificados│
      │  NFC-e (Unimake)│      │  (Unimake)      │       │  (upload/vault)    │
      └───────┬────────┘      └────────┬────────┘       └─────────┬─────────┘
              │                        │                           │
              ▼                        ▼                           │
      ┌────────────────────────────────────────┐                  │
      │     Fila assíncrona (worker/consumer)    │◀─────────────────┘
      └───────────────┬──────────────────────────┘
                       ▼
              ┌──────────────────┐
              │  SEFAZ / Prefeit. │
              └──────────────────┘
                       │
                       ▼
              ┌──────────────────┐
              │   PostgreSQL      │  (notas, status, XML, eventos, tenants)
              └──────────────────┘
```

### Módulos sugeridos (separação por responsabilidade)
1. **API.Gateway** — endpoints REST, autenticação, validação de payload, multi-tenant routing.
2. **Fiscal.Core** — abstrações de domínio (Nota, Tenant, Certificado, Status) independentes da lib usada — isso facilita trocar/testar Unimake sem acoplar tudo.
3. **Fiscal.Unimake.Adapter** — implementação concreta usando Unimake.DFe, isolada atrás de uma interface (`IEmissorFiscal`).
4. **Fiscal.Worker** — processa fila, chama SEFAZ/prefeitura, trata retry/timeout/contingência.
5. **Fiscal.Certificados** — upload, criptografia em repouso, cache em memória do certificado descriptografado por tenant (nunca gravar em disco em texto plano).
6. **Fiscal.Persistence** — repositórios (EF Core + PostgreSQL).

---

## 4. Banco de dados — recomendação: **PostgreSQL**

Motivos, dado que o projeto é open source e você não decidiu ainda:
- **Gratuito e sem restrição de licença comercial** (bom pra open source, ao contrário do SQL Server que empurra pra Express com limites).
- Excelente suporte a **JSONB** — útil pra guardar payloads de retorno da SEFAZ (que variam por UF) sem precisar modelar tudo em colunas rígidas.
- Ótimo suporte no EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- Fácil de rodar em Docker, bom para contribuidores externos do projeto open source subirem o ambiente localmente.

Estrutura mínima de tabelas:
- `tenants` (CNPJ, razão social, ambiente homolog/produção, UF, config municipal p/ NFS-e)
- `certificados` (tenant_id, certificado criptografado, senha criptografada, validade, thumbprint)
- `documentos_fiscais` (tipo: NFe/NFCe/NFSe, tenant_id, chave/numero, status, xml_enviado, xml_retorno, protocolo, criado_em)
- `eventos_fiscais` (cancelamento, carta de correção, inutilização — histórico)
- `filas_processamento` (se não usar RabbitMQ e quiser algo simples via banco/outbox)

---

## 5. Processamento síncrono vs assíncrono — recomendação: **assíncrono desde o início**

Mesmo você não tendo decidido, para o seu cenário (múltiplos CNPJs, ERP integrado) eu recomendo fila assíncrona já na v1, por causa de:
- **Contingência**: SEFAZ cai, tem timeout, tem instabilidade — se for síncrono, o ERP trava esperando.
- **Picos de emissão** (ex.: fim de mês, Black Friday) — fila absorve.
- **Reprocessamento automático** de rejeições recuperáveis (ex.: erro de schema, número duplicado) sem o ERP precisar reenviar.
- NFS-e municipal costuma ser mais lenta/instável que SEFAZ estadual — assíncrono evita acoplar a resposta da API à demora da prefeitura.

**Stack sugerida:**
- **RabbitMQ** (open source, se encaixa bem com o espírito do projeto, boa integração .NET via MassTransit).
- Alternativa mais simples para começar: **Hangfire** (mesmo processo, fila com persistência em Postgres, menos infraestrutura para o contribuidor rodar localmente) — pode ser um ótimo "MVP" antes de partir pro RabbitMQ.

Fluxo sugerido:
1. ERP chama `POST /nfe` → API valida payload, salva no banco com status `pendente`, enfileira, retorna `202 Accepted` + `id` do documento.
2. Worker consome, monta XML via Unimake, assina, transmite, salva retorno.
3. ERP consulta `GET /nfe/{id}/status` (polling) **ou** recebe webhook (recomendo webhook configurável por tenant, pra evitar polling agressivo).

---

## 6. Certificados digitais — cuidados essenciais (você optou por armazenar você mesmo)

Como vocês vão guardar o certificado, isso é o ponto mais sensível do projeto em termos de segurança e responsabilidade legal:

- **Nunca** salvar o `.pfx` nem a senha em texto puro no banco.
- Criptografar com uma chave mestra fora do banco (variável de ambiente / secret manager), idealmente com **envelope encryption** (chave de dados por tenant, criptografada por uma chave mestra).
- Carregar o certificado **em memória** apenas no momento da assinatura, e descartar logo em seguida (`X509Certificate2` com `Dispose()` explícito).
- Logar **acesso** ao certificado (auditoria: quem/quando descriptografou), nunca logar o conteúdo.
- Validar vencimento do certificado no upload e alertar o tenant com antecedência (certificado A1 vence 1x/ano — isso vai gerar suporte se não for tratado).
- Considerar, no roadmap, oferecer como **opção futura** integração com cofre externo (Azure Key Vault / AWS KMS) para quem quiser nível de segurança maior — sem tornar isso obrigatório na v1.

---

## 7. Multi-tenant / múltiplos CNPJs — pontos de atenção

- Toda chamada à API precisa identificar o tenant (CNPJ) — via API Key por tenant é mais simples de auditar do que só um JWT genérico.
- Configuração fiscal varia por tenant: regime tributário (Simples/Normal), ambiente (homologação/produção), série/numeração de NF-e independente por tenant, e para NFS-e, **cada prefeitura tem regras/layout próprios** (isso é o maior desafio técnico do projeto).
- Numeração de NF-e/NFC-e deve ser controlada de forma atômica por tenant (evitar duplicidade sob concorrência — usar transação/lock otimista no banco).

---

## 8. NFS-e — atenção especial (é o mais complexo dos três)

- NF-e e NFC-e seguem padrão nacional único (SEFAZ/Receita).
- NFS-e historicamente é **por município** (ABRASF ou layout próprio), mas está migrando para o **padrão Nacional (Sistema Nacional NFS-e)**.
- Recomendação: comece o roadmap suportando **NFS-e Nacional** (é onde a Unimake está investindo, é o futuro, e reduz a explosão de combinações por município) e trate municípios legados fora do padrão nacional como fase 2, sob demanda.

---

## 9. Roadmap sugerido (fases)

1. **Fase 0 — Fundação**
   - Setup do projeto (.NET 8, EF Core + Postgres, Docker Compose para dev).
   - Módulo de tenants + certificados (upload, criptografia, validação de vencimento).
   - CI básico (build, testes) — importante desde já por ser open source.

2. **Fase 1 — NF-e/NFC-e síncrono simples (homologação)**
   - Adapter Unimake para emissão de NF-e/NFC-e.
   - Endpoint síncrono simples pra validar a integração ponta a ponta em homologação.

3. **Fase 2 — Assíncrono + fila**
   - Introduzir Hangfire ou RabbitMQ.
   - Endpoints `202 Accepted` + consulta de status + webhook.
   - Cancelamento, carta de correção, inutilização.

4. **Fase 3 — NFS-e Nacional**
   - Adapter NFS-e via Unimake.
   - Tratar peculiaridades de cadastro municipal por tenant.

5. **Fase 4 — Produção/observabilidade**
   - Logs estruturados, métricas (Prometheus/Grafana), alertas de certificado vencendo, retry inteligente, DANFE/DANFSE em PDF.

6. **Fase 5 — Documentação e comunidade (open source)**
   - README detalhado, exemplos de payload, guia de contribuição, changelog, ambiente de homologação fake pra quem for testar sem certificado real.

---

## 10. Próximos passos imediatos

- Confirmar se topa começar com **Hangfire** (mais simples de rodar localmente, bom pra atrair contribuidores open source) e migrar pra **RabbitMQ** depois, se a demanda de escala pedir.
- Desenhar o contrato inicial da API (endpoints de NF-e primeiro, já pensando em NFS-e no formato de payload desde já pra não quebrar depois).
- Modelar o schema do Postgres (tenants, certificados, documentos_fiscais).

Se quiser, no próximo passo eu já desenho o **schema do banco** ou o **contrato REST inicial (endpoints + payloads)** — qual prefere atacar primeiro?
