import { describe, expect, it } from 'vitest'
import { changeComparison, presentReportChanges, searchChangeEntry } from './reportChangePresentation'
import type { ReportChange, ReportReview, SalesDetail, SalesGroup } from './types'

function change(overrides: Partial<ReportChange>): ReportChange {
  return { id: 'change', rowKey: 'row', kind: 'op_rectified', field: 'NUMERO OP', before: ['4500'], after: '4500 / 4501 / 4502', reason: 'Referencia histórica comprobada.', detailIds: ['a', 'b', 'c'], historyIds: ['historical-a'], manual: false, ...overrides }
}
const details: SalesDetail[] = [
  { id: 'a', sourceId: 'file', sourceFile: 'Manager.xlsx', sheet: 'Ventas', sourceRow: 8, number: '42', date: '', client: 'Clínica Norte', term: '', rawAmount: 100, detail: 'Primera entrega', line: 'Impresos', seller: '', managerOp: '4500', documentId: '', matchKey: '', selectedOpId: null, selectedOpNumber: '', selectedProduct: '', usePortalCode: false, manualGroup: null, reviewed: false, excluded: false, reason: '' },
]
const row: SalesGroup = { key: 'row', documentId: '', number: '42', date: '', op: '4500 / 4501 / 4502', product: '', client: 'Clínica Norte', term: '', amount: 100, factura: '', line: 'Impresos', seller: '', details: ['Primera entrega', 'Segunda entrega', 'Tercera entrega'], detailIds: ['a', 'b', 'c'], issues: [], modified: false }

