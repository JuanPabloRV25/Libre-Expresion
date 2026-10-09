import { afterEach, describe, expect, it, vi } from 'vitest'
import { clearAntiforgeryToken } from '../../../api/httpClient'
import { productionOrdersService } from './productionOrdersService'
import type { CommercialOrderInput } from './types'

const orderId = 'f97815bc-a21c-4122-8890-5a28918c584a'

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

describe('productionOrdersService API contract', () => {
  it('keeps the Commercial namespace and applies list filters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    await productionOrdersService.list({ search: ' Caja  ', status: 'inProduction' })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/commercial/production-orders?search=Caja&status=inProduction',
      expect.objectContaining({ credentials: 'include' }),
    )
  })

  it('uses CSRF protection for lifecycle transitions', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ token: 'commercial-csrf' }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }))
    vi.stubGlobal('fetch', fetchMock)

    await productionOrdersService.submitForReview(orderId, 1, "selected-reviewer")
    await productionOrdersService.approveReview(orderId, 2, 'PED-2048')
    await productionOrdersService.receive(orderId, 3)
    await productionOrdersService.complete(orderId, 4)
    await productionOrdersService.duplicate(orderId)
    await productionOrdersService.cancel(orderId, 5, 'Solicitud del cliente')

    const mutations = fetchMock.mock.calls.slice(1)
    expect(JSON.parse(String((mutations[0][1] as RequestInit).body))).toEqual({ version: 1, reviewerUserId: 'selected-reviewer' })
    expect(mutations.map((call) => call[0])).toEqual([
      `/api/commercial/production-orders/${orderId}/submit-for-review`,
      `/api/commercial/production-orders/${orderId}/submit`,
      `/api/commercial/production-orders/${orderId}/receive`,
      `/api/commercial/production-orders/${orderId}/complete`,
      `/api/commercial/production-orders/${orderId}/duplicate`,
      `/api/commercial/production-orders/${orderId}/cancel`,
    ])
    expect(mutations.every((call) => {
      const headers = (call[1] as RequestInit).headers as Record<string, string>
      return (call[1] as RequestInit).method === 'POST' && headers['X-XSRF-TOKEN'] === 'commercial-csrf'
    })).toBe(true)
    expect(JSON.parse(String((mutations[1][1] as RequestInit).body))).toEqual({ version: 2, customerOrderNumber: 'PED-2048' })
  })

  it('sends the reviewed commercial draft only when the quotation is confirmed', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ token: 'commercial-csrf' }))
      .mockResolvedValueOnce(jsonResponse({ id: orderId }, 201))
    vi.stubGlobal('fetch', fetchMock)
    // Synthetic quotation data; no customer records.
    const commercial = {
      quotationNumber: '99999-1',
      clientName: 'CLIENTE FICTICIO SAS',
      productName: 'PRODUCTO FICTICIO',
      quantity: 1_000,
      unitValue: 10,
      workType: 'unspecified',
      dieType: 'none',
      qualityCertificateMode: 'none',
      technicalSheetMode: 'none',
      materials: [],
      printLines: [],
      finishes: [],
    } as unknown as CommercialOrderInput

    await productionOrdersService.importQuotation(new File(['quotation'], 'Cotizacion.pdf'), 0, 2, commercial, orderId)

    const request = fetchMock.mock.calls[1][1] as RequestInit
    const body = request.body as FormData
    expect(request.method).toBe('POST')
    expect(body.get('itemIndex')).toBe('0')
    expect(body.get('optionIndex')).toBe('2')
    expect(body.get('relatedOrderId')).toBe(orderId)
    expect(JSON.parse(String(body.get('commercial')))).toMatchObject({
      quotationNumber: '99999-1',
      clientName: 'CLIENTE FICTICIO SAS',
      quantity: 1_000,
    })
  })
})
