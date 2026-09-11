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

## Contraseñas y notificaciones DEV

Los usuarios creados desde el Portal no reciben una contraseña temporal. Se
crean sin `PasswordHash` y reciben un enlace personal para establecerla. Un
restablecimiento administrativo invalida la contraseña y las sesiones
anteriores y envía un enlace nuevo. Ambos enlaces usan tokens estándar de
ASP.NET Core Identity, no se guardan en la base de datos ni en auditoría, duran
2 horas y quedan invalidados al utilizarse o al emitirse un restablecimiento
posterior. El Superadmin bootstrap conserva su mecanismo especial existente.
La pantalla retira el token de la barra al cargar y la configuración web evita
registrar queries o referencias que puedan contenerlo. Una cuenta inactiva puede
establecer su contraseña con el enlace, pero no puede iniciar sesión hasta ser
activada por un administrador.

Si una entrega falla, el usuario permanece en estado seguro y pendiente. Un
administrador con `users.reset_password` puede utilizar **Enviar
restablecimiento** en el detalle del usuario para generar un enlace nuevo.

La misma implementación `IEmailSender`/SMTP admite dos modos de DEV, elegidos
exclusivamente mediante `.env.dev`. Variables disponibles:

- `Email__Enabled`: habilita la entrega.
- `Email__Host` y `Email__Port`: servidor y puerto SMTP.
- `Email__UseTls`: `true` para STARTTLS explícito.
- `Email__UseSsl`: `true` para SSL/TLS desde el inicio de la conexión.
- `Email__Username` y `Email__Password`: credenciales SMTP; ambas deben estar
  presentes o ambas vacías. Si se utilizan credenciales, `UseTls` o `UseSsl`
  debe estar habilitado para impedir autenticación SMTP en texto plano.
- `Email__FromAddress` y `Email__FromName`: remitente.
- `Email__PortalBaseUrl`: origen público DEV usado para construir el enlace.
- `Email__EnvironmentLabel`: etiqueta visible del entorno.

`Email__UseTls` y `Email__UseSsl` son mutuamente excluyentes.

### SMTP real con una cuenta personal

Completar en el archivo ignorado `.env.dev` los valores entregados por el
proveedor de correo; no escribirlos en archivos versionados:

```dotenv
Email__Enabled=true
Email__Host=<host-smtp-del-proveedor>
Email__Port=<puerto-smtp-del-proveedor>
Email__UseTls=<true-o-false>
Email__UseSsl=<true-o-false>
Email__Username=<usuario-smtp>
Email__Password=<contraseña-o-app-password>
Email__FromName=Portal Libre Expresión
Email__FromAddress=<correo-personal-remitente>
Email__PortalBaseUrl=http://127.0.0.1:5173
Email__EnvironmentLabel=DESARROLLO
```

Usar exactamente el modo, host y puerto documentados por el proveedor. El
repositorio no contiene ni presupone credenciales reales.

### Volver a Mailpit

Restaurar estas variables en `.env.dev`:

```dotenv
Email__Enabled=true
Email__Host=mailpit
Email__Port=1025
Email__UseTls=false
Email__UseSsl=false
Email__Username=
Email__Password=
Email__FromName=Portal Libre Expresión
Email__FromAddress=notificaciones@libreexpresion.test
Email__PortalBaseUrl=http://127.0.0.1:5173
Email__EnvironmentLabel=DESARROLLO
```

La UI de Mailpit está disponible en `http://127.0.0.1:8025/` desde el PC. Sus
mensajes se almacenan en el volumen `portal-libre-expresion-dev-mailpit-data`.
Las claves de Data Protection que validan los tokens se conservan en
`portal-libre-expresion-dev-api-keys`.

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
