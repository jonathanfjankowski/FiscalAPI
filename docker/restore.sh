#!/usr/bin/env bash
# Restore de um backup pg_dump da FiscalAPI. SEMPRE testar num Postgres limpo
# antes de um desastre real (ver docs/backup-dr.md).
# Uso: docker/restore.sh backups/20260903T030000.dump
set -euo pipefail

cd "$(dirname "$0")/.."
[ -f .env ] && set -a && . ./.env && set +a

PG_USER="${POSTGRES_USER:-fiscal}"
PG_DB="${POSTGRES_DB:-fiscal}"
DUMP="${1:?uso: docker/restore.sh backups/<timestamp>.dump}"
[ -f "$DUMP" ] || { echo "arquivo não encontrado: $DUMP"; exit 1; }

echo "[restore] ATENÇÃO: isto recarrega o banco $PG_DB a partir de $DUMP"
read -r -p "Digite o nome do banco para confirmar: " CONFIRM
[ "$CONFIRM" = "$PG_DB" ] || { echo "abortado."; exit 1; }

docker compose -f docker/docker-compose.yml exec -T postgres \
  psql -U "$PG_USER" -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$PG_DB' AND pid <> pg_backend_pid();"
docker compose -f docker/docker-compose.yml exec -T postgres \
  dropdb -U "$PG_USER" --if-exists "$PG_DB"
docker compose -f docker/docker-compose.yml exec -T postgres \
  createdb -U "$PG_USER" "$PG_DB"
docker compose -f docker/docker-compose.yml exec -T postgres \
  pg_restore -U "$PG_USER" -d "$PG_DB" --no-owner --no-privileges < "$DUMP"

echo "[restore] ok. Fumaça: health/ready + GET de um documento + carregar certificado (prova a KEK)."
