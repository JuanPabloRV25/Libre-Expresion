import { ApiError } from '../../../api/httpClient'

export type DocumentChecklistStatus = 'pending' | 'attached' | 'notApplicable'
export type OptionalDocumentType = 'purchaseOrder' | 'design'

export function isProductionOrderVersionConflict(error: unknown) {
  return error instanceof ApiError && error.code === 'production_order_version_conflict'
}

export function documentStatusLabel(type: OptionalDocumentType, status: DocumentChecklistStatus) {
  if (status === 'attached') return 'Archivo adjunto'
  if (status === 'notApplicable') {
    return type === 'purchaseOrder' ? 'Marcada como no aplica' : 'Marcado como no requerido'
  }
  return 'Pendiente'
}
