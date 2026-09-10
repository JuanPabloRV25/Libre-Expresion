import type { AuditEvent } from './auditService'

const metadataLabels: Readonly<Record<string, string>> = {
  name: 'Nombre',
  previousName: 'Nombre anterior',
  isActive: 'Estado activo',
  roleIds: 'Roles',
  areaId: 'Área',
  email: 'Correo',
  previousEmail: 'Correo anterior',
  notificationStatus: 'Estado de notificación',
  addedRoleIds: 'Roles agregados',
  removedRoleIds: 'Roles retirados',
  addedCodes: 'Permisos agregados',
  removedCodes: 'Permisos retirados',
}

export function safeAuditMetadataEntries(metadata: AuditEvent['metadata']) {
  if (!metadata) return []
  return Object.entries(metadata)
    .filter(([key, value]) => Boolean(metadataLabels[key]) && typeof value === 'string' && value.trim().length > 0)
    .slice(0, 20)
    .map(([key, value]) => ({ key, label: metadataLabels[key], value }))
}

export function auditResultPresentation(result: string) {
  const normalized = result.trim().toUpperCase()
  if (normalized === 'SUCCESS') return { label: 'SUCCESS', className: 'is-success' }
  if (normalized === 'FAILED') return { label: 'FAILED', className: 'is-failed' }
  if (normalized === 'DENIED') return { label: 'DENIED', className: 'is-denied' }
  return { label: result, className: 'is-neutral' }
}

export function formatAuditDate(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('es-CO', {
    dateStyle: 'medium',
    timeStyle: 'medium',
    timeZone: 'America/Bogota',
  }).format(date)
}
