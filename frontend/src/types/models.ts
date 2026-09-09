export type UserStatus = 'active' | 'inactive'

export interface Permission {
  code: string
  name: string
  module: 'Áreas' | 'Usuarios' | 'Roles' | 'Permisos' | 'Auditoría'
  description: string
}

export interface Role {
  id: number
  name: string
  description: string
  status: UserStatus
  permissionCodes: string[]
}

export interface Area {
  id: number
  name: string
  description: string
  status: UserStatus
}

export interface PortalUser {
  id: number
  documentType: 'CC' | 'CE' | 'TI' | 'PPT'
  document: string
  firstName: string
  lastName: string
  email: string
  areaId: number
  roleIds: number[]
  status: UserStatus
  password: string
  mustChangePassword: boolean
  demoProfile?: 'superadmin' | 'limited' | 'standard' | 'first-login' | 'inactive'
}

export interface DemoEmail {
  id: string
  recipientName: string
  recipientEmail: string
  subject: string
  type: 'Creación de usuario' | 'Restablecimiento' | 'Cambio de contraseña'
  sentAt: string
  document?: string
}

export type LoginResult =
  | { status: 'success'; user: PortalUser }
  | { status: 'first-login'; user: PortalUser }
  | { status: 'inactive' }
  | { status: 'invalid' }
