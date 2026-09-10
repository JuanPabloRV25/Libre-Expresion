import { describe, expect, it } from 'vitest'
import { resolveAuthNavigation } from './authGuard'

const state = (overrides: Partial<Parameters<typeof resolveAuthNavigation>[2]> = {}) => ({
  isAuthenticated: true,
  mustChangePassword: false,
  hasPermission: (code?: string) => !code || code === 'users.view',
  ...overrides,
})

describe('authentication navigation guard', () => {
  it('sends anonymous protected navigation to login', () => {
    expect(resolveAuthNavigation('/home', { requiresAuth: true }, state({ isAuthenticated: false }))).toBe('/login')
  })

  it('confines a temporary session to mandatory password change', () => {
    expect(resolveAuthNavigation('/home', { requiresAuth: true }, state({ mustChangePassword: true }))).toBe('/first-login')
  })

  it('denies a route whose effective permission is missing', () => {
    expect(resolveAuthNavigation('/roles', { requiresAuth: true, permission: 'roles.view' }, state())).toBe('/unauthorized')
  })

  it('allows a route backed by an effective permission', () => {
    expect(resolveAuthNavigation('/users', { requiresAuth: true, permission: 'users.view' }, state())).toBe(true)
  })
})
