import { getJson, patchJson, postJson, putJson } from '../api/httpClient'
import type { UserStatus } from '../types/models'
import type { NotificationStatus } from './notificationMessages'

export interface ApiUserArea {
  id: string
  name: string
  isActive: boolean
}

export interface ApiUserRole {
  id: string
  name: string
  isActive: boolean
  isSystem: boolean
}

export interface ApiUser {
  id: string
  documentNumber: string
  firstName: string
  lastName: string
  email: string
  area: ApiUserArea | null
  advisorCode: string | null
  roles: ApiUserRole[]
  isActive: boolean
  mustChangePassword: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateUserInput {
  documentNumber: string
  firstName: string
  lastName: string
  email: string
  areaId: string | null
  advisorCode: string | null
  roleIds: string[]
  isActive: boolean
}

export interface UpdateUserInput {
  firstName: string
  lastName: string
  email: string
  areaId: string | null
  advisorCode: string | null
}

export interface UserListFilters {
  search?: string
  status?: UserStatus
  areaId?: string
  roleId?: string
}

export interface UserCreatedResponse {
  user: ApiUser
  notificationStatus: NotificationStatus
}

export interface PasswordResetResponse {
  passwordReset: boolean
  notificationStatus: NotificationStatus
}

function list(filters: UserListFilters = {}): Promise<ApiUser[]> {
  const params = new URLSearchParams()
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.status) params.set('status', filters.status)
  if (filters.areaId) params.set('areaId', filters.areaId)
  if (filters.roleId) params.set('roleId', filters.roleId)
  const query = params.size > 0 ? `?${params.toString()}` : ''
  return getJson<ApiUser[]>(`/users${query}`)
}

export const usersService = {
  list,
  get: (id: string) => getJson<ApiUser>(`/users/${id}`),
  create: (input: CreateUserInput) => postJson<UserCreatedResponse>('/users', input),
  update: (id: string, input: UpdateUserInput) => putJson<ApiUser>(`/users/${id}`, input),
  setStatus: (id: string, isActive: boolean) => patchJson<ApiUser>(`/users/${id}/status`, { isActive }),
  setRoles: (id: string, roleIds: string[]) => putJson<ApiUser>(`/users/${id}/roles`, { roleIds }),
  resetPassword: (id: string) => postJson<PasswordResetResponse>(`/users/${id}/reset-password`),
}
