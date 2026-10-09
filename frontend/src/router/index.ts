import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { resolveAuthNavigation } from './authGuard'
import LoginView from '../views/LoginView.vue'
import FirstLoginView from '../views/FirstLoginView.vue'
import PasswordUpdatedView from '../views/PasswordUpdatedView.vue'
import ResetPasswordView from '../views/ResetPasswordView.vue'
import HomeView from '../views/HomeView.vue'
import UsersView from '../views/UsersView.vue'
import UserFormView from '../views/UserFormView.vue'
import UserDetailView from '../views/UserDetailView.vue'
import AreasView from '../views/AreasView.vue'
import RolesView from '../views/RolesView.vue'
import RoleFormView from '../views/RoleFormView.vue'
import RoleDetailView from '../views/RoleDetailView.vue'
import RolePermissionsView from '../views/RolePermissionsView.vue'
import PermissionsView from '../views/PermissionsView.vue'
import AuditView from '../views/AuditView.vue'
import ProfileView from '../views/ProfileView.vue'
import ChangePasswordView from '../views/ChangePasswordView.vue'
import UnauthorizedView from '../views/UnauthorizedView.vue'
import CommercialOverviewView from '../features/commercial/production-orders/CommercialOverviewView.vue'
import ProductionOrdersView from '../features/commercial/production-orders/ProductionOrdersView.vue'
import ProductionOrderFormView from '../features/commercial/production-orders/ProductionOrderFormView.vue'
import ProductionOrderDetailView from '../features/commercial/production-orders/ProductionOrderDetailView.vue'
import { SHOW_COMMERCIAL_SUMMARY } from '../features/commercial/featureFlags'
import ReportsView from '../features/commercial/reports/ReportsView.vue'
import ReportRouteView from '../features/commercial/reports/ReportRouteView.vue'
import OpRegisterView from '../features/commercial/reports/OpRegisterView.vue'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    permission?: string
    publicPasswordReset?: boolean
    title?: string
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/login' },
    { path: '/login', component: LoginView, meta: { title: 'Iniciar sesión' } },
    { path: '/first-login', component: FirstLoginView, meta: { title: 'Primer ingreso' } },
    { path: '/password-updated', component: PasswordUpdatedView, meta: { title: 'Contraseña actualizada' } },
    { path: '/home', component: HomeView, meta: { requiresAuth: true, title: 'Inicio' } },
    { path: '/reset-password', component: ResetPasswordView, meta: { publicPasswordReset: true, title: 'Establecer contraseña' } },

    // Phase 1 · Administration
    { path: '/users', component: UsersView, meta: { requiresAuth: true, permission: 'users.view', title: 'Usuarios' } },
    { path: '/users/new', component: UserFormView, meta: { requiresAuth: true, permission: 'users.create', title: 'Crear usuario' } },
    { path: '/users/:id', component: UserDetailView, meta: { requiresAuth: true, permission: 'users.view', title: 'Detalle de usuario' } },
    { path: '/users/:id/edit', component: UserFormView, meta: { requiresAuth: true, permission: 'users.edit', title: 'Editar usuario' } },
    { path: '/areas', component: AreasView, meta: { requiresAuth: true, permission: 'areas.view', title: 'Áreas' } },
    { path: '/roles', component: RolesView, meta: { requiresAuth: true, permission: 'roles.view', title: 'Roles' } },
    { path: '/roles/new', component: RoleFormView, meta: { requiresAuth: true, permission: 'roles.create', title: 'Crear rol' } },
    { path: '/roles/:id', component: RoleDetailView, meta: { requiresAuth: true, permission: 'roles.view', title: 'Detalle de rol' } },
    { path: '/roles/:id/edit', component: RoleFormView, meta: { requiresAuth: true, permission: 'roles.edit', title: 'Editar rol' } },
    { path: '/roles/:id/permissions', component: RolePermissionsView, meta: { requiresAuth: true, permission: 'roles.assign_permissions', title: 'Matriz de permisos' } },
    { path: '/permissions', component: PermissionsView, meta: { requiresAuth: true, permission: 'permissions.view', title: 'Permisos' } },
    { path: '/audit', component: AuditView, meta: { requiresAuth: true, permission: 'audit.view', title: 'Auditoría' } },

    // Phase 2 · Commercial. Kept in its own feature and route namespace.
    {
      path: '/commercial',
      ...(SHOW_COMMERCIAL_SUMMARY
        ? { component: CommercialOverviewView }
        : { redirect: '/commercial/production-orders' }),
      meta: { requiresAuth: true, permission: 'commercial.production_orders.view', title: 'Comercial' },
    },
    { path: '/commercial/production-orders', component: ProductionOrdersView, meta: { requiresAuth: true, permission: 'commercial.production_orders.view', title: 'Órdenes de producción' } },
    { path: '/commercial/reports', redirect: '/commercial/reports/monthly', meta: { requiresAuth: true, permission: 'commercial.reports.view', title: 'Reportes' } },
    { path: '/commercial/reports/monthly', component: ReportsView, meta: { requiresAuth: true, permission: 'commercial.reports.view', title: 'Informe Mensual' } },
    { path: '/commercial/reports/ops', component: OpRegisterView, meta: { requiresAuth: true, permission: 'commercial.reports.view', title: 'Informe OPs' } },
    { path: '/commercial/reports/ventas/:id', component: ReportRouteView, meta: { requiresAuth: true, permission: 'commercial.reports.view', title: 'Informe Mensual' } },
    { path: '/commercial/production-orders/new', component: ProductionOrderFormView, meta: { requiresAuth: true, permission: 'commercial.production_orders.create', title: 'Nueva orden de producción' } },
    { path: '/commercial/production-orders/:id/edit', component: ProductionOrderFormView, meta: { requiresAuth: true, permission: 'commercial.production_orders.edit_commercial', title: 'Editar orden de producción' } },
    { path: '/commercial/production-orders/:id', component: ProductionOrderDetailView, meta: { requiresAuth: true, permission: 'commercial.production_orders.view', title: 'Orden de producción' } },

    { path: '/profile', component: ProfileView, meta: { requiresAuth: true, title: 'Mi perfil' } },
    { path: '/profile/change-password', component: ChangePasswordView, meta: { requiresAuth: true, title: 'Cambiar contraseña' } },
    { path: '/unauthorized', component: UnauthorizedView, meta: { requiresAuth: true, title: 'Acceso no autorizado' } },
    { path: '/:pathMatch(.*)*', redirect: '/home' },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.initialize()
  document.title = `${to.meta.title ?? 'Portal'} · Portal Libre Expresión`
  return resolveAuthNavigation(to.path, to.meta, {
    isAuthenticated: auth.isAuthenticated,
    mustChangePassword: auth.currentUser?.mustChangePassword ?? false,
    hasPermission: auth.hasPermission,
  })
})
