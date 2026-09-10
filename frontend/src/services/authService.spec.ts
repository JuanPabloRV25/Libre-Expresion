import { afterEach, describe, expect, it, vi } from 'vitest'
import { clearAntiforgeryToken } from '../api/httpClient'
import { authService } from './authService'

const user = {
  id: '3f6300de-87b4-4e92-953c-333690981b37',
  documentNumber: '123456789',
  firstName: 'Ada',
  lastName: 'Lovelace',
  email: 'ada@example.test',
  area: null,
  roles: ['Superadmin'],
  permissions: ['users.view'],
  isActive: true,
  mustChangePassword: false,
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  clearAntiforgeryToken()
  vi.unstubAllGlobals()
})

describe('authService real API contract', () => {
  it('logs in with antiforgery and rebuilds the user from /me', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ token: 'csrf-token' }))
      .mockResolvedValueOnce(jsonResponse({ authenticated: true, mustChangePassword: false }))
      .mockResolvedValueOnce(jsonResponse(user))
    vi.stubGlobal('fetch', fetchMock)

    const result = await authService.login('123456789', 'Secret!1')

    expect(result).toEqual({ status: 'success', user })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/auth/login', expect.objectContaining({
      credentials: 'include',
      headers: expect.objectContaining({ 'X-XSRF-TOKEN': 'csrf-token' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/auth/me', expect.objectContaining({ credentials: 'include' }))
  })

  it.each([
    [403, 'inactive_account', 'inactive'],
    [423, 'account_locked', 'locked'],
    [401, 'invalid_credentials', 'invalid'],
  ] as const)('maps API error %s to %s', async (status, code, expected) => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse({ token: 'csrf-token' }))
      .mockResolvedValueOnce(jsonResponse({ code, message: 'error' }, status)))

    await expect(authService.login('123', 'wrong')).resolves.toEqual({ status: expected })
  })

  it('sends the mandatory password change without the temporary password', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ token: 'csrf-token' }))
      .mockResolvedValueOnce(jsonResponse({ succeeded: true }))
    vi.stubGlobal('fetch', fetchMock)

    await authService.changeRequiredPassword('NewSecret!1', 'NewSecret!1')

    const request = fetchMock.mock.calls[1]?.[1] as RequestInit
    expect(JSON.parse(request.body as string)).toEqual({
      newPassword: 'NewSecret!1',
      confirmPassword: 'NewSecret!1',
    })
  })
})
