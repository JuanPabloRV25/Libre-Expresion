import { API_BASE_URL, getJson, mutateForm, postBlob, postJson, putJson } from '../../../api/httpClient'
import type { OpRecord, PreparedRowEdit, ReplacementPreview, ReportSummary, ReviewCommand, SalesReport } from './types'
const base = '/commercial/reports'
const form = (file: File) => { const body = new FormData(); body.set('file', file); return body }
export const reportService = {
  list: () => getJson<ReportSummary[]>(base),
  get: (id: string) => getJson<SalesReport>(`${base}/${id}`),
  create: (file: File) => mutateForm<SalesReport>('POST', base, form(file)),
  previewPrepared: (report: SalesReport, rowEdits: PreparedRowEdit[], decisions: ReviewCommand[] = []) => postJson<SalesReport>(`${base}/${report.id}/prepared/preview`, {
    version: report.version, name: report.name, rowEdits, decisions,
  }),
  savePrepared: (report: SalesReport, rowEdits: PreparedRowEdit[], decisions: ReviewCommand[] = []) => putJson<SalesReport>(`${base}/${report.id}/prepared`, {
    version: report.version, name: report.name, rowEdits, decisions,
  }),
  save: (report: SalesReport, codeChoices: Record<string, boolean>) => putJson<SalesReport>(`${base}/${report.id}`, {
    version: report.version, name: report.name,
    details: report.data.details.map(d => ({ id: d.id, selectedOpId: d.selectedOpId, usePortalCode: codeChoices[d.id] ?? d.usePortalCode,
      reviewed: d.reviewed, excluded: d.excluded, reason: d.reason, manualGroup: d.manualGroup, productIfMissing: d.selectedProduct })),
    edits: report.data.edits, documents: report.data.documents.map(d => ({ id: d.id, confirmedAmount: d.confirmedAmount, reason: d.reason })),
  }),
  ops: (search = '') => getJson<OpRecord[]>(`${base}/ops?search=${encodeURIComponent(search)}`),
  createOp: (cells: string[]) => postJson<OpRecord>(`${base}/ops`, { data: { cells, portalCode: null } }),
  saveOp: (record: OpRecord, cells: string[]) => putJson<OpRecord>(`${base}/ops/${record.id}`, {
    expectedCapturedAt: record.capturedAt, data: { cells, portalCode: record.data.portalCode },
  }),
  importOps: (file: File) => mutateForm<{ imported: number; duplicate: boolean; message: string }>('POST', `${base}/ops/import`, form(file)),
  originalUrl: (sourceId: string) => `${API_BASE_URL}${base}/sources/${sourceId}`,
  opsUrl: `${API_BASE_URL}${base}/ops/export`,
  export: (report: SalesReport) => postBlob(`${base}/${report.id}/export`, { version: report.version }),
  exportDraft: (report: SalesReport) => postBlob(`${base}/${report.id}/export/draft`, { version: report.version }),
  approve: (report: SalesReport) => postJson<SalesReport>(`${base}/${report.id}/approve`, { version: report.version }),
  replace: (report: SalesReport, file: File, confirm: boolean) => {
    const body = form(file); body.set('version', String(report.version)); body.set('confirm', String(confirm))
    return mutateForm<ReplacementPreview>('POST', `${base}/${report.id}/source`, body)
  },
}
export function money(value: number | null) {
  return value === null ? 'Sin completar' : new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 2 }).format(value)
}
export function inputMoney(event: Event): number | null {
  const raw = (event.target as HTMLInputElement).value
  return raw.trim() === '' ? null : Number(raw)
}
