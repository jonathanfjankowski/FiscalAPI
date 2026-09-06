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
| WAL archiving / PITR | RPO ≤ 5 min | **ativo no compose (1.9.0-alpha)**: `archive_mode=on` + volume `wal_archive` (journal a cada 5 min, `archive_timeout=300`); restore com `docker/restore-pitr.sh` |

## Backup

```bash
# no host (cron sugerido: 0 3 * * *)
docker/backup.sh   # usa POSTGRES_USER/DB do .env; grava em ./backups/<timestamp>.dump
```

O dump contém PFX **cifrados** (envelope) — é seguro transportar, desde que a
KEK não viaje junto.

## Restore pontual (PITR — RPO ≤ 5 min)

```bash
# o volume wal_archive guarda os journals (inclusive .partial a cada 5 min)
docker/restore-pitr.sh backups/20260906T030000.dump "2026-09-06 14:30:00" /tmp/restore-pitr
```

O script restaura o base backup, escreve `recovery.signal` +
`recovery_target_time` e promove o cluster ao ponto solicitado. Após o
promote, rodar a fumaça (documento + certificado carregável) do checklist.

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
      *(ops — fora do código)*
- [x] `ADMIN_JWT_SECRET` e `Certificados:ChaveMestraKEK` fora de `.env` —
      **suportado (1.9.0-alpha)**: convenção `CHAVE_FILE` (Docker secrets /
      K8s mounted files) via `AddFileSecrets()`; execução é do ambiente.
- [x] Postgres com `pgcrypto` — **suportado (1.9.0-alpha)**:
      `docker/postgres-init/01-extensions.sql` cria a extensão no primeiro
      boot; volume criptografado continua sendo opção do host.
- [x] Role da aplicação sem UPDATE/DELETE em `auditoria` — **migration
      `EnforceAuditoriaAppendOnly` aplica o REVOKE no startup** (verificado).
- [ ] Backup da KEK em cofre separado do backup do banco. *(ops)*
- [ ] Teste de restore executado e documentado (data + resultado). *(ops —
      agora também há `docker/restore-pitr.sh` para testar o PITR)*
- [ ] Política de retenção LGPD: XMLs fiscais têm guarda legal (5+ anos);
      dados sensíveis de titulares não devem entrar em campos livres
      (justificativas/motivos) — revisar no consumo dos webhooks. *(ops)*
