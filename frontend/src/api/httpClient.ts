const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()

export const API_BASE_URL = configuredBaseUrl || '/api'

export class ApiError extends Error {
  readonly status: number
  readonly code?: string

  constructor(
    status: number,
    code?: string,
    message = `HTTP ${status}`,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

let antiforgeryToken: string | null = null

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.ok) return response.json() as Promise<T>

  const body = await response.json().catch(() => null) as { code?: string; message?: string } | null
  throw new ApiError(response.status, body?.code, body?.message)
}

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })
  return parseResponse<T>(response)
}

async function getAntiforgeryToken(): Promise<string> {
  if (antiforgeryToken) return antiforgeryToken
  const response = await getJson<{ token: string }>('/auth/csrf')
  antiforgeryToken = response.token
  return response.token
}

export function clearAntiforgeryToken() {
  antiforgeryToken = null
}

export async function postJson<T>(path: string, body: unknown = {}): Promise<T> {
  const token = await getAntiforgeryToken()
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    body: JSON.stringify(body),
  })
  return parseResponse<T>(response)
}
