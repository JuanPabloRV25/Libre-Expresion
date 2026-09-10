import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '../api/httpClient'
import { useAuthStore } from './auth'

const service = vi.hoisted(() => ({
  login: vi.fn(),
  me: vi.fn(),
  logout: vi.fn(),
  changeRequiredPassword: vi.fn(),
  changePassword: vi.fn(),
}))

vi.mock('../services/authService', () => ({ authService: service }))

const user = {
  id: '1d1aaac2-aab5-43a4-90d8-0d2e10ac68dc',
  documentNumber: '123456789',
  firstName: 'Grace',
  lastName: 'Hopper',
  email: 'grace@example.test',
  area: { id: '4bfabc9c-dabe-41b8-8af0-8e539fb9991b', name: 'Tecnología' },
  roles: ['Superadmin'],
  permissions: ['users.view', 'roles.view'],
  isActive: true,
  mustChangePassword: false,
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

describe('authentication store', () => {
  it('rebuilds session and effective permissions from /me', async () => {
    service.me.mockResolvedValue(user)
    const auth = useAuthStore()

    await auth.initialize()

    expect(auth.currentUser).toEqual(user)
    expect(auth.permissionCodes).toEqual(['users.view', 'roles.view'])
    expect(auth.hasPermission('roles.view')).toBe(true)
  })

  it('treats 401 as anonymous but does not turn 403 into logout', async () => {
    service.me.mockRejectedValueOnce(new ApiError(401))
    const anonymous = useAuthStore()
    await expect(anonymous.initialize()).resolves.toBeUndefined()
    expect(anonymous.isAuthenticated).toBe(false)

    setActivePinia(createPinia())
    service.me.mockRejectedValueOnce(new ApiError(403, 'password_change_required'))
    await expect(useAuthStore().initialize()).rejects.toMatchObject({ status: 403 })
  })

  it('keeps mandatory-login identity only in memory and changes without temporary password', async () => {
    const temporaryUser = { ...user, permissions: [], mustChangePassword: true }
    service.login.mockResolvedValue({ status: 'first-login', user: temporaryUser })
    service.changeRequiredPassword.mockResolvedValue({ succeeded: true })
    const auth = useAuthStore()

    await auth.login(user.documentNumber, 'Temporary!1')
    expect(auth.currentUser?.mustChangePassword).toBe(true)
    expect(auth.permissionCodes).toEqual([])
    await expect(auth.completeFirstLogin('Definitive!1', 'Definitive!1')).resolves.toBe(true)
    expect(service.changeRequiredPassword).toHaveBeenCalledWith('Definitive!1', 'Definitive!1')
    expect(auth.currentUser).toBeNull()
  })

  it('logs out through the API and clears the in-memory identity', async () => {
    service.me.mockResolvedValue(user)
    service.logout.mockResolvedValue({ succeeded: true })
    const auth = useAuthStore()
    await auth.initialize()

    await auth.logout()

    expect(service.logout).toHaveBeenCalledOnce()
    expect(auth.currentUser).toBeNull()
  })
})
