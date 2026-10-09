import { API_BASE_URL, deleteJson, getJson, mutateForm, postJson, putJson } from '../../../api/httpClient'
import type { CommercialOrderInput, ProductionOrderDetail, ProductionOrderReviewer, ProductionOrderInput, ProductionOrderStatus, ProductionOrderSummary, QuotationPreview } from './types'

export interface OrderFilters { search?: string; status?: ProductionOrderStatus | ''; assignedToMe?: boolean }

function list(filters: OrderFilters = {}) {
  const params = new URLSearchParams()
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.status) params.set('status', filters.status)
  if (filters.assignedToMe) params.set('assignedToMe', 'true')
  return getJson<ProductionOrderSummary[]>(`/commercial/production-orders${params.size ? `?${params}` : ''}`)
}

export const productionOrdersService = {
  list,
  get: (id: string) => getJson<ProductionOrderDetail>(`/commercial/production-orders/${id}`),
  reviewers: (id: string) => getJson<ProductionOrderReviewer[]>(`/commercial/production-orders/${id}/reviewers`),
  create: (input: CommercialOrderInput) => postJson<ProductionOrderDetail>('/commercial/production-orders', input),
  updateCommercial: (id: string, input: CommercialOrderInput) => putJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/commercial`, input),
  previewQuotation: (file: File) => mutateForm<QuotationPreview>('POST', '/commercial/production-orders/quotation-preview', fileForm(file)),
  importQuotation: (file: File, itemIndex: number, optionIndex: number, commercial: CommercialOrderInput, relatedOrderId?: string) => mutateForm<ProductionOrderDetail>('POST', '/commercial/production-orders/import-quotation', fileForm(file, { itemIndex, optionIndex, commercial: JSON.stringify(commercial), ...(relatedOrderId ? { relatedOrderId } : {}) })),
  replaceQuotation: (id: string, version: number, file: File, itemIndex: number, optionIndex: number) => mutateForm<ProductionOrderDetail>('PUT', `/commercial/production-orders/${id}/quotation`, fileForm(file, { version, itemIndex, optionIndex })),
  addDocument: (id: string, version: number, type: 'purchaseOrder' | 'design', file: File) => mutateForm<ProductionOrderDetail>('POST', `/commercial/production-orders/${id}/documents/${type}`, fileForm(file, { version })),
  setDocumentApplicability: (id: string, version: number, type: 'purchaseOrder' | 'design', notApplicable: boolean) => putJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/documents/${type}/applicability`, { version, notApplicable }),
  deleteDocument: (id: string, documentId: string, version: number) => deleteJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/documents/${documentId}?version=${version}`),
  discardDraft: (id: string, version: number) => deleteJson<{ discarded: boolean }>(`/commercial/production-orders/${id}/draft?version=${version}`),
  downloadUrl: (id: string, documentId: string) => `${API_BASE_URL}/commercial/production-orders/${id}/documents/${documentId}`,
  submitForReview: (id: string, version: number, reviewerUserId: string) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/submit-for-review`, { version, reviewerUserId }),
  approveReview: (id: string, version: number, customerOrderNumber: string) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/submit`, { version, customerOrderNumber }),
  returnForCorrection: (id: string, version: number, reason: string) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/return-for-correction`, { version, reason }),
  duplicate: (id: string) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/duplicate`),
  receive: (id: string, version: number) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/receive`, { version }),
  updateProduction: (id: string, input: ProductionOrderInput) => putJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/production`, input),
  complete: (id: string, version: number) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/complete`, { version }),
  cancel: (id: string, version: number, reason: string) => postJson<ProductionOrderDetail>(`/commercial/production-orders/${id}/cancel`, { version, reason }),
}

function fileForm(file: File, values: Record<string, string | number> = {}) {
  const data = new FormData()
  data.append('file', file)
  Object.entries(values).forEach(([key, value]) => data.append(key, String(value)))
  return data
}