describe('human report change presentation', () => {
  it('hides file preparation and excluded columns from history without changing the audit', () => {
    const changes = [
      change({ id: 'columns', rowKey: null, kind: 'columns_removed', field: 'Columnas', before: ['IVA', 'TOTAL', 'NUMERO_REM'], after: '', detailIds: [], historyIds: [] }),
      change({ id: 'file-finding', rowKey: null, kind: 'observation', field: 'Archivo', before: [], after: '', reason: 'Una fila requiere revisión.' }),
      change({ id: 'file-decision', rowKey: null, kind: 'manual_edit', field: 'Archivo', manual: true }),
      change({ id: 'row-finding', kind: 'observation' }),
    ]
    const original = structuredClone(changes)
    const result = presentReportChanges(changes, details, [row])
    expect(result.automatic).toHaveLength(1)
    expect(result.automatic[0]!.changes.map(item => item.id)).toEqual(['row-finding'])
    expect(result.manual).toEqual([])
    expect(result.retained).toEqual([])
    expect(changes).toEqual(original)
  })
  it('groups the full automatic transformation into one entry for the result row', () => {
    const changes = [
      change({ id: 'group', kind: 'rows_consolidated', field: 'Filas', before: ['fila 8', 'fila 9', 'fila 10'], after: 'Una fila' }),
      change({ id: 'ops' }),
      change({ id: 'details', kind: 'details_combined', field: 'DETALLE', before: row.details, after: row.details.join('\n') }),
      change({ id: 'amount', kind: 'amount_kept_once', field: 'VALOR_BRUT', before: ['100', '100', '100'], after: '100' }),
      change({ id: 'retained', rowKey: 'unchanged', kind: 'source_kept', after: '6000', before: ['6000'], detailIds: ['d'] }),
    ]
    const result = presentReportChanges(changes, details, [row])
    expect(result.automatic).toHaveLength(1)
    expect(result.automatic[0]!.title).toBe('OP 4500 / 4501 / 4502')
    expect(result.automatic[0]!.subtitle).toBe('Clínica Norte · Impresos')
    expect(result.automatic[0]!.events[0]!.actions.map(action => action.text)).toEqual([
      'Reunimos 3 registros del Informe de ventas en una sola fila.',
      'Actualizamos los números OP con el Informe de OPs.',
      'Unimos los 3 detalles en una celda.',
      expect.stringContaining('Conservamos el importe una sola vez:'),
    ])
    expect(result.automatic[0]!.changes).toEqual(changes.slice(0, 4))
    expect(result.retained[0]!.changes).toEqual([changes[4]])
    expect(result.automatic[0]!.events[0]!.actions[1]!.comparison).toEqual({ before: ['4500'], after: '4500 / 4501 / 4502' })
  })
  it('keeps every ledger record and all evidence available across the three views', () => {
    const changes = [change({ id: 'auto' }), change({ id: 'kept', kind: 'source_kept' }), change({ id: 'manual', manual: true, kind: 'manual_edit' })]
    const result = presentReportChanges(changes, details, [row])
    const presented = [...result.automatic, ...result.manual, ...result.retained].flatMap(entry => entry.changes)
    expect(presented.map(change => change.id).sort()).toEqual(changes.map(change => change.id).sort())
    expect(presented.find(change => change.id === 'auto')!.historyIds).toEqual(['historical-a'])
    expect(presented.find(change => change.id === 'auto')!.detailIds).toEqual(['a', 'b', 'c'])
    expect(changes).toHaveLength(3)
  })
  it('shows manual confirmations as decisions without inventing a before/after difference', () => {
    const confirmation = change({ kind: 'manual_edit', field: 'VALOR_BRUT', before: ['100'], after: '100.00', manual: true })
    const result = presentReportChanges([confirmation], details, [row])
    expect(changeComparison(confirmation)).toBeNull()
    expect(result.manual[0]!.events[0]!.actions[0]!).toMatchObject({ text: 'Confirmaste el valor de importe sin modificarlo.', status: 'confirmation', comparison: null })
  })
  it('distinguishes a deliberate empty cell from an unresolved value', () => {
    const empty = change({ kind: 'manual_edit', field: 'FACTURA', before: ['Manual-123'], after: '', manual: true })
    const result = presentReportChanges([empty], details, [row])
    expect(result.manual[0]!.events[0]!.actions[0]!).toMatchObject({ text: 'Dejaste la celda de factura vacía.', comparison: { before: ['Manual-123'], after: 'Celda vacía' } })
  })
  it('groups a saved edit into one event and shows later versions first', () => {
    const changes = [
      change({ id: 'invoice', kind: 'manual_edit', field: 'FACTURA', before: [''], after: 'F-1', manual: true, reportVersion: 2, occurredAt: '2026-10-07T15:00:00Z' }),
      change({ id: 'client', kind: 'manual_edit', field: 'NOMBRE', before: ['Clínica Norte'], after: 'Clínica Sur', manual: true, reportVersion: 2, occurredAt: '2026-10-07T15:00:00Z' }),
      change({ id: 'restore', kind: 'manual_restore', field: 'FACTURA', before: ['F-1'], after: '', manual: true, reportVersion: 3, occurredAt: '2026-10-07T16:00:00Z' }),
    ]
    const result = presentReportChanges(changes, details, [row])
    expect(result.manual).toHaveLength(1)
    expect(result.manual[0]!.events).toHaveLength(2)
    expect(result.manual[0]!.events[0]!.label).toContain('Versión 3')
    expect(result.manual[0]!.events[1]!.actions).toHaveLength(2)
  })
  it('labels unresolved data clearly and recognizes an observation resolved by an edit', () => {
    const observation = change({ kind: 'observation', field: 'VALOR_BRUT', before: ['100', '200'], after: '', reason: 'Hay importes diferentes.' })
    const pending = presentReportChanges([observation], details, [{ ...row, amount: null, issues: [observation.reason] }])
    expect(pending.automatic[0]!.events[0]!.actions[0]!).toMatchObject({ text: 'Dato por completar: importe.', status: 'pending', comparison: null })
    const resolved = presentReportChanges([observation], details, [row])
    expect(resolved.automatic[0]!.events[0]!.actions[0]!.status).toBe('resolved')
    expect(resolved.automatic[0]!.events[0]!.actions[0]!.text).not.toContain('por completar')
    expect(resolved.automatic[0]!.events[0]!.actions[0]!.explanation).toContain('se resolvió al editar')
  })
  it('searches current OPs, client names and friendly field names without requiring accents', () => {
    const result = presentReportChanges([change({ kind: 'amount_kept_once', field: 'VALOR_BRUT', before: ['100'], after: '100' })], details, [row])
    const entry = result.automatic[0]!
    expect(searchChangeEntry(entry, 'clinica')).toBe(true)
    expect(searchChangeEntry(entry, '4502')).toBe(true)
    expect(searchChangeEntry(entry, 'importe')).toBe(true)
    expect(searchChangeEntry(entry, 'no existe')).toBe(false)
  })
  it('uses structured server review instead of stale row issues and distinguishes unchanged human N/A', () => {
    const observation = change({ kind: 'observation', field: 'NUMERO OP', before: [''], after: 'N/A', reason: 'OP no informada.' })
    const review: ReportReview = { schemaVersion: 1, policyVersion: 1, cases: [{ id: 'case', rowKey: 'row', scope: 'row', classification: 'validation', findings: [{
      id: 'op', code: 'op_not_informed', field: 'NUMERO OP', rule: 'RC-06', initialClassification: 'validation', resolution: 'resolved', provenance: 'human', proposal: 'N/A', reason: 'OP no informada.', detailIds: ['a'], evidence: [], allowedActions: ['keep_na'], decisionId: 'keep',
    }] }], decisions: [], approvals: [], summary: { sourceRows: 1, finalRows: 1, automaticRows: 0, humanResolvedRows: 1, validationRows: 0, conflictRows: 0, filePendingCases: 0, pendingCases: 0, pendingFindings: 0 } }
    const saved = presentReportChanges([observation], details, [{ ...row, op: 'N/A', issues: [observation.reason] }], review)
    expect(saved.automatic[0]!.events[0]!.actions[0]).toMatchObject({ status: 'resolved', text: 'Número OP: confirmado por la auxiliar.' })
    review.cases[0]!.findings[0]!.resolution = 'pending'
    const pending = presentReportChanges([observation], details, [{ ...row, op: 'N/A', issues: [] }], review)
    expect(pending.automatic[0]!.events[0]!.actions[0]).toMatchObject({ status: 'pending', text: 'Número OP: decisión pendiente.', explanation: 'OP no informada.' })
  })
})
