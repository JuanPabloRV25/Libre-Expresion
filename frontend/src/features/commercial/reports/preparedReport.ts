import type { PreparedRowEdit, ReportChange, SalesGroup } from './types'

export const finalFields = [
  { key: 'op', label: 'NUMERO OP' }, { key: 'factura', label: 'FACTURA' },
  { key: 'number', label: 'NUMERO' }, { key: 'date', label: 'FECHA' },
  { key: 'client', label: 'NOMBRE' }, { key: 'term', label: 'PLAZO' },
  { key: 'amount', label: 'VALOR_BRUT' }, { key: 'detail', label: 'DETALLE' },
  { key: 'line', label: 'LINEA' }, { key: 'seller', label: 'VENDEDOR' },
] as const
export type FinalField = typeof finalFields[number]['key']
export type RowDraft = Record<Exclude<FinalField, 'amount'>, string> & { amount: number | null }
// Modes come from the saved server result; OP text and candidate counts are not business rules here.
export function amountCanBeEdited(row: SalesGroup): boolean {
  return row.amountMode !== 'automatic' && row.amountMode !== 'validation'
}
export function amountPresentation(row: SalesGroup): { label: string; help: string } | null {
  if (row.amountMode === 'automatic') return { label: 'Automático · 1 OP', help: 'Importe del informe de ventas de Manager.' }
  if (row.amountMode === 'manual') return { label: 'Manual · varias OP', help: 'Escribe el total correspondiente a las OP de esta fila.' }
  if (row.amountMode === 'validation') return { label: 'Requiere validación', help: 'Manager contiene importes diferentes para una sola OP. El total queda sin determinar.' }
  return null
}
export function rowDraft(row: SalesGroup): RowDraft {
  return { op: row.op, factura: row.factura, number: row.number, date: row.date, client: row.client,
    term: row.term, amount: row.amount, detail: row.details.join('\n'), line: row.line, seller: row.seller }
}
// Submit only the fields the auxiliary changed. Unrelated unresolved fields remain optional.
export function rowPatch(row: SalesGroup, draft: RowDraft, historyIds?: string[], amountConfirmed = false): PreparedRowEdit {
  const previous = rowDraft(row)
  const patch: PreparedRowEdit = { key: row.key }
  for (const { key } of finalFields) {
    if (key === 'amount') {
      if (row.amountMode && row.amountMode !== 'legacy' && draft.op !== previous.op) continue
      const editable = amountCanBeEdited(row)
      const confirmingAutomatic = row.amountMode === 'automatic' && amountConfirmed && previous.amount !== null && draft.amount === previous.amount
      if ((editable && (draft.amount !== previous.amount || amountConfirmed)) || confirmingAutomatic) { patch.amount = draft.amount; patch.setAmount = true }
    } else if (draft[key] !== previous[key]) patch[key] = draft[key]
  }
  if (historyIds !== undefined) patch.historyIds = historyIds
  return patch
}
export function formatFinalValue(row: SalesGroup, field: FinalField) {
  if (field === 'detail') return row.details.join('\n')
  if (field === 'amount') return row.amount === null ? '' : new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(row.amount)
  if (field === 'date' && row.date) {
    const date = new Date(`${row.date.slice(0, 10)}T12:00:00`)
    return Number.isNaN(date.getTime()) ? row.date : date.toLocaleDateString('es-CO')
  }
  return row[field]
}
export function searchRow(row: SalesGroup, query: string) {
  const text = finalFields.map(({ key }) => formatFinalValue(row, key)).join(' ').toLocaleLowerCase('es-CO')
  return text.includes(query.trim().toLocaleLowerCase('es-CO'))
}

// Match the download: the total covers all saved rows and remains blank while an amount is unresolved.
export function preparedReportTotal(rows: SalesGroup[], changes: ReportChange[]): number | null {
  if (rows.some(row => row.amount === null || !Number.isFinite(row.amount))) return null
  const rowsByKey = new Map(rows.map(row => [row.key, row]))
  if (changes.some(change => change.kind === 'shared_amount_observation' && change.rowKey &&
    rowsByKey.get(change.rowKey)?.issues.includes(change.reason))) return null
  return rows.reduce((sum, row) => sum + (row.amount ?? 0), 0)
}
