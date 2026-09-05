#!/usr/bin/env bash
# Backup lógico diário do Postgres da FiscalAPI (pg_dump custom format).
# Uso: docker/backup.sh            (requer .env com POSTGRES_USER/POSTGRES_DB)
# Cron sugerido no host: 0 3 * * *  cd /caminho/FiscalAPI && docker/backup.sh >> backups/backup.log 2>&1
set -euo pipefail

cd "$(dirname "$0")/.."
[ -f .env ] && set -a && . ./.env && set +a

PG_USER="${POSTGRES_USER:-fiscal}"
PG_DB="${POSTGRES_DB:-fiscal}"
DIR="backups"
STAMP="$(date +%Y%m%dT%H%M%S)"
TARGET="$DIR/$STAMP.dump"

mkdir -p "$DIR"

echo "[backup] dump de $PG_DB → $TARGET"
docker compose -f docker/docker-compose.yml exec -T postgres \
  pg_dump -U "$PG_USER" -d "$PG_DB" -Fc --no-owner --no-privileges > "$TARGET"

# Retenção: 7 diários + 4 semanais (aproximação por mtime).
find "$DIR" -name '*.dump' -mtime +7 -delete
echo "[backup] ok ($(du -h "$TARGET" | cut -f1)); backups restantes: $(ls -1 "$DIR"/*.dump | wc -l)"
