import { areas, emails, permissions, roles, users } from './data'
import type { Area, DemoEmail, PortalUser, Role } from '../types/models'

const wait = async <T>(value: T): Promise<T> => Promise.resolve(value)
const copyUser = (user: PortalUser): PortalUser => ({ ...user, roleIds: [...user.roleIds] })
const copyRole = (role: Role): Role => ({ ...role, permissionCodes: [...role.permissionCodes] })
const copyArea = (area: Area): Area => ({ ...area })

export const mockUsersProvider = {
  list: async () => wait(users.map(copyUser)),
  get: async (id: number) => wait(users.find((user) => user.id === id)).then((user) => user ? copyUser(user) : undefined),
  async save(input: Omit<PortalUser, 'id' | 'password' | 'mustChangePassword'> & { id?: number }) {
    if (input.id) {
      const current = users.find((user) => user.id === input.id)
      if (!current) return wait(undefined)
      Object.assign(current, input)
      return wait(copyUser(current))
    }
    const created: PortalUser = { ...input, id: Math.max(...users.map((user) => user.id)) + 1, password: input.document, mustChangePassword: true }
    users.unshift(created)
    emails.unshift({ id: `mail-welcome-${Date.now()}`, recipientName: `${created.firstName} ${created.lastName}`, recipientEmail: created.email, subject: 'Bienvenido(a) - Creación de usuario en Portal Libre Expresión', type: 'Creación de usuario', sentAt: 'Ahora', document: created.document })
    return wait(copyUser(created))
  },
  async toggleStatus(id: number) {
    const user = users.find((item) => item.id === id)
    if (!user) return wait(undefined)
    user.status = user.status === 'active' ? 'inactive' : 'active'
    return wait(copyUser(user))
  },
  async resetPassword(id: number) {
    const user = users.find((item) => item.id === id)
    if (!user) return wait(undefined)
    user.password = user.document
    user.mustChangePassword = true
    emails.unshift({ id: `mail-reset-${Date.now()}`, recipientName: `${user.firstName} ${user.lastName}`, recipientEmail: user.email, subject: 'Restablecimiento de contraseña - Portal Libre Expresión', type: 'Restablecimiento', sentAt: 'Ahora', document: user.document })
    return wait(copyUser(user))
  },
}

export const mockAreasProvider = {
  list: async () => wait(areas.map(copyArea)),
  async save(input: Omit<Area, 'id'> & { id?: number }) {
    if (input.id) {
      const current = areas.find((area) => area.id === input.id)
      if (!current) return wait(undefined)
      Object.assign(current, input)
      return wait(copyArea(current))
    }
    const created: Area = { ...input, id: Math.max(...areas.map((area) => area.id)) + 1 }
    areas.push(created)
    return wait(copyArea(created))
  },
  async toggleStatus(id: number) {
    const area = areas.find((item) => item.id === id)
    if (!area) return wait(undefined)
    area.status = area.status === 'active' ? 'inactive' : 'active'
    return wait(copyArea(area))
  },
}

export const mockRolesProvider = {
  list: async () => wait(roles.map(copyRole)),
  get: async (id: number) => wait(roles.find((role) => role.id === id)).then((role) => role ? copyRole(role) : undefined),
  async save(input: Omit<Role, 'id'> & { id?: number }) {
    if (input.id) {
      const current = roles.find((role) => role.id === input.id)
      if (!current) return wait(undefined)
      Object.assign(current, input)
      return wait(copyRole(current))
    }
    const created: Role = { ...input, id: Math.max(...roles.map((role) => role.id)) + 1 }
    roles.push(created)
    return wait(copyRole(created))
  },
  async setPermissions(id: number, permissionCodes: string[]) {
    const role = roles.find((item) => item.id === id)
    if (!role) return wait(undefined)
    role.permissionCodes = permissionCodes
    return wait(copyRole(role))
  },
}

export const mockPermissionsProvider = { list: async () => wait(permissions.map((permission) => ({ ...permission }))) }
export const mockEmailsProvider = {
  list: async (): Promise<DemoEmail[]> => wait(emails.map((email) => ({ ...email }))),
  get: async (id: string) => wait(emails.find((email) => email.id === id)).then((email) => email ? { ...email } : undefined),
}
