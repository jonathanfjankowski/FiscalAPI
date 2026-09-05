# Backup / Disaster Recovery (Postgres)

> Estado viva deste documento: revisar a cada release. Última revisão: 2026-09-03 (Fase 5).

## O que é crítico

O banco Postgres concentra **todo** o estado: tenants, API keys (hash PBKDF2),
certificados A1 (cifrados com envelope AES-GCM — DEK por registro + KEK),
documentos e XMLs autorizados, eventos, outbox de webhooks e auditoria
(append-only). Perder o banco = perder a operação fiscal.

A **KEK** (`Certificados:ChaveMestraKEK`) NÃO fica no banco — vem de variável
de ambiente/secret manager. Backup do banco sem a KEK não decifra certificados:
**backup da KEK é separado e mais protegido** (cofre/secret manager com
versionamento e acesso restrito). Sem ela, os PFX cifrados são irrecuperáveis.

## Estratégia mínima viável (alpha)

| Item | Meta | Como |
|---|---|---|
| Backup lógico diário | RPO ≤ 24 h | `docker/backup.sh` (`pg_dump` custom format) em cron do host |
| Retenção | 7 diários + 4 semanais | o script roda `find -mtime` e apaga backups antigos |
| Restore testado | trimestral | `docker/restore.sh` num Postgres limpo + `dotnet test` de fumaça |
| RTO alvo | ≤ 1 h | subir Postgres + `restore.sh` + API/Worker (docker compose) |
| WAL archiving / PITR | RPO ≤ 5 min | **pendente** (Fase 5 avançada): `archive_command` + volume dedicado |

## Backup

```bash
# no host (cron sugerido: 0 3 * * *)
docker/backup.sh   # usa POSTGRES_USER/DB do .env; grava em ./backups/<timestamp>.dump
```

O dump contém PFX **cifrados** (envelope) — é seguro transportar, desde que a
KEK não viaje junto.

## Restore (testar antes de precisar)

```bash
docker/restore.sh backups/20260903T030000.dump
# depois:
curl http://localhost:8080/health/ready
# fumaça: GET /v1/documentos-fiscais/{id} de um doc autorizado + carregar um
# certificado (GET /v1/certificados) — prova a KEK correta e o envelope íntegro.
```

## Checklist de desastre

1. Provisionar Postgres limpo (mesma versão major).
2. `restore.sh` com o dump mais recente.
3. Subir API + Worker (`docker compose up -d`) com a MESMA KEK do backup.
4. `health/ready` + fumaça (documento + certificado carregável).
5. Confirmar fila: jobs CONTINGENCIA/PENDENTE retomam sozinhos (Hangfire
   storage é o mesmo Postgres restaurado); outbox de webhooks drena sozinha.

## Hardening de produção (checklist de go-live)

- [ ] TLS terminado no proxy + HSTS no proxy (a API escuta HTTP interno).
- [ ] `ADMIN_JWT_SECRET` e `Certificados:ChaveMestraKEK` fora de `.env`
      (secret manager: Docker secrets / Azure Key Vault / Vault).
- [ ] Postgres com `pgcrypto`/volume criptografado (criptografia em repouso).
- [ ] Role da aplicação sem UPDATE/DELETE em `auditoria` (ver migration
      `EnforceAuditoriaAppendOnly`).
- [ ] Backup da KEK em cofre separado do backup do banco.
- [ ] Teste de restore executado e documentado (data + resultado).
- [ ] Política de retenção LGPD: XMLs fiscais têm guarda legal (5+ anos);
      dados sensíveis de titulares não devem entrar em campos livres
      (justificativas/motivos) — revisar no consumo dos webhooks.
