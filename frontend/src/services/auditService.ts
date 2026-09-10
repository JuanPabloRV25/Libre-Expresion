import { getJson } from '../api/httpClient'

export interface AuditActor {
  id: string
  name: string
}

export interface AuditEvent {
  id: string
  action: string
  entityType: string | null
  entityId: string | null
  result: string
  occurredAt: string
  correlationId: string | null
  actor: AuditActor | null
  metadata: Record<string, string> | null
  userAgent: string | null
}

export interface AuditPage {
  items: AuditEvent[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface AuditListFilters {
  page?: number
  pageSize?: number
  action?: string
  entityType?: string
  result?: string
  actorUserId?: string
  dateFrom?: string
  dateTo?: string
  search?: string
}

function list(filters: AuditListFilters = {}): Promise<AuditPage> {
  const params = new URLSearchParams({
    page: String(filters.page ?? 1),
    pageSize: String(filters.pageSize ?? 25),
  })

  for (const [key, value] of Object.entries({
    action: filters.action,
    entityType: filters.entityType,
    result: filters.result,
    actorUserId: filters.actorUserId,
    dateFrom: filters.dateFrom,
    dateTo: filters.dateTo,
    search: filters.search,
  })) {
    if (value?.trim()) params.set(key, value.trim())
  }

  return getJson<AuditPage>(`/audit?${params.toString()}`)
}

export const auditService = { list }
