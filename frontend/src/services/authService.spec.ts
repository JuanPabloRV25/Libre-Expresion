import { describe, expect, it } from 'vitest'
import { authService } from './authService'

describe('authService mock contract', () => {
  it('authenticates the superadmin demo profile with document and password', async () => {
    const result = await authService.login('10000001', '10000001')

    expect(result.status).toBe('success')
    if (result.status === 'success') expect(result.user.demoProfile).toBe('superadmin')
  })

  it('requires a password change for the first-login profile', async () => {
    const result = await authService.login('10000004', '10000004')

    expect(result.status).toBe('first-login')
  })

  it('rejects an inactive profile', async () => {
    const result = await authService.login('10000005', '10000005')

    expect(result.status).toBe('inactive')
  })

  it('rejects invalid credentials', async () => {
    const result = await authService.login('10000001', 'incorrecta')

    expect(result.status).toBe('invalid')
  })
})
