# Runbook de operación

Este documento cubre la operación actual de DEV y PROD. No contiene secretos. Los comandos deben ejecutarse con el usuario `administrador` en el servidor correspondiente.

## DEV

### Ubicación

`/home/administrador/portal-libre-expresion`

### Iniciar servicios

```bash
cd /home/administrador/portal-libre-expresion
sudo docker compose --env-file .env.dev up -d
```

### Detener servicios

```bash
cd /home/administrador/portal-libre-expresion
sudo docker compose --env-file .env.dev down
```

No agregar `-v`: esa opción elimina los volúmenes persistentes.

### Validar estado y healthcheck

```bash
cd /home/administrador/portal-libre-expresion
sudo docker compose --env-file .env.dev ps
curl -fsS http://127.0.0.1:8080/api/health
sudo ss -lntp | grep -E ':(22|8080|8025|5432|3000)\b' || true
```

La respuesta esperada del healthcheck es `{"status":"Healthy"}`.

### Acceso desde PowerShell

```powershell
ssh -N -p 22200 -i "$env:USERPROFILE\.ssh\libre_expresion_dev" `
  -L 127.0.0.1:5173:127.0.0.1:8080 `
  -L 127.0.0.1:8025:127.0.0.1:8025 `
  administrador@173.201.39.180
```

- Portal DEV: `http://127.0.0.1:5173/`
- Mailpit DEV: `http://127.0.0.1:8025/`

## PROD

### Ubicación

Enlace de release activo: `/opt/portal-le/portal-libre-expresion/current`

Configuración de entorno: `/opt/portal-le/config/prod.env`

### Verificar servicios

```bash
cd /opt/portal-le/portal-libre-expresion/current
sudo docker compose --env-file /opt/portal-le/config/prod.env -f docker-compose.prod.yml ps
```

### Reiniciar servicios

```bash
cd /opt/portal-le/portal-libre-expresion/current
sudo docker compose --env-file /opt/portal-le/config/prod.env -f docker-compose.prod.yml restart web api db mailpit
```

### Detener servicios

```bash
cd /opt/portal-le/portal-libre-expresion/current
sudo docker compose --env-file /opt/portal-le/config/prod.env -f docker-compose.prod.yml down
```

No agregar `-v`: esa opción elimina los volúmenes persistentes.

### Iniciar servicios

```bash
cd /opt/portal-le/portal-libre-expresion/current
sudo docker compose --env-file /opt/portal-le/config/prod.env -f docker-compose.prod.yml up -d db mailpit api web
```

### Validar healthcheck y puertos

```bash
curl -fsS http://127.0.0.1/api/health
sudo ss -lntp | grep -E ':(22|80|443|5432|8080|3000|8025)\b' || true
```

La respuesta esperada del healthcheck es `{"status":"Healthy"}`. PostgreSQL, API y Mailpit no deben quedar publicados directamente en el host.

### Acceso interno desde PowerShell

```powershell
ssh -N -p 1022 -i "$env:USERPROFILE\.ssh\libre_expresion_prod_20260913" `
  -L 127.0.0.1:8088:127.0.0.1:80 `
  administrador@173.201.39.180
```

- Portal PROD interno: `http://127.0.0.1:8088/`

La publicación externa continúa bloqueada hasta disponer de dominio, DNS, NAT 443, TLS y aprobación del cliente.
