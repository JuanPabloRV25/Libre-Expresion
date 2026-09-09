# Portal Libre Expresión — Frontend

Frontend de la Fase 1 construido con Vue 3, TypeScript, Vite, Vue Router y Pinia.

La interfaz reproduce la referencia funcional de Sites sin depender de su código. Los datos y la autenticación son simulados detrás de servicios y proveedores reemplazables; la única integración HTTP real de esta fase es `GET /health` del backend.

## Ejecución con Docker

Desde `frontend/`:

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -e npm_config_cache=/tmp/.npm \
  -v "$PWD:/workspace" \
  -w /workspace \
  node:24-bookworm-slim npm install

docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -e npm_config_cache=/tmp/.npm \
  -v "$PWD:/workspace" \
  -w /workspace \
  node:24-bookworm-slim npm run build
```

Los comandos disponibles son `npm run dev`, `npm run lint`, `npm run test` y `npm run build`.

## Configuración

Copie `.env.example` a `.env` cuando necesite personalizar la integración:

- `VITE_API_BASE_URL`: prefijo utilizado por el cliente HTTP; por defecto `/api`.
- `VITE_DEV_PROXY_TARGET`: destino del proxy de desarrollo; por defecto `http://portal-api:8080`.

## Perfiles de demostración

En todos los perfiles la contraseña inicial coincide con el número de documento.

- `10000001`: Superadmin.
- `10000002`: Administrador limitado.
- `10000003`: Usuario interno estándar.
- `10000004`: Primer ingreso con cambio obligatorio.
- `10000005`: Cuenta inactiva.

Estos datos existen únicamente para la simulación local y deberán reemplazarse por la API real en una fase posterior.
