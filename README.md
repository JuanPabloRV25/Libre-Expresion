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
- Sistema operativo: Ubuntu Server

## Estructura

- `frontend/` — Aplicación Web Vue.
- `backend/` — Backend ASP.NET Core.
- `tests/` — Pruebas automatizadas.
- `deploy/` — Configuración de despliegue.
