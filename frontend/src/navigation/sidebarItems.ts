export type SidebarIconName = 'home' | 'users' | 'areas' | 'roles' | 'permissions' | 'audit'

export interface SidebarItem {
  label: string
  to: string
  icon: SidebarIconName
  permission?: string
  hidden?: boolean
}

export const sidebarItems: readonly SidebarItem[] = [
  { label: 'Inicio', to: '/home', icon: 'home' },
  { label: 'Usuarios', to: '/users', icon: 'users', permission: 'users.view' },
  { label: 'Áreas', to: '/areas', icon: 'areas', permission: 'areas.view' },
  { label: 'Roles', to: '/roles', icon: 'roles', permission: 'roles.view' },
  { label: 'Permisos', to: '/permissions', icon: 'permissions', permission: 'permissions.view' },
  { label: 'Auditoría', to: '/audit', icon: 'audit', permission: 'audit.view', hidden: true },
]

export function visibleSidebarItems(hasPermission: (permission?: string) => boolean) {
  return sidebarItems.filter((item) => !item.hidden && hasPermission(item.permission))
}
