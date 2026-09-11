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
  roleIds: string[]
  status: UserStatus
  mustChangePassword: boolean
  demoProfile?: 'superadmin' | 'limited' | 'standard' | 'first-login' | 'inactive'
}

export interface AuthenticatedUserArea {
  id: string
  name: string
}

export interface AuthenticatedUser {
  id: string
  documentNumber: string
  firstName: string
  lastName: string
  email: string
  area: AuthenticatedUserArea | null
  roles: string[]
  permissions: string[]
  isActive: boolean
  mustChangePassword: boolean
}

export type LoginResult =
  | { status: 'success'; user: AuthenticatedUser }
  | { status: 'first-login'; user: AuthenticatedUser }
  | { status: 'inactive' }
  | { status: 'locked' }
  | { status: 'invalid' }
