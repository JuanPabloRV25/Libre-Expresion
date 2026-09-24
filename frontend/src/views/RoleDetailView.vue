<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { ApiError } from '../api/httpClient'
import { rolesService, type ApiRole } from '../services/rolesService'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const auth = useAuthStore()
const role = ref<ApiRole>()
const errorMessage = ref('')
const permissionNames: Record<string, string> = {
  'areas.view': 'Ver áreas', 'areas.create': 'Crear áreas', 'areas.edit': 'Editar áreas', 'areas.activate': 'Activar o inactivar áreas',
  'users.view': 'Ver usuarios', 'users.create': 'Crear usuarios', 'users.edit': 'Editar usuarios', 'users.activate': 'Activar o inactivar usuarios',
  'users.assign_roles': 'Asignar roles a usuarios', 'users.reset_password': 'Restablecer contraseñas',
  'roles.view': 'Ver roles', 'roles.create': 'Crear roles', 'roles.edit': 'Editar roles', 'roles.activate': 'Activar o inactivar roles',
  'roles.assign_permissions': 'Asignar permisos a roles', 'permissions.view': 'Ver permisos', 'audit.view': 'Ver auditoría',
}
const permissionName = (code: string) => permissionNames[code] ?? 'Acción autorizada'
const moduleNames: Record<string, string> = {
  areas: 'Áreas', users: 'Usuarios', roles: 'Roles', permissions: 'Permisos', audit: 'Auditoría',
}
const visiblePermissionCount = computed(() => (role.value?.permissionCodes ?? []).filter((code) => !code.startsWith('audit.')).length)
const groupedPermissions = computed(() => (role.value?.permissionCodes ?? []).filter((code) => !code.startsWith('audit.')).reduce<Record<string, { code: string; name: string }[]>>((groups, code) => {
  const module = moduleNames[code.split('.')[0] ?? ''] ?? 'Otras acciones'
  ;(groups[module] ??= []).push({ code, name: permissionName(code) })
  return groups
}, {}))

onMounted(async () => {
  try {
    role.value = await rolesService.get(String(route.params.id))
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el rol.'
  }
})
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink>
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section v-if="role" class="detail-hero">
      <div><p class="eyebrow">DETALLE DE ROL</p><h1>{{ role.name }}</h1><p>{{ role.description || 'Sin descripción' }}</p></div>
      <StatusBadge :active="role.isActive" />
      <div class="detail-actions">
        <RouterLink v-if="auth.hasPermission('roles.edit')" class="button secondary" :to="`/roles/${role.id}/edit`">Editar</RouterLink>
        <RouterLink v-if="auth.hasPermission('roles.assign_permissions')" class="button primary" :to="`/roles/${role.id}/permissions`">Gestionar permisos</RouterLink>
      </div>
    </section>
    <div v-if="role" class="detail-grid">
      <section class="panel">
        <p class="eyebrow">PERMISOS ASIGNADOS</p><h3>{{ visiblePermissionCount }} permisos</h3>
        <div v-if="visiblePermissionCount" class="assigned-permission-groups"><details v-for="(items, module) in groupedPermissions" :key="module"><summary><span><strong>{{ module }}</strong><small>{{ items.length }} {{ items.length === 1 ? 'permiso' : 'permisos' }}</small></span><b aria-hidden="true">+</b></summary><div class="assigned-permission-items"><span v-for="permission in items" :key="permission.code">{{ permission.name }}</span></div></details></div><p v-else class="empty-permissions">Este rol todavía no tiene permisos.</p>
      </section>
      <section class="panel"><p class="eyebrow">USUARIOS ASIGNADOS</p><h3>{{ role.userCount }} usuarios</h3><p>Las asignaciones se administran desde el módulo de Usuarios.</p></section>
    </div>
  </AppLayout>
</template>
