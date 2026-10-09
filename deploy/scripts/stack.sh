#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )); then
  printf 'Uso: %s dev|prod config|build|db|up|ps|logs|down|migrate|provision\n' "$0" >&2
  exit 2
fi

environment=$1
action=$2
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_dir=$(cd -- "$script_dir/../.." && pwd)

case "$environment" in
  dev)
    compose_file="$repo_dir/deploy/dev/compose.yaml"
    env_file="$repo_dir/.env.dev"
    ;;
  prod)
    compose_file="$repo_dir/deploy/prod/compose.yaml"
    env_file="$repo_dir/deploy/prod/.env"
    ;;
  *)
    printf 'Entorno inválido: %s\n' "$environment" >&2
    exit 2
    ;;
esac

if [[ ! -f "$env_file" ]]; then
  printf 'Falta el archivo privado de parámetros para %s.\n' "$environment" >&2
  exit 1
fi

compose() {
  docker compose --env-file "$env_file" -f "$compose_file" "$@"
}

compose config --quiet

case "$action" in
  config)
    printf 'Configuración %s válida.\n' "$environment"
    ;;
  build)
    compose --profile tools build api web migrate
    ;;
  db)
    compose up -d --no-build db
    ;;
  up)
    compose up -d --no-build db api web
    ;;
  ps)
    compose ps
    ;;
  logs)
    compose logs --tail=100
    ;;
  down)
    compose down
    ;;
  migrate)
    compose --profile tools run --rm migrate database update \
      --project backend/Portal.Infrastructure/Portal.Infrastructure.csproj \
      --startup-project backend/Portal.Api/Portal.Api.csproj \
      --configuration Release --no-build
    ;;
  provision)
    compose --profile tools run --rm provision
    ;;
  *)
    printf 'Acción inválida: %s\n' "$action" >&2
    exit 2
    ;;
esac
