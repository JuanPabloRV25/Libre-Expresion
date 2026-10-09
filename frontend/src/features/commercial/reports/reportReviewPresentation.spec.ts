import { describe, expect, it } from 'vitest'
import { pendingCaseSeverity, pendingReviewCases, reviewCommand, reviewDecisionLabel, reviewFieldEditor, reviewFieldPatch, reviewFindingLabel } from './reportReviewPresentation'
import type { ReportReview, ReviewCase, ReviewFinding, SalesGroup } from './types'
const finding = (id: string, overrides: Partial<ReviewFinding> = {}): ReviewFinding => ({
  id, code: 'op_not_determined', field: 'NUMERO OP', rule: 'RC-06', initialClassification: 'validation',
  resolution: 'pending', provenance: 'automatic', proposal: 'N/A', reason: 'OP no determinada',
  detailIds: ['detail'], evidence: [], allowedActions: ['select_candidates', 'set_manual_op', 'keep_na'], ...overrides,
})

describe('inline incidence field correction', () => {
  const row: SalesGroup = { key: 'sale-row', documentId: 'sale', number: '00042', date: '2026-10-07', op: 'N/A', product: '',
    client: 'Cliente', term: '30', amount: 123.45, factura: '0007', line: 'Producto', seller: 'Asesor',
    details: ['Primer detalle', 'Segundo detalle'], detailIds: ['detail'], issues: ['OP no determinada'], modified: false }
  const fieldFinding = (field: string) => finding(`field-${field}`, { field, allowedActions: [] })

  it('shows the saved cell and its matching control, preserving detail line breaks and decimal values', () => {
    expect(reviewFieldEditor(row, fieldFinding('VALOR_BRUT'))).toEqual({ key: 'amount', label: 'VALOR_BRUT', input: 'number', value: '123.45' })
    expect(reviewFieldEditor(row, fieldFinding('FECHA'))).toEqual({ key: 'date', label: 'FECHA', input: 'date', value: '2026-10-07' })
    expect(reviewFieldEditor(row, fieldFinding('DETALLE'))).toEqual({ key: 'detail', label: 'DETALLE', input: 'textarea', value: 'Primer detalle\nSegundo detalle' })
    expect(reviewFieldEditor(row, fieldFinding('NUMERO'))?.value).toBe('00042')
  })
  it('submits only the affected field without replaying OP, FACTURA or other saved corrections', () => {
    const edits = [
      ['NUMERO', 'number', '00043'], ['FECHA', 'date', '2026-10-08'], ['NOMBRE', 'client', 'Otro cliente'],
      ['PLAZO', 'term', '60'], ['DETALLE', 'detail', 'Uno\nDos'], ['LINEA', 'line', 'Otra línea'], ['VENDEDOR', 'seller', 'Otra asesora'],
    ] as const
    for (const [field, key, value] of edits) expect(reviewFieldPatch(row, fieldFinding(field), value)).toEqual({ key: 'sale-row', [key]: value })
    expect(row.op).toBe('N/A')
    expect(row.factura).toBe('0007')
  })
  it('uses the existing amount confirmation patch even to maintain its shown value, and distinguishes zero from blank', () => {
    const amount = fieldFinding('VALOR_BRUT')
    expect(reviewFieldPatch(row, amount, '123.45')).toEqual({ key: 'sale-row', amount: 123.45, setAmount: true })
    expect(reviewFieldPatch(row, amount, '0')).toEqual({ key: 'sale-row', amount: 0, setAmount: true })
    expect(reviewFieldPatch(row, amount, '')).toEqual({ key: 'sale-row', amount: null, setAmount: true })
    expect(() => reviewFieldPatch(row, amount, 'no es un importe')).toThrow('importe válido')
  })
  it('does not offer a generic cell edit for an OP decision, optional FACTURA, file warning or unknown field', () => {
    for (const field of ['NUMERO OP', 'FACTURA', 'Archivo', 'DESCONOCIDO']) {
      expect(reviewFieldEditor(row, fieldFinding(field))).toBeNull()
      expect(() => reviewFieldPatch(row, fieldFinding(field), 'valor')).toThrow('celda editable')
    }
    expect(() => reviewCommand(item('sale', [fieldFinding('VALOR_BRUT')]), ['field-VALOR_BRUT'], 'keep_na')).toThrow('no está disponible')
  })
  it('shows a single OP automatic amount as read-only and only confirms an existing shared amount without changing it', () => {
    const automatic: SalesGroup = { ...row, amountMode: 'automatic', opCount: 1 }
    const shared = { ...fieldFinding('VALOR_BRUT'), code: 'shared_amount_observation' }
    const editor = reviewFieldEditor(automatic, shared)
    expect(editor).toMatchObject({ value: '123.45', readOnly: true, confirmUnchanged: true, modeLabel: 'Automático · 1 OP' })
    expect(reviewFieldPatch(automatic, shared, '123.45')).toEqual({ key: 'sale-row', amount: 123.45, setAmount: true })
    for (const value of ['999', '0', '']) expect(() => reviewFieldPatch(automatic, shared, value)).toThrow('no se puede editar')
    expect(() => reviewFieldPatch(automatic, fieldFinding('VALOR_BRUT'), '123.45')).toThrow('no se puede editar')
  })
  it('keeps differing Manager amounts for one OP pending and offers no manual substitute', () => {
    const unresolved: SalesGroup = { ...row, amount: null, amountMode: 'validation', opCount: 1 }
    const amount = { ...fieldFinding('VALOR_BRUT'), code: 'amount_undetermined' }
    expect(reviewFieldEditor(unresolved, amount)).toMatchObject({ value: '', readOnly: true, confirmUnchanged: false, modeLabel: 'Requiere validación' })
    for (const value of ['123.45', '0', '']) expect(() => reviewFieldPatch(unresolved, amount, value)).toThrow('no se puede editar')
    expect(reviewFieldPatch(unresolved, fieldFinding('NOMBRE'), 'Auxiliar')).toEqual({ key: 'sale-row', client: 'Auxiliar' })
  })
  it('offers an empty editable amount for several final OP and keeps missing OP compatibility unchanged', () => {
    const manual: SalesGroup = { ...row, op: '123/456', amount: null, amountMode: 'manual', opCount: 2 }
    const amount = fieldFinding('VALOR_BRUT')
    expect(reviewFieldEditor(manual, amount)).toMatchObject({ value: '', readOnly: false, modeLabel: 'Manual · varias OP' })
    expect(reviewFieldPatch(manual, amount, '123.45')).toEqual({ key: 'sale-row', amount: 123.45, setAmount: true })
    const absent: SalesGroup = { ...row, op: 'N/A', amountMode: 'legacy', opCount: 0 }
    expect(reviewFieldEditor(absent, amount)).toEqual(reviewFieldEditor(row, amount))
    expect(reviewFieldPatch(absent, amount, '0')).toEqual({ key: 'sale-row', amount: 0, setAmount: true })
  })
})
const item = (id: string, findings: ReviewFinding[], overrides: Partial<ReviewCase> = {}): ReviewCase => ({
  id, rowKey: id, scope: 'row', classification: 'validation', findings, ...overrides,
})
const review = (cases: ReviewCase[]): ReportReview => ({ schemaVersion: 1, policyVersion: 1, cases, decisions: [], approvals: [], summary: {
  sourceRows: 10, finalRows: 3, automaticRows: 1, humanResolvedRows: 0, validationRows: 1, conflictRows: 1, filePendingCases: 1, pendingCases: 3, pendingFindings: 4,
} })
describe('exception review presentation', () => {
  it('keeps file incidents and only pending cases, with conflicts first and stable source order', () => {
    const data = review([item('validation', [finding('v')]), item('resolved', [finding('r', { resolution: 'resolved', provenance: 'human' })]),
      item('conflict', [finding('c', { initialClassification: 'conflict' })]), item('file', [finding('f')], { scope: 'file', rowKey: null })])
    expect(pendingReviewCases(data).map(value => value.id)).toEqual(['conflict', 'validation', 'file'])
    expect(data.summary.pendingCases).toBe(3)
    expect(data.cases).toHaveLength(4)
  })
  it('does not classify a resolved human N/A as automatic or remove unrelated pending findings', () => {
    const kept = finding('op', { resolution: 'resolved', provenance: 'human', decisionId: 'kept' })
    const amount = finding('amount', { field: 'VALOR_BRUT', allowedActions: [], initialClassification: 'conflict' })
    expect(reviewFindingLabel(kept)).toBe('Confirmado por la auxiliar')
    expect(pendingReviewCases(review([item('row', [kept, amount])]))).toHaveLength(1)
    expect(reviewFindingLabel(amount)).toBe('Conflicto pendiente')
  })
  it('uses remaining findings for ordering after a conflict was resolved', () => {
    const mixed = item('mixed', [finding('old', { resolution: 'resolved', initialClassification: 'conflict', provenance: 'human' }), finding('new')], { classification: 'conflict' })
    expect(pendingReviewCases(review([item('validation', [finding('v')]), mixed])).map(value => value.id)).toEqual(['validation', 'mixed'])
  })
  it('presents current severity without changing the original classification or resolution', () => {
    const mixed = item('mixed', [finding('old', { resolution: 'resolved', initialClassification: 'conflict', provenance: 'human' }), finding('new')], { classification: 'conflict' })
    expect(pendingCaseSeverity(mixed)).toBe('validation')
    expect(mixed.classification).toBe('conflict')
    expect(mixed.findings[0]!.resolution).toBe('resolved')
    mixed.findings[1]!.resolution = 'resolved'
    expect(pendingCaseSeverity(mixed)).toBeNull()
  })
  it('sends KeepNA explicitly even when N/A is already the displayed proposal', () => {
    expect(reviewCommand(item('sale', [finding('op')]), ['op'], 'keep_na', ['ignored'], 'ignored')).toEqual({ caseId: 'sale', findingIds: ['op'], action: 'keep_na' })
  })
  it('sends one decision type without carrying another mode or another finding', () => {
    const current = item('sale', [finding('op'), finding('amount', { field: 'VALOR_BRUT', allowedActions: [] })])
    expect(reviewCommand(current, ['op'], 'select_candidates', ['a', 'b', 'a'], 'ignored')).toEqual({ caseId: 'sale', findingIds: ['op'], action: 'select_candidates', historyIds: ['a', 'b'] })
    expect(reviewCommand(current, ['op'], 'set_manual_op', ['ignored'], ' 00123 ')).toEqual({ caseId: 'sale', findingIds: ['op'], action: 'set_manual_op', op: '00123' })
    expect(() => reviewCommand(current, ['amount'], 'keep_na')).toThrow('no está disponible')
    expect(() => reviewCommand(current, ['another-case'], 'keep_na')).toThrow('no está disponible')
    expect(() => reviewCommand(current, ['op'], 'select_candidates')).toThrow('al menos una OP')
    expect(() => reviewCommand(current, ['op'], 'set_manual_op', [], ' ')).toThrow('Escribe')
  })
  it('distinguishes independent field corrections from OP decisions', () => {
    expect(reviewDecisionLabel('field_edit')).toBe('Corrección guardada')
    expect(reviewDecisionLabel('keep_na')).toBe('Mantener N/A')
  })
})
