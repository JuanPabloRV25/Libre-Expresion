export type SidebarIconName = 'home' | 'users' | 'areas' | 'roles' | 'permissions' | 'audit' | 'emails'

export interface SidebarItem {
  label: string
  to: string
  icon: SidebarIconName
  permission?: string
  demo?: boolean
}

export const sidebarItems: readonly SidebarItem[] = [
  { label: 'Inicio', to: '/home', icon: 'home' },
  { label: 'Usuarios', to: '/users', icon: 'users', permission: 'users.view' },
  { label: 'Áreas', to: '/areas', icon: 'areas', permission: 'areas.view' },
  { label: 'Roles', to: '/roles', icon: 'roles', permission: 'roles.view' },
  { label: 'Permisos', to: '/permissions', icon: 'permissions', permission: 'permissions.view' },
  { label: 'Auditoría', to: '/audit', icon: 'audit', permission: 'audit.view' },
  { label: 'Correos simulados', to: '/demo/emails', icon: 'emails', permission: 'users.view', demo: true },
]

export function visibleSidebarItems(hasPermission: (permission?: string) => boolean) {
  return sidebarItems.filter((item) => hasPermission(item.permission))
}
