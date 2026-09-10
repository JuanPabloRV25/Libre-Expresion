import { getJson } from '../api/httpClient'

export interface ApiPermission {
  id: string
  code: string
  module: string
  action: string
  displayName: string
  description: string | null
  isActive: boolean
}

export const permissionsService = {
  list: () => getJson<ApiPermission[]>('/permissions'),
}
