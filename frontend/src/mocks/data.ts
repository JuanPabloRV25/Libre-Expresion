import type { Area, Permission, PortalUser, Role } from '../types/models'

export const permissions: Permission[] = [
  { code: 'areas.view', name: 'Ver áreas', module: 'Áreas', description: 'Consulta de áreas o dependencias.' },
  { code: 'areas.create', name: 'Crear áreas', module: 'Áreas', description: 'Registro de nuevas áreas o dependencias.' },
  { code: 'areas.edit', name: 'Editar áreas', module: 'Áreas', description: 'Actualización de áreas o dependencias.' },
  { code: 'areas.activate', name: 'Activar o inactivar áreas', module: 'Áreas', description: 'Control del estado de las áreas.' },
  { code: 'users.view', name: 'Ver usuarios', module: 'Usuarios', description: 'Consulta del directorio interno.' },
  { code: 'users.create', name: 'Crear usuarios', module: 'Usuarios', description: 'Registro de nuevas personas.' },
  { code: 'users.edit', name: 'Editar usuarios', module: 'Usuarios', description: 'Actualización de información general.' },
  { code: 'users.activate', name: 'Activar o inactivar usuarios', module: 'Usuarios', description: 'Control del estado de acceso.' },
  { code: 'users.assign_roles', name: 'Asignar roles', module: 'Usuarios', description: 'Asociación de responsabilidades.' },
  { code: 'users.reset_password', name: 'Restablecer contraseñas', module: 'Usuarios', description: 'Generación de acceso temporal.' },
  { code: 'roles.view', name: 'Ver roles', module: 'Roles', description: 'Consulta de roles parametrizables.' },
  { code: 'roles.create', name: 'Crear roles', module: 'Roles', description: 'Creación de responsabilidades.' },
  { code: 'roles.edit', name: 'Editar roles', module: 'Roles', description: 'Actualización de roles.' },
  { code: 'roles.activate', name: 'Activar o inactivar roles', module: 'Roles', description: 'Control del estado de los roles.' },
  { code: 'roles.assign_permissions', name: 'Asignar permisos', module: 'Roles', description: 'Configuración de capacidades.' },
  { code: 'permissions.view', name: 'Ver permisos', module: 'Permisos', description: 'Consulta del catálogo técnico.' },
  { code: 'audit.view', name: 'Ver auditoría', module: 'Auditoría', description: 'Consulta de eventos de auditoría cuando el módulo esté disponible.' },
]

export const areas: Area[] = [
  { id: 1, name: 'Tecnología', description: 'Servicios tecnológicos y soporte interno.', status: 'active' },
  { id: 2, name: 'Administrativa', description: 'Gestión administrativa y soporte corporativo.', status: 'active' },
  { id: 3, name: 'Comercial', description: 'Gestión de clientes y actividad comercial.', status: 'active' },
  { id: 4, name: 'Producción', description: 'Operación y producción de servicios.', status: 'active' },
  { id: 5, name: 'Gerencia', description: 'Dirección y seguimiento organizacional.', status: 'active' },
  { id: 6, name: 'Nómina', description: 'Administración de nómina.', status: 'active' },
]

const allCodes = permissions.map((permission) => permission.code)

export const roles: Role[] = [
  { id: 1, name: 'Administrador del sistema', description: 'Administra el acceso de la plataforma.', status: 'active', permissionCodes: allCodes },
  { id: 2, name: 'Jefe Comercial', description: 'Rol configurable para liderazgo comercial.', status: 'active', permissionCodes: ['users.view', 'users.edit'] },
  { id: 3, name: 'Asesor Comercial', description: 'Acceso base de asesores comerciales.', status: 'active', permissionCodes: [] },
  { id: 4, name: 'Producción', description: 'Rol inicial del equipo de producción.', status: 'active', permissionCodes: [] },
  { id: 5, name: 'Jefe Administrativo', description: 'Rol configurable del área administrativa.', status: 'active', permissionCodes: ['users.view'] },
  { id: 6, name: 'Asistente Administrativa', description: 'Gestión operativa administrativa limitada.', status: 'active', permissionCodes: ['users.view', 'users.create', 'users.edit', 'users.activate', 'users.assign_roles', 'roles.view', 'roles.create'] },
  { id: 7, name: 'Responsable de Nómina', description: 'Rol inicial para responsables de nómina.', status: 'active', permissionCodes: [] },
  { id: 8, name: 'Gerencia / Dirección', description: 'Rol inicial para consulta de dirección.', status: 'active', permissionCodes: [] },
]

export const users: PortalUser[] = [
  { id: 1, documentType: 'CC', document: '10000001', firstName: 'Laura', lastName: 'Mendoza', email: 'laura.mendoza@demo.local', areaId: 1, roleIds: ['1'], status: 'active', password: '10000001', mustChangePassword: false, demoProfile: 'superadmin' },
  { id: 2, documentType: 'CC', document: '10000002', firstName: 'Diego', lastName: 'Torres', email: 'diego.torres@demo.local', areaId: 2, roleIds: ['6'], status: 'active', password: '10000002', mustChangePassword: false, demoProfile: 'limited' },
  { id: 3, documentType: 'CC', document: '10000003', firstName: 'Mariana', lastName: 'Ruiz', email: 'mariana.ruiz@demo.local', areaId: 3, roleIds: ['3'], status: 'active', password: '10000003', mustChangePassword: false, demoProfile: 'standard' },
  { id: 4, documentType: 'CC', document: '10000004', firstName: 'Andrés', lastName: 'Pardo', email: 'andres.pardo@demo.local', areaId: 3, roleIds: ['2', '3'], status: 'active', password: '10000004', mustChangePassword: true, demoProfile: 'first-login' },
  { id: 5, documentType: 'CC', document: '10000005', firstName: 'Camilo', lastName: 'Vargas', email: 'camilo.vargas@demo.local', areaId: 2, roleIds: ['5'], status: 'inactive', password: '10000005', mustChangePassword: false, demoProfile: 'inactive' },
  { id: 6, documentType: 'CC', document: '10000006', firstName: 'Natalia', lastName: 'Cárdenas', email: 'natalia.cardenas@demo.local', areaId: 4, roleIds: ['4'], status: 'active', password: '10000006', mustChangePassword: false },
  { id: 7, documentType: 'CC', document: '10000007', firstName: 'Valentina', lastName: 'Rojas', email: 'valentina.rojas@demo.local', areaId: 6, roleIds: ['7'], status: 'active', password: '10000007', mustChangePassword: false },
  { id: 8, documentType: 'CC', document: '10000008', firstName: 'Sebastián', lastName: 'López', email: 'sebastian.lopez@demo.local', areaId: 5, roleIds: ['8'], status: 'active', password: '10000008', mustChangePassword: false },
]
