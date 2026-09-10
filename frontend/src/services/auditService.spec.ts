import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/httpClient'
import { auditResultPresentation, safeAuditMetadataEntries } from './auditPresentation'
import { auditService, type AuditPage } from './auditService'

const auditPage: AuditPage = {
  items: [{
    id: '79fdccba-08d9-4358-a0fd-3a5d66a55f87',
    action: 'area.created',
    entityType: 'Area',
    entityId: 'd203a168-8151-4a80-ab30-1b646f244902',
    result: 'Success',
    occurredAt: '2026-09-09T20:00:00Z',
    correlationId: null,
    actor: { id: '8d7842f2-85cf-4f15-b4cb-feb2f8805495', name: 'Audit Administrator' },
    metadata: { name: 'Comunicaciones' },
    userAgent: null,
  }],
  page: 2,
  pageSize: 10,
  totalItems: 21,
  totalPages: 3,
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => vi.unstubAllGlobals())

describe('auditService real API contract', () => {
  it('loads a backend page with pagination and filters', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(jsonResponse(auditPage))
    vi.stubGlobal('fetch', fetchMock)

    await expect(auditService.list({
      page: 2,
      pageSize: 10,
      action: ' area.created ',
      entityType: 'Area',
      result: 'SUCCESS',
      actorUserId: '8d7842f2-85cf-4f15-b4cb-feb2f8805495',
      dateFrom: '2026-09-01T00:00:00.000Z',
      dateTo: '2026-09-30T23:59:59.999Z',
      search: 'd203a168',
    })).resolves.toEqual(auditPage)

    const requestedUrl = fetchMock.mock.calls[0][0] as string
    const requested = new URL(requestedUrl, 'http://portal.test')
    expect(requested.pathname).toBe('/api/audit')
    expect(Object.fromEntries(requested.searchParams)).toEqual({
      page: '2',
      pageSize: '10',
      action: 'area.created',
      entityType: 'Area',
      result: 'SUCCESS',
      actorUserId: '8d7842f2-85cf-4f15-b4cb-feb2f8805495',
      dateFrom: '2026-09-01T00:00:00.000Z',
      dateTo: '2026-09-30T23:59:59.999Z',
      search: 'd203a168',
    })
    expect(fetchMock).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ credentials: 'include' }),
    )
  })

  it.each([401, 403])('preserves an HTTP %i authorization error', async (status) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(jsonResponse({
      code: status === 401 ? 'unauthorized' : 'forbidden',
      message: 'Acceso restringido',
    }, status)))

    const error = await auditService.list().catch((reason: unknown) => reason)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(status)
  })

  it('renders only explicitly allowed metadata and controlled result states', () => {
    expect(safeAuditMetadataEntries({
      name: 'Área visible',
      passwordHash: 'never-render-this',
      secret: 'never-render-this-either',
    })).toEqual([{ key: 'name', label: 'Nombre', value: 'Área visible' }])
    expect(auditResultPresentation('Success')).toEqual({ label: 'SUCCESS', className: 'is-success' })
    expect(auditResultPresentation('FAILED').className).toBe('is-failed')
    expect(auditResultPresentation('denied').className).toBe('is-denied')
  })
})
