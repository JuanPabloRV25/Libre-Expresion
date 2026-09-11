export interface AuthGuardState {
  isAuthenticated: boolean
  mustChangePassword: boolean
  hasPermission: (code?: string) => boolean
}

export function resolveAuthNavigation(
  path: string,
  meta: { requiresAuth?: boolean; permission?: string; publicPasswordReset?: boolean },
  auth: AuthGuardState,
) {
  if (meta.publicPasswordReset) return true
  if (auth.isAuthenticated && auth.mustChangePassword && path !== '/first-login') return '/first-login'
  if (path === '/first-login' && (!auth.isAuthenticated || !auth.mustChangePassword)) return auth.isAuthenticated ? '/home' : '/login'
  if (meta.requiresAuth && !auth.isAuthenticated) return '/login'
  if (meta.permission && !auth.hasPermission(meta.permission)) return '/unauthorized'
  if (path === '/login' && auth.isAuthenticated) return '/home'
  return true
}
