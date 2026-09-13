# Operación de accesos y credenciales

Este documento identifica los accesos sin registrar contraseñas, contenidos de secretos ni claves privadas. Las claves privadas, archivos `.env` reales y certificados privados no deben subirse a Git.

## Accesos de servidores

| Ambiente | Host de acceso | Usuario | Puerto externo | Método | Clave privada en estación autorizada |
|---|---|---|---:|---|---|
| DEV | `173.201.39.180` | `administrador` | 22200 | SSH mediante clave | `%USERPROFILE%\.ssh\libre_expresion_dev` |
| PROD | `173.201.39.180` | `administrador` | 1022 | SSH mediante clave Ed25519 exclusiva de PROD | `%USERPROFILE%\.ssh\libre_expresion_prod_20260913` |

Los puertos externos son publicados por la infraestructura perimetral; `sshd` escucha en el puerto 22 dentro de cada servidor.

## Permisos necesarios

- La clave privada debe ser legible únicamente por su propietario autorizado.
- `administrador` requiere acceso SSH y pertenencia al grupo con privilegios `sudo` para operar Docker, consultar puertos y administrar servicios.
- En PROD no se permite autenticación SSH por contraseña ni acceso directo de `root`.
- Las claves privadas no se copian a los servidores, al repositorio ni a canales de mensajería.

## Ubicación segura de secretos

| Elemento | Ubicación | Protección |
|---|---|---|
| Configuración DEV | `/home/administrador/portal-libre-expresion/.env.dev` | Modo `0600`, propietario `administrador` |
| Configuración PROD | `/opt/portal-le/config/prod.env` | Modo `0600`, propietario `root` |
| Claves SSH privadas | Carpeta `%USERPROFILE%\.ssh\` de la estación autorizada | ACL exclusiva del usuario; fuera de Git |
| Material auxiliar protegido de PROD | `%USERPROFILE%\.ssh\portal_le_prod_*.dpapi` | Protección DPAPI del usuario de Windows; fuera de Git |

Los valores SMTP corporativos, certificados privados y credenciales definitivas permanecen pendientes y deberán almacenarse en el mecanismo seguro aprobado por TI, nunca en Git.

## Responsables

- Libre Expresión / cliente: autorizar usuarios, custodios y salida a producción.
- TI cliente: administrar DNS, SMTP y custodiar los secretos que le correspondan.
- Infraestructura: mantener NAT, firewall, servidor y disponibilidad de acceso.
- Administrador técnico autorizado: operar contenedores y aplicar el procedimiento de acceso sin divulgar claves.
