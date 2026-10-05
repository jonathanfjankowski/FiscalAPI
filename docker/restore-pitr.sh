#!/usr/bin/env bash
# Restore pontual (PITR) a partir de um base backup + arquivo de WAL.
# RPO alvo: ≤ 5 min (docs/backup-dr.md).
#
# Uso:
#   ./restore-pitr.sh <backup.dump> <timestamp-alvo> <destino>
#   ./restore-pitr.sh backups/20260906T030000.dump "2026-09-06 14:30:00" /tmp/restore-pitr
#
# Requer: base backup gerado com pg_dump (docker/backup.sh) e o volume
# wal_archive (arquivos .partial inclusos) preservado do incidente.
set -euo pipefail

BACKUP="${1:?Uso: restore-pitr.sh <backup.dump> <timestamp-alvo> <destino>}"
ALVO="${2:?Informe o timestamp alvo (ex.: \"2026-09-06 14:30:00\")}"
DESTINO="${3:?Informe o diretório de destino do cluster restaurado}"

echo "==> Restore do base backup: ${BACKUP}"
pg_restore -d postgres "${BACKUP}" --role=fiscal || true

echo "==> Criando recovery.signal para PITR até '${ALVO}'"
touch "${PGDATA:-/var/lib/postgresql/data}/recovery.signal"

cat >> "${PGDATA:-/var/lib/postgresql/data}/postgresql.auto.conf" <<EOF
restore_command = 'cp /wal_archive/%f %p'
recovery_target_time = '${ALVO}'
recovery_target_action = 'promote'
EOF

echo "==> Inicie o Postgres e valide (fumaça em docs/backup-dr.md):"
echo "    SELECT pg_is_in_recovery();  -- deve voltar f após o promote"
echo "    ./restore.sh ${BACKUP}       -- conferir integridade do base backup"
