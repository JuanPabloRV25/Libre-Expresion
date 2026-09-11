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

  it('allows the public password link for anonymous and pending sessions', () => {
    const meta = { publicPasswordReset: true }
    expect(resolveAuthNavigation('/reset-password', meta, state({ isAuthenticated: false }))).toBe(true)
    expect(resolveAuthNavigation('/reset-password', meta, state({ mustChangePassword: true }))).toBe(true)
  })

  it('denies a route whose effective permission is missing', () => {
    expect(resolveAuthNavigation('/roles', { requiresAuth: true, permission: 'roles.view' }, state())).toBe('/unauthorized')
  })

  it('allows a route backed by an effective permission', () => {
    expect(resolveAuthNavigation('/users', { requiresAuth: true, permission: 'users.view' }, state())).toBe(true)
  })

  it('protects direct audit navigation with audit.view', () => {
    expect(resolveAuthNavigation('/audit', { requiresAuth: true, permission: 'audit.view' }, state())).toBe('/unauthorized')
    expect(resolveAuthNavigation('/audit', { requiresAuth: true, permission: 'audit.view' }, state({
      hasPermission: (code?: string) => !code || code === 'audit.view',
    }))).toBe(true)
  })
})
