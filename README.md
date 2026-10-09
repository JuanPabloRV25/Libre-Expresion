# Portal Libre Expresión

Portal web interno de Libre Expresión. El repositorio contiene el frontend,
backend, pruebas automatizadas y automatización técnica de despliegue.

## Estructura

- `frontend/`: Vue 3, TypeScript, Vite, Vue Router y Pinia.
- `backend/`: .NET 10 con API, Application, Domain e Infrastructure.
- `tests/`: pruebas unitarias y de integración.
- `deploy/`: Dockerfiles, Compose, Nginx y scripts operativos.
- `tools/`: herramientas técnicas versionadas.

La documentación funcional, de arquitectura, infraestructura y operación se
mantiene en la bóveda privada de Obsidian; no se duplica en este repositorio.

## Trabajo del equipo y de IA

Consulta [AGENTS.md](AGENTS.md) y los documentos oficiales del alcance antes de
implementar. En el workspace Windows, este checkout está en
`Libre Expresión/portal-libre-expresion` y la bóveda en
`Libre Expresión/Portal_Libre_Expresion/Modulos`.

La nota `DEV-000 - Organización del proyecto y flujo de trabajo` define ramas,
worktrees, evidencias y entregas. Las copias históricas están fuera del código,
en `Archivo/`, y conservan cambios pendientes. El repositorio compartido es
[JuanPabloRV25/Libre-Expresion](https://github.com/JuanPabloRV25/Libre-Expresion),
configurado como `origin`; la rama compartida es `main`.

## DEV con Docker

La configuración privada se guarda en `.env.dev`, ignorada por Git. No se deben
incluir credenciales reales en `.env.example` ni en archivos versionados.

```bash
bash deploy/scripts/stack.sh dev config
bash deploy/scripts/stack.sh dev build
bash deploy/scripts/stack.sh dev up
bash deploy/scripts/stack.sh dev ps
```

No ejecutar `docker compose down -v`: los datos y las claves de protección se
conservan en volúmenes persistentes.

## Validación

El backend se valida con el SDK oficial de .NET 10 y el frontend con Node.js 24.

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp \
  -e NUGET_PACKAGES=/tmp/.nuget/packages \
  -v "$PWD:/workspace" -w /workspace/backend \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test PortalLibreExpresion.sln

docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e npm_config_cache=/tmp/.npm \
  -v "$PWD/frontend:/workspace" -w /workspace \
  node:24-bookworm-slim npm run lint

docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e npm_config_cache=/tmp/.npm \
  -v "$PWD/frontend:/workspace" -w /workspace \
  node:24-bookworm-slim npm run test

docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e npm_config_cache=/tmp/.npm \
  -v "$PWD/frontend:/workspace" -w /workspace \
  node:24-bookworm-slim npm run build
```

PROD se opera exclusivamente con la configuración protegida del servidor y el
procedimiento vigente documentado en Obsidian.
