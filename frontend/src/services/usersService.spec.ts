import { afterEach, describe, expect, it, vi } from 'vitest'
import { clearAntiforgeryToken } from '../api/httpClient'
import { usersService } from './usersService'

const user = {
  id: '27babfe4-279b-4eac-a8b2-fc8dbcb584f4',
  documentNumber: '7000000010',
  firstName: 'Ana',
  lastName: 'Pruebas',
  email: 'ana@example.test',
  area: null,
  advisorCode: null,
  roles: [],
  isActive: true,
  mustChangePassword: true,
  createdAt: '2026-09-09T20:00:00Z',
  updatedAt: '2026-09-09T20:00:00Z',
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  clearAntiforgeryToken()
  vi.unstubAllGlobals()
})

describe('usersService real API contract', () => {
  it('uses list, CRUD, roles, status and reset endpoints with antiforgery', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([user]))
      .mockResolvedValueOnce(jsonResponse(user))
      .mockResolvedValueOnce(jsonResponse({ token: 'csrf-user' }))
      .mockResolvedValueOnce(jsonResponse({ user, notificationStatus: 'sent' }, 201))
      .mockResolvedValueOnce(jsonResponse({ ...user, firstName: 'Ana María' }))
      .mockResolvedValueOnce(jsonResponse({ ...user, roles: [{ id: 'role', name: 'Editor', isActive: true, isSystem: false }] }))
      .mockResolvedValueOnce(jsonResponse({ ...user, isActive: false }))
      .mockResolvedValueOnce(jsonResponse({ passwordReset: true, notificationStatus: 'failed' }))
    vi.stubGlobal('fetch', fetchMock)

    await usersService.list({ search: 'ana', status: 'active', areaId: 'area', roleId: 'role' })
    await usersService.get(user.id)
    await expect(usersService.create({ ...user, areaId: null, roleIds: [], isActive: true })).resolves.toMatchObject({ notificationStatus: 'sent' })
    await usersService.update(user.id, { firstName: 'Ana María', lastName: user.lastName, email: user.email, areaId: null, advisorCode: null })
    await usersService.setRoles(user.id, ['role'])
    await usersService.setStatus(user.id, false)
    await expect(usersService.resetPassword(user.id)).resolves.toMatchObject({ notificationStatus: 'failed' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/users?search=ana&status=active&areaId=area&roleId=role', expect.anything())
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/users', expect.objectContaining({ method: 'POST' }))
    expect(fetchMock).toHaveBeenNthCalledWith(5, `/api/users/${user.id}`, expect.objectContaining({ method: 'PUT' }))
    expect(fetchMock).toHaveBeenNthCalledWith(6, `/api/users/${user.id}/roles`, expect.objectContaining({ method: 'PUT' }))
    expect(fetchMock).toHaveBeenNthCalledWith(7, `/api/users/${user.id}/status`, expect.objectContaining({ method: 'PATCH' }))
    expect(fetchMock).toHaveBeenNthCalledWith(8, `/api/users/${user.id}/reset-password`, expect.objectContaining({ method: 'POST' }))
  })
})
