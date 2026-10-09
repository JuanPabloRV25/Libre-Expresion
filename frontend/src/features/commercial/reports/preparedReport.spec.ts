import { describe, expect, it } from 'vitest'
import { amountCanBeEdited, amountPresentation, preparedReportTotal, rowDraft, rowPatch, searchRow } from './preparedReport'
import type { ReportChange, SalesGroup } from './types'
const row: SalesGroup = { key: 'row', documentId: '', number: '42', date: '', op: '', product: '', client: 'Cliente', term: '',
  amount: null, factura: '', line: 'Línea A', seller: '', details: ['Detalle A', 'Detalle A'], detailIds: ['a', 'b'], issues: [], modified: false }
describe('prepared row edits', () => {
  it('patches one optional field without confirming an unresolved OP, date or amount', () => {
    const draft = rowDraft(row); draft.factura = 'Código escrito'
    expect(rowPatch(row, draft)).toEqual({ key: 'row', factura: 'Código escrito' })
    expect(draft.detail).toBe('Detalle A\nDetalle A')
  })
  it('retains manual FACTURA text and leading zeros without treating N/A as an OP decision', () => {
    const absent = { ...row, op: 'N/A', issues: ['OP no informada'] }
    const draft = { ...rowDraft(absent), factura: '00023' }
    expect(rowPatch(absent, draft)).toEqual({ key: 'row', factura: '00023' })
    expect(absent.op).toBe('N/A')
    expect(absent.issues).toEqual(['OP no informada'])
  })
  it('distinguishes a deliberate blank amount from zero', () => {
    expect(rowPatch(row, { ...rowDraft(row), amount: 0 })).toEqual({ key: 'row', amount: 0, setAmount: true })
    expect(rowPatch({ ...row, amount: 10 }, rowDraft(row))).toEqual({ key: 'row', amount: null, setAmount: true })
  })
  it('does not replay earlier saved fields when changing a different field', () => {
    const saved = { ...row, factura: 'Manual', amount: 0 }
    expect(rowPatch(saved, { ...rowDraft(saved), client: 'Nuevo' })).toEqual({ key: 'row', client: 'Nuevo' })
  })
  it('searches all exported fields including details and manual FACTURA', () => {
    expect(searchRow(row, 'detalle a')).toBe(true)
    expect(searchRow({ ...row, factura: 'F-1' }, 'F-1')).toBe(true)
    expect(searchRow(row, 'otro')).toBe(false)
  })
  it('sends an explicit historical selection without replaying unrelated saved fields', () => {
    const saved = { ...row, factura: 'Manual', amount: 0 }
    expect(rowPatch(saved, rowDraft(saved), ['chosen'])).toEqual({ key: 'row', historyIds: ['chosen'] })
  })
  it('confirms a shared amount only when explicitly requested', () => {
    const valued = { ...row, amount: 100 }
    expect(rowPatch(valued, rowDraft(valued))).toEqual({ key: 'row' })
    expect(rowPatch(valued, rowDraft(valued), undefined, true)).toEqual({ key: 'row', amount: 100, setAmount: true })
  })
  it('uses server amount modes rather than inferring rules from OP text or repeated detail count', () => {
    expect(amountCanBeEdited({ ...row, op: '123/456', amountMode: 'automatic', opCount: 1 })).toBe(false)
    expect(amountCanBeEdited({ ...row, op: '123', amountMode: 'manual', opCount: 2 })).toBe(true)
    expect(amountCanBeEdited({ ...row, amountMode: 'validation', opCount: 1 })).toBe(false)
    expect(amountCanBeEdited({ ...row, op: '123', amountMode: 'legacy' })).toBe(true)
    expect(amountCanBeEdited({ ...row, op: '123' })).toBe(true)
    expect(amountPresentation({ ...row, amountMode: 'manual' })?.help).toContain('Escribe el total')
    expect(amountPresentation(row)).toBeNull()
  })
  it('preserves automatic Manager amount when another editable field changes, even with a modified local amount draft', () => {
    const automatic: SalesGroup = { ...row, amount: 123.45, factura: 'F01', amountMode: 'automatic', opCount: 1 }
    expect(rowPatch(automatic, { ...rowDraft(automatic), factura: '00023', amount: 987 })).toEqual({ key: 'row', factura: '00023' })
    expect(rowPatch(automatic, { ...rowDraft(automatic), amount: null })).toEqual({ key: 'row' })
    expect(rowPatch(automatic, rowDraft(automatic), undefined, true)).toEqual({ key: 'row', amount: 123.45, setAmount: true })
    expect(rowPatch(automatic, { ...rowDraft(automatic), amount: 987 }, undefined, true)).toEqual({ key: 'row' })
  })
  it('never submits an invented value for one OP with differing Manager amounts but still permits FACTURA editing', () => {
    const unresolved: SalesGroup = { ...row, amountMode: 'validation', opCount: 1, factura: 'F01' }
    expect(rowPatch(unresolved, { ...rowDraft(unresolved), amount: 300, factura: '' }, undefined, true)).toEqual({ key: 'row', factura: '' })
    expect(amountPresentation(unresolved)?.help).toContain('importes diferentes')
  })
  it('accepts a manually entered total for several final OP and preserves zero, decimals, and an intentional blank', () => {
    const manual: SalesGroup = { ...row, op: '123/456', amountMode: 'manual', opCount: 2, factura: 'F01' }
    expect(rowDraft(manual).amount).toBeNull()
    expect(rowPatch(manual, { ...rowDraft(manual), amount: 42.75 })).toEqual({ key: 'row', amount: 42.75, setAmount: true })
    expect(rowPatch(manual, { ...rowDraft(manual), amount: 0 })).toEqual({ key: 'row', amount: 0, setAmount: true })
    const saved = { ...manual, amount: 42.75 }
    expect(rowPatch(saved, { ...rowDraft(saved), factura: 'Manual' })).toEqual({ key: 'row', factura: 'Manual' })
    expect(rowPatch(saved, { ...rowDraft(saved), amount: null })).toEqual({ key: 'row', amount: null, setAmount: true })
  })
  it('saves changed OP before editing the amount governed by its new server count, while preserving legacy simultaneous edits', () => {
    const manual: SalesGroup = { ...row, op: '123/456', amount: 400, amountMode: 'manual', opCount: 2 }
    expect(rowPatch(manual, { ...rowDraft(manual), op: '123', amount: 999, factura: 'F01' }, undefined, true)).toEqual({ key: 'row', op: '123', factura: 'F01' })
    const legacy: SalesGroup = { ...row, op: '123/456', amount: 400 }
    expect(rowPatch(legacy, { ...rowDraft(legacy), op: '123', amount: 999 })).toEqual({ key: 'row', op: '123', amount: 999, setAmount: true })
    const absent: SalesGroup = { ...row, op: 'N/A', amount: 400, amountMode: 'legacy', opCount: 0 }
    expect(rowPatch(absent, { ...rowDraft(absent), op: '123', amount: 999 })).toEqual({ key: 'row', op: '123', amount: 999, setAmount: true })
  })
})

describe('prepared report total', () => {
  const observation: ReportChange = { id: 'amount-warning', rowKey: 'row', kind: 'shared_amount_observation', field: 'VALOR_BRUT', before: ['100'], after: '100', reason: 'Importe compartido', detailIds: [], historyIds: [], manual: false }
  it('adds every saved row including zero and cents', () => {
    const rows = Array.from({ length: 8 }, (_, index) => ({ ...row, key: `row-${index}`, amount: index === 0 ? 0 : 10.25 }))
    expect(preparedReportTotal(rows, [])).toBe(71.75)
  })
  it('leaves the total undetermined while an amount is blank', () => {
    expect(preparedReportTotal([{ ...row, amount: 100 }, row], [])).toBeNull()
  })
  it('requires confirmation for a shared amount and accepts its saved resolution', () => {
    expect(preparedReportTotal([{ ...row, amount: 100, issues: [observation.reason] }], [observation])).toBeNull()
    expect(preparedReportTotal([{ ...row, amount: 100, issues: [] }], [observation])).toBe(100)
  })
})
