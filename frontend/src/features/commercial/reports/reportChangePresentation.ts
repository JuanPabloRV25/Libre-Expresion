import type { ReportChange, ReportReview, SalesDetail, SalesGroup } from './types'

export interface ChangeAction {
  id: string
  text: string
  explanation: string
  status: 'changed' | 'pending' | 'resolved' | 'confirmation' | 'retained'
  comparison: { before: string[]; after: string } | null
}
export interface ChangeEvent {
  id: string
  label: string
  actions: ChangeAction[]
}
export interface ChangeEntry {
  id: string
  rowKey: string | null
  title: string
  subtitle: string
  changes: ReportChange[]
  events: ChangeEvent[]
  searchText: string
}
const fields: Record<string, string> = {
  'NUMERO OP': 'Número OP', NUMERO: 'Número', FACTURA: 'Factura', FECHA: 'Fecha',
  NOMBRE: 'Cliente', PLAZO: 'Plazo', VALOR_BRUT: 'Importe', DETALLE: 'Detalle',
  LINEA: 'Línea', VENDEDOR: 'Vendedor', Filas: 'Registros reunidos',
  Columnas: 'Columnas excluidas', Archivo: 'Archivo', Fila: 'Fila',
}
export function changeFieldLabel(field: string) { return fields[field] || field }
const money = new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 2 })
export function changeValue(field: string, value: string): string {
  if (!value.trim()) return 'Celda vacía'
  if (field === 'VALOR_BRUT' && Number.isFinite(Number(value))) return money.format(Number(value))
  if (field === 'FECHA' && /^\d{4}-\d{2}-\d{2}$/.test(value)) return value.split('-').reverse().join('/')
  return value
}
function sameValue(field: string, before: string, after: string) {
  if (field === 'VALOR_BRUT' && before.trim() && after.trim() && Number.isFinite(Number(before)) && Number.isFinite(Number(after))) return Number(before) === Number(after)
  return before === after
}
export function changeComparison(change: ReportChange): ChangeAction['comparison'] {
  if (change.kind === 'columns_removed' || !change.before.length) return null
  const before = [...new Set(change.before)]
  if (before.every(value => sameValue(change.field, value, change.after))) return null
  return { before: before.map(value => changeValue(change.field, value)), after: changeValue(change.field, change.after) }
}
function sourceCount(change: ReportChange) { return new Set(change.detailIds).size || change.before.length }
function action(change: ReportChange, row?: SalesGroup, review?: ReportReview | null): ChangeAction {
  const field = changeFieldLabel(change.field)
  let text: string
  let explanation = '', status: ChangeAction['status'] = 'changed', comparison = changeComparison(change)
  switch (change.kind) {
    case 'columns_removed':
      text = 'Excluimos IVA, TOTAL y NUMERO_REM.'
      explanation = 'Estas columnas del Informe de ventas no se necesitan en VENTAS MES.'
      comparison = null
      break
    case 'rows_consolidated':
      text = `Reunimos ${sourceCount(change)} registros del Informe de ventas en una sola fila.`
      explanation = 'Se conservaron los datos y detalles del grupo de origen indicado en la evidencia.'
      comparison = null
      break
    case 'op_rectified':
      text = 'Actualizamos los números OP con el Informe de OPs.'
      explanation = 'Los registros utilizados se pueden consultar en la evidencia del ajuste.'
      break
    case 'details_combined':
      text = `Unimos los ${sourceCount(change)} detalles en una celda.`
      explanation = 'Se conservaron todos, en el orden original y separados por líneas.'
      comparison = null
      break
    case 'amount_kept_once':
      text = `Conservamos el importe una sola vez: ${changeValue(change.field, change.after)}.`
      explanation = 'El mismo importe se repetía en los registros reunidos. No sumamos esas repeticiones.'
      comparison = null
      break
    case 'source_kept':
      text = 'Conservamos el registro como venía en el Informe de ventas.'
      explanation = 'La combinación del número OP y la línea aparece una sola vez.'
      status = 'retained'; comparison = null
      break
    case 'manual_restore':
      text = change.field === 'Fila' ? 'Recuperaste la propuesta automática de la fila.' : `Recuperaste la propuesta automática de ${field.toLowerCase()}.`
      break
    case 'manual_edit':
      if (!comparison) {
        text = change.field === 'NUMERO OP' ? 'Confirmaste los números OP seleccionados.' : `Confirmaste el valor de ${field.toLowerCase()} sin modificarlo.`
        status = 'confirmation'
      } else if (!change.after.trim()) text = `Dejaste la celda de ${field.toLowerCase()} vacía.`
      else if (change.before.every(value => !value.trim())) text = `Completaste ${field.toLowerCase()}.`
      else text = `Ajustaste ${field.toLowerCase()}.`
      break
    case 'shared_amount_observation':
      text = 'Importe por confirmar.'
      explanation = 'Este importe se repite en otras líneas de la misma OP. Se conservó sin repartirlo; puedes confirmarlo o ajustarlo en Editar.'
      status = review ? review.cases.some(item => item.rowKey === change.rowKey && item.findings.some(finding => finding.field === change.field && finding.resolution === 'pending')) ? 'pending' : 'resolved' : !row || row.issues.includes(change.reason) ? 'pending' : 'resolved'; comparison = null
      if (status === 'resolved') { text = 'La observación del importe ya está resuelta.'; explanation = 'El importe repetido en otras líneas se revisó al editar la fila.' }
      break
    case 'observation': {
      const pending = review ? review.cases.some(item => item.rowKey === change.rowKey && item.findings.some(finding => finding.field === change.field && finding.resolution === 'pending')) : !row || row.issues.includes(change.reason)
      status = pending ? 'pending' : 'resolved'; comparison = null
      if (!change.rowKey) { text = 'Observación del archivo.'; explanation = change.reason; break }
      if (change.field === 'NUMERO OP') {
        text = change.after.trim() ? 'Número OP por confirmar.' : 'Dato por completar: número OP.'
        explanation = change.after.trim()
          ? 'No se pudo elegir con certeza una OP del histórico. Se conservó el número del Informe de ventas; puedes revisarlo en Editar.'
          : 'El Informe de ventas no trae este número. La fila se conservó para que puedas completarlo en Editar.'
      } else if (change.field === 'VALOR_BRUT') {
        text = 'Dato por completar: importe.'
        explanation = 'Los importes de origen son diferentes. La celda final quedó vacía para evitar escoger o sumar un valor incorrecto.'
      } else {
        text = `Dato por completar: ${field.toLowerCase()}.`
        const different = new Set(change.before.filter(value => value.trim())).size > 1
        explanation = different
          ? `Los valores de ${field.toLowerCase()} son diferentes en los registros de origen. La celda final quedó vacía; puedes ajustarla en Editar.`
          : `Este dato está vacío en el Informe de ventas. Se conservó así; puedes completarlo en Editar.`
      }
      if (!pending) { text = `La observación de ${field.toLowerCase()} ya está resuelta.`; explanation = `Esta observación se resolvió al editar la fila. ${explanation}` }
      break
    }
    default:
      text = `${field}: ajuste del reporte.`; explanation = change.reason
  }
  if (review && (change.kind === 'observation' || change.kind === 'shared_amount_observation')) {
    const related = review.cases.filter(item => item.rowKey === change.rowKey).flatMap(item => item.findings).filter(finding => finding.field === change.field)
    const pending = related.filter(finding => finding.resolution === 'pending')
    if (pending.length) { text = `${field}: decisión pendiente.`; explanation = pending.map(finding => finding.reason).join('\n'); status = 'pending'; comparison = null }
    else if (related.some(finding => finding.provenance === 'human' && finding.resolution === 'resolved')) { text = `${field}: confirmado por la auxiliar.`; explanation = 'La decisión guardada se puede consultar en el historial de decisiones.'; status = 'resolved'; comparison = null }
  }
  return { id: change.id, text, explanation, status, comparison }
}
function eventLabel(change: ReportChange): string {
  if (!change.manual) return 'Preparación automática'
  const date = change.occurredAt ? new Date(change.occurredAt) : null
  const time = date && !Number.isNaN(date.getTime()) ? date.toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short' }) : ''
  return [change.reportVersion ? `Versión ${change.reportVersion}` : 'Edición guardada', time].filter(Boolean).join(' · ')
}
function entries(changes: ReportChange[], details: SalesDetail[], rows: SalesGroup[], manual: boolean, review?: ReportReview | null): ChangeEntry[] {
  const rowByKey = new Map(rows.map(row => [row.key, row])), detailById = new Map(details.map(detail => [detail.id, detail]))
  const grouped = new Map<string, ReportChange[]>()
  for (const change of changes) {
    const key = change.rowKey || 'file'
    const group = grouped.get(key) || []; group.push(change); grouped.set(key, group)
  }
  const results = [...grouped].map(([key, changes]) => {
    const rowKey = changes[0]!.rowKey, row = rowKey ? rowByKey.get(rowKey) : undefined
    const detail = changes.flatMap(change => change.detailIds).map(id => detailById.get(id)).find(Boolean)
    const op = row?.op || detail?.managerOp || '', client = row?.client || detail?.client || '', line = row?.line || detail?.line || ''
    const title = rowKey ? op ? `OP ${op}` : 'Registro sin número OP' : 'Preparación del archivo'
    const subtitle = rowKey ? [client, line].filter(Boolean).join(' · ') || 'Fila de VENTAS MES' : 'Informe de ventas → VENTAS MES'
    const batches = new Map<string, ReportChange[]>()
    for (const change of changes) {
      const batchKey = manual ? `${change.reportVersion || 0}:${change.occurredAt || ''}:${change.actor || ''}` : 'automatic'
      const batch = batches.get(batchKey) || []; batch.push(change); batches.set(batchKey, batch)
    }
    const events = [...batches].map(([id, batch]) => ({ id, label: eventLabel(batch[0]!), actions: batch.map(change => action(change, row, review)) }))
    if (manual) events.reverse()
    const searchText = [title, subtitle, ...changes.flatMap(change => [change.field, changeFieldLabel(change.field), ...change.before, change.after, change.reason]), ...events.flatMap(event => event.actions.map(action => `${action.text} ${action.explanation}`))].join(' ')
    return { id: `${manual ? 'manual' : 'automatic'}:${key}`, rowKey, title, subtitle, changes, events, searchText }
  })
  return manual ? results.reverse() : results
}
export function presentReportChanges(changes: ReportChange[], details: SalesDetail[], rows: SalesGroup[] = [], review?: ReportReview | null) {
  // File findings are handled in the incidence resolver; the persisted audit is untouched.
  const rowChanges = changes.filter(change => !!change.rowKey && change.kind !== 'columns_removed')
  return {
    automatic: entries(rowChanges.filter(change => !change.manual && change.kind !== 'source_kept'), details, rows, false, review),
    manual: entries(rowChanges.filter(change => change.manual), details, rows, true, review),
    retained: entries(rowChanges.filter(change => !change.manual && change.kind === 'source_kept'), details, rows, false, review),
  }
}
export function searchChangeEntry(entry: ChangeEntry, search: string) {
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
  return normalize(entry.searchText).includes(normalize(search.trim()))
}
