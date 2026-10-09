export interface OpRecord {
  id: string; number: string; code: string; client: string; product: string
  orderVersion: number | null; capturedAt: string; origin: string
  registryVersion?: number; previousRecordId?: string | null
  data: { cells: string[]; portalCode: string | null }
}
export interface SalesDetail {
  id: string; sourceId: string; sourceFile: string; sheet: string; sourceRow: number
  number: string; date: string; client: string; term: string; rawAmount: number; detail: string
  line: string; seller: string; managerOp: string; documentId: string; matchKey: string
  selectedOpId: string | null; selectedOpNumber: string; selectedProduct: string
  usePortalCode: boolean
  manualGroup: string | null; reviewed: boolean; excluded: boolean; reason: string
}
export interface GroupEdit {
  key: string; factura: string; amount: number | null; client: string | null
  line: string | null; seller: string | null; reason: string
}
export interface SalesDocument {
  id: string; number: string; client: string; sourceAmount: number | null
  confirmedAmount: number | null; reason: string
}
export interface SalesGroup {
  key: string; documentId: string; number: string; date: string; op: string; product: string
  client: string; term: string; amount: number | null; factura: string; line: string; seller: string
  details: string[]; detailIds: string[]; issues: string[]; modified: boolean
  amountMode?: 'automatic' | 'manual' | 'validation' | 'legacy'; opCount?: number
}
export interface ReportSummary {
  id: string; name: string; version: number; updatedAt: string; lastExportedVersion: number | null
}
export interface SalesReport extends ReportSummary {
  data: { currentSourceId: string; sourceFile: string; sha256: string; details: SalesDetail[]
    documents: SalesDocument[]; edits: GroupEdit[]; unappliedEdits: GroupEdit[]; warnings: string[]
    sourceWarningEvidence?: { warning: string; rows: ReviewSourceRow[] }[]
    preparation?: ReportPreparation }
  groups: SalesGroup[]
  controls: { id: string; number: string; client: string; expected: number | null; distributed: number; difference: number | null }[]
  canExport: boolean
  canExportDraft?: boolean; canApprove?: boolean; canExportFinal?: boolean
}
export interface PreparedRowEdit {
  key: string; op?: string; factura?: string; number?: string; date?: string; client?: string
  term?: string; amount?: number | null; setAmount?: boolean; detail?: string; line?: string
  seller?: string; historyIds?: string[]; historyReferenceId?: string; restore?: boolean
}
export interface ReportChange {
  id: string; rowKey: string | null; kind: string; field: string; before: string[]; after: string
  reason: string; detailIds: string[]; historyIds: string[]; actor?: string | null; manual: boolean
  reportVersion?: number; occurredAt?: string | null
}
export interface ReportPreparation {
  ruleVersion: number; status: 'generated' | 'generated_with_observations'; historyFingerprint: string
  history: OpRecord[]; automaticRows: SalesGroup[]; rows: SalesGroup[]
  changes: ReportChange[]; rowEdits: PreparedRowEdit[]
  review?: ReportReview | null
}
export type ReviewAction = 'select_candidates' | 'set_manual_op' | 'keep_na'
export interface ReviewCandidate { historyId: string; number: string; matchedFields: string[] }
export interface ReviewEvidence {
  detailId: string; original: string; segment: string; normalizedProduct: string; candidates: ReviewCandidate[]
}
export interface ReviewSourceRow {
  sourceFile: string; sheet: string; sourceRow: number
  cells: { column: string; header: string; value: string }[]
}
export interface ReviewFinding {
  id: string; code: string; field: string; rule: string; initialClassification: 'automatic' | 'validation' | 'conflict'
  resolution: 'pending' | 'resolved'; provenance: 'automatic' | 'human'; proposal: string; reason: string
  detailIds: string[]; evidence: ReviewEvidence[]; allowedActions: ReviewAction[]; decisionId?: string | null
  sourceRows?: ReviewSourceRow[]
}
export interface ReviewCase {
  id: string; rowKey: string | null; scope: 'row' | 'file'; classification: 'automatic' | 'validation' | 'conflict'
  findings: ReviewFinding[]
}
export interface ReviewDecision {
  id: string; caseId: string; findingIds: string[]; action: ReviewAction | 'field_edit'; before: string; after: string
  historyIds: string[]; actor: string; occurredAt: string; reportVersion: number; contentFingerprint: string
}
export interface ReviewApproval { actor: string; occurredAt: string; reportVersion: number; contentFingerprint: string; valid: boolean }
export interface ReviewSummary {
  sourceRows: number; finalRows: number; automaticRows: number; humanResolvedRows: number
  validationRows: number; conflictRows: number; filePendingCases: number; pendingCases: number; pendingFindings: number
}
export interface ReportReview {
  schemaVersion: number; policyVersion: number; cases: ReviewCase[]; decisions: ReviewDecision[]
  approvals: ReviewApproval[]; summary: ReviewSummary
}
export interface ReviewCommand {
  caseId: string; findingIds: string[]; action: ReviewAction; historyIds?: string[]; op?: string
}
export interface ReplacementPreview {
  fileName: string; identical: boolean; retained: number; added: number; removed: number; preview: SalesReport
}
