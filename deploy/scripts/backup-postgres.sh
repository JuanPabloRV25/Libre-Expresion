#!/usr/bin/env bash
set -euo pipefail
umask 077

if (( $# != 2 )) || [[ "$1" != dev && "$1" != prod ]] \
  || [[ ! -d "$2" || -L "$2" ]]; then
  printf 'Uso: %s dev|prod DIRECTORIO_DE_BACKUP_EXISTENTE\n' "$0" >&2
  exit 2
fi

environment=$1
backup_dir=$(cd -- "$2" && pwd)
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_dir=$(cd -- "$script_dir/../.." && pwd)
if [[ "$environment" == dev ]]; then
  env_file="$repo_dir/.env.dev"
  compose_file="$repo_dir/deploy/dev/compose.yaml"
else
  env_file="$repo_dir/deploy/prod/.env"
  compose_file="$repo_dir/deploy/prod/compose.yaml"
fi

if [[ ! -f "$env_file" ]]; then
  printf 'Falta el archivo privado de parámetros para %s.\n' "$environment" >&2
  exit 1
fi

compose() {
  docker compose --env-file "$env_file" -f "$compose_file" "$@"
}

compose config --quiet
config_json=$(compose config --format json)
db_user=$(jq -er '.services.db.environment.POSTGRES_USER' <<< "$config_json")
db_name=$(jq -er '.services.db.environment.POSTGRES_DB' <<< "$config_json")

timestamp=$(date -u +%Y%m%dT%H%M%SZ)
backup_file=$(mktemp --tmpdir="$backup_dir" "portal-$environment-$timestamp-XXXXXX.partial")

if ! compose exec -T db pg_dump -U "$db_user" -d "$db_name" -Fc > "$backup_file"; then
  printf 'Falló pg_dump; el archivo parcial quedó en %s.\n' "$backup_file" >&2
  exit 1
fi

if [[ ! -s "$backup_file" ]] || ! compose exec -T db pg_restore -l < "$backup_file" > /dev/null; then
  printf 'Falló la verificación; el archivo parcial quedó en %s.\n' "$backup_file" >&2
  exit 1
fi

final_file="${backup_file%.partial}.dump"
mv -- "$backup_file" "$final_file"
printf 'Backup verificado: %s\n' "$final_file"
