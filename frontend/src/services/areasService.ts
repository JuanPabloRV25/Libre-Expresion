import { getJson, patchJson, postJson, putJson } from '../api/httpClient'
import type { UserStatus } from '../types/models'

export interface ApiArea {
  id: string
  name: string
  description: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface AreaInput {
  name: string
  description: string | null
}

export interface AreaListFilters {
  search?: string
  status?: UserStatus
}

function list(filters: AreaListFilters = {}): Promise<ApiArea[]> {
  const params = new URLSearchParams()
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.status) params.set('status', filters.status)
  const query = params.size > 0 ? `?${params.toString()}` : ''
  return getJson<ApiArea[]>(`/areas${query}`)
}

export const areasService = {
  list,
  get: (id: string) => getJson<ApiArea>(`/areas/${id}`),
  create: (input: AreaInput) => postJson<ApiArea>('/areas', input),
  update: (id: string, input: AreaInput) => putJson<ApiArea>(`/areas/${id}`, input),
  setStatus: (id: string, isActive: boolean) => patchJson<ApiArea>(`/areas/${id}/status`, { isActive }),
}
