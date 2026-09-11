# Portal Libre Expresión

Portal web interno para Libre Expresión.

## Fase actual

Fase 1:

- Autenticación.
- Usuarios.
- Áreas.
- Roles.
- Permisos.
- Auditoría básica.
- Notificaciones de acceso y seguridad.

## Arquitectura

- Frontend: Vue 3
- Backend: ASP.NET Core 10 / C# 14
- ORM: Entity Framework Core 10
- Base de datos: PostgreSQL 18
- Reverse proxy: Nginx
- Contenedores: Docker
- Orquestación: Docker Compose
- Correo DEV: SMTP + Mailpit (exclusivo del entorno de desarrollo)
- Sistema operativo: Ubuntu Server

## Estructura

- `frontend/` — Aplicación Web Vue.
- `backend/` — Backend ASP.NET Core.
- `tests/` — Pruebas automatizadas.
- `deploy/` — Configuración de despliegue.

## Flujo de contraseñas en DEV

Los usuarios creados desde el Portal se almacenan inicialmente sin contraseña.
ASP.NET Core Identity genera un token de restablecimiento protegido, asociado al
usuario y con una vida útil de 2 horas. El enlace enviado por correo permite
establecer la contraseña una sola vez; después, el inicio de sesión continúa
utilizando número de documento y contraseña.

El restablecimiento administrativo invalida las sesiones y la contraseña
anteriores, genera un token nuevo y envía otro enlace. Si el correo falla, la
operación permanece registrada y un administrador con `users.reset_password`
puede reenviar un enlace desde el detalle del usuario.

El Superadmin inicial conserva deliberadamente el bootstrap basado en documento
y el cambio obligatorio del primer ingreso. Esta es la única excepción al flujo
por enlace.

Por continuidad administrativa, el único Superadmin activo no puede usar el
restablecimiento administrativo; debe cambiar su propia contraseña desde Perfil
o promover primero otro Superadmin activo.

## Deployment DEV

El procedimiento reproducible con Docker Compose, las operaciones one-shot de
migración/provisión y el acceso mediante túnel SSH están documentados en
[`deploy/README.md`](deploy/README.md).

Con el túnel conjunto activo, el Portal está disponible en
`http://127.0.0.1:5173/` y la bandeja Mailpit DEV en
`http://127.0.0.1:8025/`. Producción no utilizará Mailpit: el proveedor
corporativo de Libre Expresión está pendiente de definición.
