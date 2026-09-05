# Como contribuir

## Setup local

```bash
cp .env.example .env
# editar .env — gerar KEK com: openssl rand -base64 32
docker compose -f docker/docker-compose.yml --env-file .env up -d
```

A API fica em `http://localhost:8080`. Em dev, as migrations são aplicadas automaticamente na subida.

## Padrão de commits

Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.

## Antes de abrir PR

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Os três precisam passar — o CI também roda.

## Áreas que aceitam contribuição

- Adapters alternativos (substituir Unimake por outra lib, atrás de `IEmissorFiscal`).
- Templates de DANFE/DANFCe/DANFSe.
- Suporte a municípios NFS-e fora do padrão Nacional.
- Tradução da documentação para inglês.

## Regras de arquitetura (não negociáveis)

- **`Fiscal.Core` não referencia nada**: sem Unimake, sem EF, sem Hangfire, sem QuestPDF.
- **Adapters isolam terceiros**: tudo que fala com SEFAZ/Unimake vive em `Fiscal.Adapters.Unimake`,
  com interface em Core e mock para sandbox/testes.
- **Falha alto em vez de transmitir errado**: dados obrigatórios ausentes → `ErroNaoRecuperavelException`
  (ERRO_INTERNO), nunca payload "mais ou menos".
- **Migrations hand-written** precisam dos atributos `[DbContext]` e `[Migration("...")]` — sem eles o
  `Migrate()` não as aplica (bug que já custou um smoke test).
- **Sem cálculo de tributos na API** (decisão de escopo central — valores vêm calculados do ERP).
- Nos testes de integração (SQLite): **não use `OrderBy`/comparação de `DateTimeOffset` em EF** (não
  traduz) e confira os tipos da Unimake (`double` na maioria dos valores, `decimal` em qtd/unidade).
