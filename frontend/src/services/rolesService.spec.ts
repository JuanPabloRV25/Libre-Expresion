import { afterEach, describe, expect, it, vi } from 'vitest'
import { clearAntiforgeryToken } from '../api/httpClient'
import { permissionsService } from './permissionsService'
import { rolesService } from './rolesService'

const role = {
  id: '50365577-77db-4462-9c9c-e42f844f572f',
  name: 'Editor',
  description: 'Editor institucional',
  isActive: true,
  isSystem: false,
  userCount: 0,
  permissionCodes: ['roles.view'],
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

describe('roles and permissions real API contracts', () => {
  it('uses role CRUD, status and permission endpoints with antiforgery', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([role]))
      .mockResolvedValueOnce(jsonResponse(role))
      .mockResolvedValueOnce(jsonResponse({ token: 'csrf-role' }))
      .mockResolvedValueOnce(jsonResponse(role, 201))
      .mockResolvedValueOnce(jsonResponse({ ...role, name: 'Editor institucional' }))
      .mockResolvedValueOnce(jsonResponse({ ...role, isActive: false }))
      .mockResolvedValueOnce(jsonResponse({ ...role, permissionCodes: ['roles.view', 'users.view'] }))
    vi.stubGlobal('fetch', fetchMock)

    await rolesService.list({ search: 'editor', status: 'active' })
    await rolesService.get(role.id)
    await rolesService.create({ name: role.name, description: role.description })
    await rolesService.update(role.id, { name: 'Editor institucional', description: role.description })
    await rolesService.setStatus(role.id, false)
    await rolesService.setPermissions(role.id, ['roles.view', 'users.view'])

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/roles?search=editor&status=active', expect.anything())
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/roles', expect.objectContaining({ method: 'POST' }))
    expect(fetchMock).toHaveBeenNthCalledWith(5, `/api/roles/${role.id}`, expect.objectContaining({ method: 'PUT' }))
    expect(fetchMock).toHaveBeenNthCalledWith(6, `/api/roles/${role.id}/status`, expect.objectContaining({ method: 'PATCH' }))
    expect(fetchMock).toHaveBeenNthCalledWith(7, `/api/roles/${role.id}/permissions`, expect.objectContaining({ method: 'PUT' }))
  })

  it('loads the permission catalog from the real endpoint', async () => {
    const catalog = [{ id: '1', code: 'areas.view', module: 'areas', action: 'view', displayName: 'Ver áreas', description: null, isActive: true }]
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(catalog))
    vi.stubGlobal('fetch', fetchMock)

    await expect(permissionsService.list()).resolves.toEqual(catalog)
    expect(fetchMock).toHaveBeenCalledWith('/api/permissions', expect.objectContaining({ credentials: 'include' }))
  })
})
