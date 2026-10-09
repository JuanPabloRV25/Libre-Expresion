import { afterEach, describe, expect, it, vi } from 'vitest'
import { inputMoney, money, reportService } from './reportService'
import { postBlob, postJson, putJson } from '../../../api/httpClient'
import type { SalesReport } from './types'
vi.mock('../../../api/httpClient', () => ({ API_BASE_URL: '/api', getJson: vi.fn(), mutateForm: vi.fn(), postBlob: vi.fn(), postJson: vi.fn(), putJson: vi.fn() }))
afterEach(() => { vi.restoreAllMocks(); vi.clearAllMocks() })
describe('report money fields', () => {
  it('keeps a blank different from a confirmed zero and accepts credits', () => {
    const event = (value: string) => ({ target: { value } }) as unknown as Event
    expect(inputMoney(event(''))).toBeNull()
    expect(inputMoney(event('0'))).toBe(0)
    expect(inputMoney(event('-594000'))).toBe(-594000)
    expect(money(null)).toBe('Sin completar')
    expect(money(0)).not.toBe('Sin completar')
  })
})
describe('review request contracts', () => {
  const report = { id: 'report', name: 'Reporte', version: 7 } as SalesReport
  it('sends an explicit unchanged N/A decision and an independent FACTURA patch atomically', async () => {
    const decision = { caseId: 'case', findingIds: ['op'], action: 'keep_na' as const }
    const factura = { key: 'row', factura: '000123' }
    await reportService.savePrepared(report, [factura], [decision])
    expect(putJson).toHaveBeenCalledWith('/commercial/reports/report/prepared', { version: 7, name: 'Reporte', rowEdits: [factura], decisions: [decision] })
    expect(decision).toEqual({ caseId: 'case', findingIds: ['op'], action: 'keep_na' })
  })
  it('uses preview separately from save and preserves the request after an error', async () => {
    const edits = [{ key: 'row', factura: 'Local' }]
    vi.mocked(postJson).mockRejectedValueOnce(new Error('Versión desactualizada'))
    await expect(reportService.previewPrepared(report, edits)).rejects.toThrow('Versión desactualizada')
    expect(postJson).toHaveBeenCalledWith('/commercial/reports/report/prepared/preview', { version: 7, name: 'Reporte', rowEdits: edits, decisions: [] })
    expect(putJson).not.toHaveBeenCalled()
    expect(edits).toEqual([{ key: 'row', factura: 'Local' }])
  })
  it('downloads draft and final through different contracts using the saved version', async () => {
    await reportService.exportDraft(report)
    await reportService.export(report)
    expect(postBlob).toHaveBeenNthCalledWith(1, '/commercial/reports/report/export/draft', { version: 7 })
    expect(postBlob).toHaveBeenNthCalledWith(2, '/commercial/reports/report/export', { version: 7 })
  })
  it('approval is an explicit request with the saved version', async () => {
    await reportService.approve(report)
    expect(postJson).toHaveBeenCalledWith('/commercial/reports/report/approve', { version: 7 })
    expect(putJson).not.toHaveBeenCalled()
  })
})
