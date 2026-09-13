# Validación operativa final — 2026-09-13

Validación ejecutada sin apagar ambientes, modificar código de aplicación, cambiar arquitectura ni realizar despliegues.

## DEV

- Host: `devle`.
- Checkout: `/home/administrador/portal-libre-expresion`.
- Docker Compose: API activa; PostgreSQL activo y saludable; frontend activo; Mailpit activo y saludable.
- Healthcheck: `http://127.0.0.1:8080/api/health` respondió `{"status":"Healthy"}`.
- Puertos del host observados: SSH `22`, portal `127.0.0.1:8080` y Mailpit `127.0.0.1:8025`.
- PostgreSQL y puerto de desarrollo `3000` no estaban publicados en el host.
- Acceso SSH validado como `administrador` mediante el puerto externo `22200`.

Resultado: **DEV funcionando y disponible mediante túnel SSH.**

## PROD

- Host: `portal-le-prod-01`.
- Release efectivo: `v1.0.5`.
- Imágenes activas: web `portal-le-web:1.0.5`, API `portal-le-api:1.0.4`, PostgreSQL `postgres:18` y Mailpit `axllent/mailpit:v1.30.6`.
- Docker Compose: web y API activos; PostgreSQL y Mailpit activos y saludables.
- Healthcheck: `http://127.0.0.1/api/health` respondió `{"status":"Healthy"}`.
- Puertos del host observados: SSH `22` y web `127.0.0.1:80`.
- PostgreSQL `5432`, API `8080`, frontend de desarrollo `3000`, Mailpit `8025` y HTTPS `443` no estaban publicados directamente en el host.
- Acceso SSH validado como `administrador` mediante clave Ed25519 exclusiva y puerto externo `1022`.
- SSH mantiene deshabilitados el acceso de `root` y la autenticación por contraseña.

Resultado: **PROD funcionando internamente y disponible mediante túnel SSH.** La salida pública continúa en **NO-GO** hasta resolver dominio, DNS, SMTP, NAT 443, TLS, usuarios iniciales, backup externo y aprobación del cliente.

## Continuidad

No se ejecutaron comandos de parada. DEV y PROD quedaron activos al finalizar la validación.
