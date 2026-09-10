import { afterEach, describe, expect, it, vi } from 'vitest'
import { clearAntiforgeryToken } from '../api/httpClient'
import { areasService } from './areasService'

const area = {
  id: '5cc6642f-d8cf-4754-bec2-65de64c9863b',
  name: 'Comunicaciones',
  description: 'Área institucional',
  isActive: true,
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

describe('areasService real API contract', () => {
  it('uses the list filters and the protected mutation endpoints', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse([area]))
      .mockResolvedValueOnce(jsonResponse({ token: 'csrf-token' }))
      .mockResolvedValueOnce(jsonResponse(area, 201))
      .mockResolvedValueOnce(jsonResponse({ ...area, name: 'Comunicaciones Estratégicas' }))
      .mockResolvedValueOnce(jsonResponse({ ...area, isActive: false }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(areasService.list({ search: 'comunica', status: 'active' })).resolves.toEqual([area])
    await areasService.create({ name: area.name, description: area.description })
    await areasService.update(area.id, { name: 'Comunicaciones Estratégicas', description: area.description })
    await areasService.setStatus(area.id, false)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/areas?search=comunica&status=active', expect.objectContaining({ credentials: 'include' }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/areas', expect.objectContaining({ method: 'POST' }))
    expect(fetchMock).toHaveBeenNthCalledWith(4, `/api/areas/${area.id}`, expect.objectContaining({ method: 'PUT' }))
    expect(fetchMock).toHaveBeenNthCalledWith(5, `/api/areas/${area.id}/status`, expect.objectContaining({ method: 'PATCH' }))
    expect(fetchMock.mock.calls.slice(2).every((call) => (call[1] as RequestInit).headers && ((call[1] as RequestInit).headers as Record<string, string>)['X-XSRF-TOKEN'] === 'csrf-token')).toBe(true)
  })
})
