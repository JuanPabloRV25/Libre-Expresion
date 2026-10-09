import { SHOW_COMMERCIAL_SUMMARY } from '../features/commercial/featureFlags'

export type SidebarIconName =
  | 'home'
  | 'users'
  | 'areas'
  | 'roles'
  | 'permissions'
  | 'audit'
  | 'commercial'
  | 'orders'
  | 'reports'
  | 'folder'
  | 'administration'

export interface SidebarItem {
  label: string
  to: string
  icon: SidebarIconName
  permission?: string
  visible?: boolean
  activePrefixes?: readonly string[]
}

export interface SidebarFolder {
  id: string
  label: string
  icon: SidebarIconName
  permission?: string
  visible?: boolean
  routePrefix: string
  children: readonly SidebarItem[]
}

export type SidebarEntry = SidebarItem | SidebarFolder

export interface SidebarGroup {
  id: 'administration' | 'commercial'
  label: string
  icon: SidebarIconName
  items: readonly SidebarEntry[]
}

export const standaloneSidebarItems: readonly SidebarItem[] = [
  { label: 'Inicio', to: '/home', icon: 'home' },
]

export const sidebarGroups: readonly SidebarGroup[] = [
  {
    id: 'administration',
    label: 'Administración',
    icon: 'administration',
    items: [
      { label: 'Usuarios', to: '/users', icon: 'users', permission: 'users.view' },
      { label: 'Áreas', to: '/areas', icon: 'areas', permission: 'areas.view' },
      { label: 'Roles', to: '/roles', icon: 'roles', permission: 'roles.view' },
      { label: 'Permisos', to: '/permissions', icon: 'permissions', permission: 'permissions.view' },
      { label: 'Auditoría', to: '/audit', icon: 'audit', permission: 'audit.view' },
    ],
  },
  {
    id: 'commercial',
    label: 'Comercial',
    icon: 'commercial',
    items: [
      { label: 'Resumen comercial', to: '/commercial', icon: 'commercial', permission: 'commercial.production_orders.view', visible: SHOW_COMMERCIAL_SUMMARY },
      { label: 'Órdenes de producción', to: '/commercial/production-orders', icon: 'orders', permission: 'commercial.production_orders.view' },
      {
        id: 'reports',
        label: 'Reportes',
        icon: 'folder',
        permission: 'commercial.reports.view',
        routePrefix: '/commercial/reports',
        children: [
          { label: 'Informe Mensual', to: '/commercial/reports/monthly', icon: 'reports', permission: 'commercial.reports.view', activePrefixes: ['/commercial/reports/ventas'] },
          { label: 'Informe OPs', to: '/commercial/reports/ops', icon: 'orders', permission: 'commercial.reports.view' },
        ],
      },
    ],
  },
]

export function visibleSidebarGroups(hasPermission: (permission?: string) => boolean): SidebarGroup[] {
  return sidebarGroups
    .map((group) => ({
      ...group,
      items: group.items
        .filter((item) => item.visible !== false && hasPermission(item.permission))
        .map((item) => 'children' in item
          ? { ...item, children: item.children.filter((child) => child.visible !== false && hasPermission(child.permission)) }
          : item)
        .filter((item) => !('children' in item) || item.children.length > 0),
    }))
    .filter((group) => group.items.length > 0)
}

export function visibleStandaloneSidebarItems(hasPermission: (permission?: string) => boolean) {
  return standaloneSidebarItems.filter((item) => item.visible !== false && hasPermission(item.permission))
}

// Kept as a small compatibility helper for consumers that need a flat list.
export function visibleSidebarItems(hasPermission: (permission?: string) => boolean) {
  return [
    ...visibleStandaloneSidebarItems(hasPermission),
    ...visibleSidebarGroups(hasPermission).flatMap((group) => group.items.flatMap((item) => 'children' in item ? item.children : [item])),
  ]
}
