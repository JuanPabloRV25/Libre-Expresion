#!/usr/bin/env bash
set -euo pipefail

if (( $# != 1 )) || [[ "$1" != https://* ]]; then
  printf 'Uso: %s https://dominio-publico\n' "$0" >&2
  exit 2
fi

base_url=${1%/}
curl --fail --silent --show-error --max-time 10 \
  "$base_url/" > /dev/null
curl --fail --silent --show-error --max-time 10 \
  "$base_url/api/health" > /dev/null
curl --fail --silent --show-error --max-time 10 \
  "$base_url/api/ready" > /dev/null

printf 'La web y la base de datos responden por HTTPS.\n'
printf 'Completar manualmente login, permisos y entrega Gmail con un destinatario de prueba.\n'
