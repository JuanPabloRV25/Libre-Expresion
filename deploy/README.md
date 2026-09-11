# Deployment DEV

Este deployment ejecuta Vue compilado en Nginx, ASP.NET Core 10, PostgreSQL 18 y
Mailpit exclusivamente mediante contenedores. Nginx y la UI de Mailpit se vinculan
al loopback de la VM; API, PostgreSQL y el SMTP de Mailpit no publican puertos.

## Preparación inicial

1. Copiar `.env.example` como `.env.dev` y reemplazar los valores de ejemplo. El
   archivo real está ignorado por Git y debe conservar permisos restrictivos.
2. Crear una vez el volumen de PostgreSQL si se trata de una VM nueva:

   ```bash
   sudo docker volume create portal-dev-pgdata
   ```

3. Construir e iniciar únicamente PostgreSQL:

   ```bash
   sudo docker compose --env-file .env.dev build
   sudo docker compose --env-file .env.dev up -d db
   sudo docker compose --env-file .env.dev ps
   ```

4. Aplicar migraciones de forma explícita:

   ```bash
   sudo docker compose --env-file .env.dev --profile tools run --rm migrate \
     database update \
     --project backend/Portal.Infrastructure/Portal.Infrastructure.csproj \
     --startup-project backend/Portal.Api/Portal.Api.csproj \
     --configuration Release \
     --no-build
   ```

5. Solo para una base nueva, provisionar el Superadmin una vez:

   ```bash
   sudo docker compose --env-file .env.dev --profile tools run --rm provision
   ```

   La operación es idempotente y no restablece la contraseña de un usuario ya
   existente. Los arranques normales fuerzan `DATABASE_SEEDING_ENABLED=false`.

6. Levantar los cuatro servicios permanentes:

   ```bash
   sudo docker compose --env-file .env.dev up -d db mailpit api web
   ```

## Operación habitual

```bash
# Iniciar
sudo docker compose --env-file .env.dev up -d

# Estado
sudo docker compose --env-file .env.dev ps

# Logs
sudo docker compose --env-file .env.dev logs -f

# Detener sin borrar datos
sudo docker compose --env-file .env.dev down

# Reconstruir
sudo docker compose --env-file .env.dev up -d --build
```

No ejecutar `docker compose down -v`: esa opción elimina volúmenes administrados
por Compose. Tampoco se deben eliminar manualmente `portal-dev-pgdata` ni
`portal-libre-expresion-dev-mailpit-data`.

## Notificaciones DEV

La API envía mediante SMTP a Mailpit usando las variables `Email__*` de
`.env.dev`: `Enabled`, `Host`, `Port`, `UseTls`, `Username`, `Password`,
`FromName`, `FromAddress`, `PortalBaseUrl` y `EnvironmentLabel`. Los valores DEV
usan el host Docker `mailpit`, puerto interno `1025`, sin TLS ni credenciales,
remitente ficticio `notificaciones@libreexpresion.test` y etiqueta
`DESARROLLO`. No se deben documentar ni versionar secretos reales.

Mailpit es exclusivo de DEV. Su UI permite revisar los correos capturados en
`http://127.0.0.1:8025/` desde el PC. Los mensajes se almacenan en SQLite dentro
del volumen nombrado `portal-libre-expresion-dev-mailpit-data`. Producción usará
el proveedor corporativo pendiente de definición por Libre Expresión.

## Inspección de migraciones

```bash
sudo docker compose --env-file .env.dev --profile tools run --rm migrate \
  migrations list \
  --project backend/Portal.Infrastructure/Portal.Infrastructure.csproj \
  --startup-project backend/Portal.Api/Portal.Api.csproj \
  --configuration Release \
  --no-build

sudo docker compose --env-file .env.dev --profile tools run --rm migrate \
  migrations has-pending-model-changes \
  --project backend/Portal.Infrastructure/Portal.Infrastructure.csproj \
  --startup-project backend/Portal.Api/Portal.Api.csproj \
  --configuration Release \
  --no-build
```

## Acceso desde un PC Windows

Mantener abierta esta sesión de PowerShell:

```powershell
ssh -p 22200 -N \
  -L 5173:127.0.0.1:8080 \
  -L 8025:127.0.0.1:8025 \
  administrador@173.201.39.180
```

Luego abrir el Portal en `http://127.0.0.1:5173/` y Mailpit en
`http://127.0.0.1:8025/`. Ambas direcciones son locales al PC y no son dominios
públicos.
