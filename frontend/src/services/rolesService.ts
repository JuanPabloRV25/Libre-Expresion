import { getJson, patchJson, postJson, putJson } from '../api/httpClient'
import type { UserStatus } from '../types/models'

export interface ApiRole {
  id: string
  name: string
  description: string | null
  isActive: boolean
  isSystem: boolean
  userCount: number
  permissionCodes: string[]
  createdAt: string
  updatedAt: string
}

export interface RoleInput {
  name: string
  description: string | null
}

export interface RoleListFilters {
  search?: string
  status?: UserStatus
}

function list(filters: RoleListFilters = {}): Promise<ApiRole[]> {
  const params = new URLSearchParams()
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.status) params.set('status', filters.status)
  const query = params.size > 0 ? `?${params.toString()}` : ''
  return getJson<ApiRole[]>(`/roles${query}`)
}

export const rolesService = {
  list,
  get: (id: string) => getJson<ApiRole>(`/roles/${id}`),
  create: (input: RoleInput) => postJson<ApiRole>('/roles', input),
  update: (id: string, input: RoleInput) => putJson<ApiRole>(`/roles/${id}`, input),
  setStatus: (id: string, isActive: boolean) => patchJson<ApiRole>(`/roles/${id}/status`, { isActive }),
  setPermissions: (id: string, permissionCodes: string[]) => putJson<ApiRole>(`/roles/${id}/permissions`, { permissionCodes }),
}
