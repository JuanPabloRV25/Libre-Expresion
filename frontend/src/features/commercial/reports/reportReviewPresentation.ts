import type { PreparedRowEdit, ReportReview, ReviewAction, ReviewCase, ReviewCommand, ReviewFinding, SalesGroup } from './types'
import { amountCanBeEdited, amountPresentation, finalFields, rowDraft, type FinalField } from './preparedReport'

// Presentation only: the server owns classification, counts, resolutions and capabilities.
export function pendingCaseSeverity(item: ReviewCase): 'validation' | 'conflict' | null {
  const pending = item.findings.filter(finding => finding.resolution === 'pending')
  if (!pending.length) return null
  return pending.some(finding => finding.initialClassification === 'conflict') ? 'conflict' : 'validation'
}
export function pendingReviewCases(review: ReportReview): ReviewCase[] {
  const conflict = (item: ReviewCase) => pendingCaseSeverity(item) === 'conflict'
  return review.cases.map((item, index) => ({ item, index }))
    .filter(({ item }) => item.findings.some(finding => finding.resolution === 'pending'))
    .sort((a, b) => Number(conflict(b.item)) - Number(conflict(a.item)) || a.index - b.index)
    .map(({ item }) => item)
}
export function reviewFindingLabel(finding: ReviewFinding): string {
  if (finding.provenance === 'human' && finding.resolution === 'resolved') return 'Confirmado por la auxiliar'
  if (finding.resolution === 'resolved') return 'Resuelto automáticamente'
  return finding.initialClassification === 'conflict' ? 'Conflicto pendiente' : 'Requiere validación'
}
export function reviewDecisionLabel(action: ReviewAction | 'field_edit'): string {
  return { keep_na: 'Mantener N/A', set_manual_op: 'OP escrita por la auxiliar', select_candidates: 'OPs elegidas por la auxiliar', field_edit: 'Corrección guardada' }[action]
}
export function reviewCommand(item: ReviewCase, findingIds: string[], action: ReviewAction, historyIds: string[] = [], op = ''): ReviewCommand {
  if (!findingIds.length || findingIds.some(id => !item.findings.some(finding => finding.id === id && finding.resolution === 'pending' && finding.allowedActions.includes(action))))
    throw new Error('Esta acción no está disponible para el caso actual.')
  const command: ReviewCommand = { caseId: item.id, findingIds: [...findingIds], action }
  if (action === 'select_candidates') {
    if (!historyIds.length) throw new Error('Elige al menos una OP.')
    command.historyIds = [...new Set(historyIds)]
  }
  if (action === 'set_manual_op') {
    if (!op.trim()) throw new Error('Escribe el número OP que quieres guardar.')
    command.op = op.trim()
  }
  return command
}
export function reviewDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' })
}

export interface ReviewFieldEditor {
  key: Exclude<FinalField, 'op' | 'factura'>
  label: string
  input: 'text' | 'date' | 'number' | 'textarea'
  value: string
  readOnly?: boolean
  help?: string
  modeLabel?: string
  confirmUnchanged?: boolean
}
// Existing row patches are used for cell corrections; OP commands remain independent.
export function reviewFieldEditor(row: SalesGroup, finding: ReviewFinding): ReviewFieldEditor | null {
  const field = finalFields.find(value => value.label === finding.field)
  if (!field || field.key === 'op' || field.key === 'factura') return null
  const value = rowDraft(row)[field.key]
  return {
    key: field.key, label: field.label,
    input: field.key === 'detail' ? 'textarea' : field.key === 'date' ? 'date' : field.key === 'amount' ? 'number' : 'text',
    value: value === null ? '' : String(value),
    ...(field.key === 'amount' && amountPresentation(row) ? {
      readOnly: !amountCanBeEdited(row), help: amountPresentation(row)!.help, modeLabel: amountPresentation(row)!.label,
      confirmUnchanged: row.amountMode !== 'validation' && row.amount !== null && finding.code === 'shared_amount_observation',
    } : {}),
  }
}
export function reviewFieldPatch(row: SalesGroup, finding: ReviewFinding, value: string): PreparedRowEdit {
  const field = reviewFieldEditor(row, finding)
  if (!field) throw new Error('Esta incidencia no corresponde a una celda editable del informe.')
  if (field.key === 'amount') {
    const amount = value.trim() === '' ? null : Number(value)
    if (amount !== null && !Number.isFinite(amount)) throw new Error('Escribe un importe válido.')
    if (field.readOnly && (!field.confirmUnchanged || amount !== row.amount))
      throw new Error('Este importe no se puede editar. Se conserva el valor de Manager o la validación pendiente.')
    return { key: row.key, amount, setAmount: true }
  }
  return { key: row.key, [field.key]: value }
}
