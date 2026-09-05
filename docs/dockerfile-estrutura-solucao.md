# Dockerfile e Estrutura de Pastas da Solução .NET

## 1. Estrutura de pastas / projetos

A divisão reflete os módulos que já desenhamos ao longo do planejamento — cada um com uma responsabilidade e dependências bem delimitadas, o que facilita tanto manutenção quanto contribuição externa (alguém pode entender/mexer em `Fiscal.Pdf` sem precisar entender o resto).

```
fiscal-api/
├── src/
│   ├── Fiscal.Api/                    # Camada HTTP — controllers, middlewares, autenticação por API Key
│   │   ├── Controllers/
│   │   │   ├── DocumentosFiscaisController.cs
│   │   │   ├── InutilizacoesController.cs
│   │   │   └── CertificadosController.cs
│   │   ├── Middlewares/
│   │   │   ├── ApiKeyAuthenticationMiddleware.cs
│   │   │   └── RateLimitingMiddleware.cs      # ou config nativa do RateLimiting
│   │   ├── Validators/                # validação estrutural/consistência (FluentValidation)
│   │   ├── Program.cs
│   │   └── Fiscal.Api.csproj
│   │
│   ├── Fiscal.Core/                   # Domínio — POCOs, interfaces, regras de negócio puras (sem dependência de Unimake/EF/etc.)
│   │   ├── Entities/
│   │   │   ├── DocumentoFiscal.cs
│   │   │   ├── Tenant.cs
│   │   │   ├── Certificado.cs
│   │   │   └── EventoFiscal.cs
│   │   ├── Enums/
│   │   │   └── StatusDocumento.cs
│   │   ├── Interfaces/
│   │   │   ├── IEmissorFiscal.cs      # abstração — implementada pelo adapter Unimake
│   │   │   ├── IFilaEmissao.cs        # abstração — implementada por Hangfire hoje
│   │   │   ├── IRepositorioDocumentoFiscal.cs
│   │   │   └── ICertificadoStore.cs
│   │   ├── Services/
│   │   │   └── ValidadorConsistenciaFiscal.cs   # soma de itens, base x alíquota, etc.
│   │   └── Fiscal.Core.csproj
│   │
│   ├── Fiscal.Adapters.Unimake/       # Implementação concreta usando Unimake.DFe
│   │   ├── EmissorNFe.cs
│   │   ├── EmissorNFCe.cs
│   │   ├── EmissorNFSeNacional.cs
│   │   ├── ConfiguracaoUnimakeFactory.cs
│   │   └── Fiscal.Adapters.Unimake.csproj
│   │
│   ├── Fiscal.Pdf/                    # Geração de DANFE/DANFCe/DANFSe com QuestPDF
│   │   ├── Templates/
│   │   │   ├── DanfeDocument.cs
│   │   │   ├── DanfceDocument.cs      # layout bobina térmica
│   │   │   └── DanfseDocument.cs
│   │   ├── GeradorPdfService.cs
│   │   └── Fiscal.Pdf.csproj
│   │
│   ├── Fiscal.Persistence/            # EF Core + PostgreSQL
│   │   ├── FiscalDbContext.cs
│   │   ├── Migrations/
│   │   ├── Repositories/
│   │   ├── Criptografia/
│   │   │   └── EnvelopeEncryptionService.cs   # DEK/KEK dos certificados
│   │   └── Fiscal.Persistence.csproj
│   │
│   ├── Fiscal.Worker/                 # Processo separado — servidor Hangfire, jobs
│   │   ├── Jobs/
│   │   │   ├── ProcessarDocumentoJob.cs
│   │   │   ├── VarrerContingenciaJob.cs       # recurring job
│   │   │   ├── AlertarCertificadosVencendoJob.cs
│   │   │   └── EntregarWebhooksJob.cs         # drena outbox_webhooks
│   │   ├── Program.cs
│   │   └── Fiscal.Worker.csproj
│   │
│   └── Fiscal.Shared/                 # DTOs de contrato REST/webhook, constantes compartilhadas
│       ├── Contracts/
│       └── Fiscal.Shared.csproj
│
├── tests/
│   ├── Fiscal.Core.Tests/             # unitários — validação, idempotência, numeração
│   ├── Fiscal.Api.Tests/              # testes de integração com WebApplicationFactory + emissor mock
│   └── Fiscal.Integration.Tests/      # contra homologação real da SEFAZ — roda só no pipeline nightly
│
├── docker/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── docker-compose.override.yml    # ajustes locais de dev (hot reload, portas expostas, etc.)
│
├── .github/
│   ├── workflows/
│   │   ├── build-and-test.yml
│   │   └── docker-publish.yml
│   ├── ISSUE_TEMPLATE/
│   └── PULL_REQUEST_TEMPLATE.md
│
├── docs/
│   └── contrato-rest.md               # a documentação que já desenhamos
│
├── .env.example
├── LICENSE                            # MIT
├── README.md
├── CONTRIBUTING.md
├── SECURITY.md
└── CODE_OF_CONDUCT.md
```

