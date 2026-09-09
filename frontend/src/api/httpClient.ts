const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()

export const API_BASE_URL = configuredBaseUrl || '/api'

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, { headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(`HTTP ${response.status}`)
  return response.json() as Promise<T>
}

