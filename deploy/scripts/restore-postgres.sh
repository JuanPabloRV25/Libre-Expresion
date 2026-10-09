#!/usr/bin/env bash
set -euo pipefail

if (( $# != 3 )) || [[ "$1" != dev && "$1" != prod ]] \
  || [[ ! -f "$2" || -L "$2" ]] \
  || [[ ! "$3" =~ ^[a-z][a-z0-9_]{0,62}$ ]]; then
  printf 'Uso: %s dev|prod ARCHIVO.dump nombre_base_nueva\n' "$0" >&2
  exit 2
fi

environment=$1
backup_file=$2
target_db=$3
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
live_db=$(jq -er '.services.db.environment.POSTGRES_DB' <<< "$config_json")

if [[ "$target_db" == "$live_db" || "$target_db" == "postgres" ]]; then
  printf 'La restauración requiere una base nueva, distinta de la base activa.\n' >&2
  exit 1
fi

if compose exec -T db psql -U "$db_user" -d postgres -tAc \
  "SELECT 1 FROM pg_database WHERE datname = '$target_db'" | grep -qx 1; then
  printf 'La base destino ya existe; no se sobrescribirá.\n' >&2
  exit 1
fi

if ! compose exec -T db pg_restore -l < "$backup_file" > /dev/null; then
  printf 'El archivo no es un backup pg_dump válido.\n' >&2
  exit 1
fi

compose exec -T db createdb -U "$db_user" "$target_db"
if ! compose exec -T db pg_restore --no-owner --no-privileges \
  -U "$db_user" -d "$target_db" < "$backup_file"; then
  printf 'Restauración incompleta en %s; se conserva para diagnóstico.\n' "$target_db" >&2
  exit 1
fi

printf 'Restauración completada en la base aislada %s.\n' "$target_db"