### Regras de dependência entre projetos (importante manter disciplina nisso)
```
Fiscal.Api            → Fiscal.Core, Fiscal.Shared
Fiscal.Worker         → Fiscal.Core, Fiscal.Adapters.Unimake, Fiscal.Pdf, Fiscal.Persistence
Fiscal.Adapters.Unimake → Fiscal.Core   (implementa IEmissorFiscal)
Fiscal.Persistence     → Fiscal.Core   (implementa IRepositorioDocumentoFiscal, ICertificadoStore)
Fiscal.Pdf             → Fiscal.Core
Fiscal.Core            → (não depende de nada do projeto — só de bibliotecas puras como FluentValidation)
```
`Fiscal.Core` **nunca** referencia Unimake, EF Core, QuestPDF ou Hangfire diretamente — só interfaces. Isso é o que permite, por exemplo, trocar Hangfire por RabbitMQ no futuro (mencionado antes) sem tocar no domínio, e também é o que permite o **modo sandbox** existir: basta uma segunda implementação de `IEmissorFiscal` (`EmissorMock`) registrada via injeção de dependência quando `ModoSandbox=true`.

---

## 2. Dockerfile multi-stage

Uma imagem base compartilhada de build, gerando dois artefatos finais (`api` e `worker`) — evita duplicar a etapa de compilação/restore em dois Dockerfiles separados.

```dockerfile
# syntax=docker/dockerfile:1

# ---------- Estágio 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copia só os .csproj primeiro — aproveita cache de camada do Docker no restore
COPY src/Fiscal.Api/*.csproj src/Fiscal.Api/
COPY src/Fiscal.Core/*.csproj src/Fiscal.Core/
COPY src/Fiscal.Adapters.Unimake/*.csproj src/Fiscal.Adapters.Unimake/
COPY src/Fiscal.Pdf/*.csproj src/Fiscal.Pdf/
COPY src/Fiscal.Persistence/*.csproj src/Fiscal.Persistence/
COPY src/Fiscal.Worker/*.csproj src/Fiscal.Worker/
COPY src/Fiscal.Shared/*.csproj src/Fiscal.Shared/

RUN dotnet restore src/Fiscal.Api/Fiscal.Api.csproj \
 && dotnet restore src/Fiscal.Worker/Fiscal.Worker.csproj

# Agora copia o restante do código
COPY src/ src/

RUN dotnet publish src/Fiscal.Api/Fiscal.Api.csproj \
      -c Release -o /app/publish/api --no-restore

RUN dotnet publish src/Fiscal.Worker/Fiscal.Worker.csproj \
      -c Release -o /app/publish/worker --no-restore

# ---------- Estágio 2: runtime da API ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS api-final
WORKDIR /app

# Usuário não-root — boa prática de segurança, ainda mais numa API que lida com certificado digital
RUN addgroup -S fiscal && adduser -S fiscal -G fiscal
USER fiscal

COPY --from=build /app/publish/api .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Fiscal.Api.dll"]

# ---------- Estágio 3: runtime do Worker ----------
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS worker-final
WORKDIR /app

RUN addgroup -S fiscal && adduser -S fiscal -G fiscal
USER fiscal

COPY --from=build /app/publish/worker .

ENTRYPOINT ["dotnet", "Fiscal.Worker.dll"]
```

### Por que essa abordagem
- **Alpine** como base — imagem final bem menor, importante pro tempo de build/deploy de um projeto open source que outros vão clonar e rodar frequentemente.
- **Cache de camada no restore**: copiar só os `.csproj` antes do restante do código faz o Docker reaproveitar a camada de `dotnet restore` entre builds quando só o código muda (não as dependências) — builds ficam bem mais rápidos no dia a dia.
- **Usuário não-root** (`fiscal`) — relevante porque essa API mexe com certificado digital e chave de criptografia; rodar como root desnecessariamente é uma prática ruim de segurança que vale evitar desde o início.
- **Dois estágios finais (`api-final` / `worker-final`)** a partir do **mesmo estágio de build** — o `docker-compose.yml` que já desenhamos (`api` e `worker` como serviços separados) referencia esses dois `target`s do mesmo Dockerfile:

```yaml
services:
  api:
    build:
      context: .
      dockerfile: docker/Dockerfile
      target: api-final
    ...

  worker:
    build:
      context: .
      dockerfile: docker/Dockerfile
      target: worker-final
    ...
```

Isso mantém um único `Dockerfile` de verdade (fácil de manter) em vez de dois arquivos quase-duplicados.

---

## 3. `.dockerignore` (evita inflar o contexto de build)
```
**/bin/
**/obj/
**/.vs/
**/.vscode/
**/*.user
tests/
docs/
.git/
.github/
*.md
.env
```

---

## 4. Resumo — o planejamento está fechado

Com isso, o planejamento cobre: arquitetura assíncrona multi-tenant, modelo de dados PostgreSQL, idempotência e concorrência, contingência (SVC/EPEC/offline), homologação vs produção, fila (Hangfire), cache (Redis com escopo delimitado), autenticação (API Key por CNPJ), contrato REST completo, geração de PDF em base64 (QuestPDF, três templates distintos), decisão consciente de **não** calcular impostos, segurança/LGPD, auditoria, observabilidade, testes/CI-CD, backup/DR, licenciamento (MIT, alinhado à Unimake), governança open source, e agora a estrutura de código e containerização.

A partir daqui, o próximo passo natural já sai do "planejamento" e entra em "execução" — por exemplo, começar pelo `Fiscal.Core` (entidades + interfaces) e o `Fiscal.Adapters.Unimake` com a primeira emissão de NF-e em homologação ponta a ponta. Quer que eu comece por aí quando for a hora, ou prefere revisar/ajustar algo no planejamento antes?
