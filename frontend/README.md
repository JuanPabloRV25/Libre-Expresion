# Portal Libre Expresión — Frontend

Frontend de la Fase 1 construido con Vue 3, TypeScript, Vite, Vue Router y Pinia.

La interfaz reproduce la referencia funcional de Sites sin depender de su código y está integrada con la API ASP.NET Core real. La autenticación utiliza la sesión segura por cookie del backend, protección antifalsificación y permisos efectivos obtenidos desde `/api/auth/me`.

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

## Acceso en DEV

Las cuentas y credenciales de desarrollo se administran mediante la configuración protegida del entorno y no se documentan en el repositorio. Los usuarios normales se crean sin contraseña y reciben por correo un enlace temporal, válido durante 2 horas y para un solo uso, con el que establecen su contraseña antes de iniciar sesión. Un restablecimiento administrativo invalida la contraseña y las sesiones anteriores y envía un enlace nuevo. El Superadmin bootstrap conserva su flujo especial de primer ingreso.
