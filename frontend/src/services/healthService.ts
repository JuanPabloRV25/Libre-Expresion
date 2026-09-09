import { getJson } from '../api/httpClient'

export interface HealthResponse {
  status: string
}

export const healthService = {
  check: () => getJson<HealthResponse>('/health'),
}

