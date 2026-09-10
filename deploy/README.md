# Deployment DEV

Este deployment ejecuta Vue compilado en Nginx, ASP.NET Core 10 y PostgreSQL 18
exclusivamente mediante contenedores. Nginx es el único servicio publicado y se
vincula al loopback de la VM en `127.0.0.1:8080`.

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

6. Levantar los tres servicios permanentes:

   ```bash
   sudo docker compose --env-file .env.dev up -d db api web
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
por Compose. Tampoco se debe eliminar manualmente `portal-dev-pgdata`.

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
ssh -p 22200 -N -L 5173:127.0.0.1:8080 administrador@173.201.39.180
```

Luego abrir `http://127.0.0.1:5173/`. Esta dirección es local al PC y no es un
dominio público.
