#!/usr/bin/env bash
set -euo pipefail
umask 077

if (( $# != 1 )) || [[ ! -d "$1" || -L "$1" ]]; then
  printf 'Uso: %s DIRECTORIO_DE_BACKUP_EXISTENTE\n' "$0" >&2
  exit 2
fi

backup_dir=$(cd -- "$1" && pwd)
volume=portal-libre-expresion-dev-commercial-documents
timestamp=$(date -u +%Y%m%dT%H%M%SZ)
partial=$(mktemp --tmpdir="$backup_dir" "portal-dev-documents-$timestamp-XXXXXX.partial")

docker volume inspect "$volume" >/dev/null
docker run --rm --read-only \
  -v "$volume:/source:ro" \
  -v "$backup_dir:/backup" \
  alpine:3.22 \
  sh -c "tar -czf /backup/$(basename "$partial") -C /source ."

if [[ ! -s "$partial" ]] || ! tar -tzf "$partial" >/dev/null; then
  printf 'Falló la verificación del backup documental: %s\n' "$partial" >&2
  exit 1
fi

final_file="${partial%.partial}.tar.gz"
mv -- "$partial" "$final_file"
printf 'Backup documental verificado: %s\n' "$final_file"
