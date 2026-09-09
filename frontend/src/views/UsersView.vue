<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Search, Plus } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { usersService } from '../services/usersService'
import { areasService } from '../services/areasService'
import { rolesService } from '../services/rolesService'
import type { Area, PortalUser, Role } from '../types/models'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const users = ref<PortalUser[]>([])
const areas = ref<Area[]>([])
const roles = ref<Role[]>([])
const search = ref('')
const status = ref('all')

onMounted(async () => {
  ;[users.value, areas.value, roles.value] = await Promise.all([usersService.list(), areasService.list(), rolesService.list()])
})

const filtered = computed(() => users.value.filter((user) => {
  const term = search.value.toLowerCase()
  const matchesTerm = `${user.firstName} ${user.lastName} ${user.document} ${user.email}`.toLowerCase().includes(term)
  return matchesTerm && (status.value === 'all' || user.status === status.value)
}))
const areaName = (id: number) => areas.value.find((area) => area.id === id)?.name ?? '—'
const roleNames = (ids: number[]) => ids.map((id) => roles.value.find((role) => role.id === id)?.name).filter(Boolean).join(', ') || 'Sin rol'
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Usuarios" title="Usuarios" description="Administra las personas, su estado, acceso y roles asignados.">
      <RouterLink v-if="auth.hasPermission('users.create')" class="button primary" to="/users/new"><Plus :size="18" /> Crear usuario</RouterLink>
    </PageHeader>
    <section class="panel table-panel">
      <div class="table-toolbar">
        <label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar usuarios" placeholder="Buscar usuarios" /></label>
        <select v-model="status" aria-label="Filtrar por estado"><option value="all">Todos los estados</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select>
        <span><strong>{{ filtered.length }}</strong> usuarios <small>Datos ficticios del prototipo</small></span>
      </div>
      <div class="table-scroll"><table><thead><tr><th>Usuario</th><th>Documento</th><th>Área</th><th>Roles</th><th>Estado</th><th>Acciones</th></tr></thead><tbody>
        <tr v-for="user in filtered" :key="user.id"><td><div class="person-cell"><span class="avatar">{{ user.firstName[0] }}{{ user.lastName[0] }}</span><span><strong>{{ user.firstName }} {{ user.lastName }}</strong><small>{{ user.email }}</small></span></div></td><td>{{ user.documentType }} {{ user.document }}</td><td>{{ areaName(user.areaId) }}</td><td class="role-cell">{{ roleNames(user.roleIds) }}</td><td><StatusBadge :active="user.status === 'active'" /></td><td><div class="row-actions"><RouterLink :to="`/users/${user.id}`">Ver</RouterLink><RouterLink v-if="auth.hasPermission('users.edit')" :to="`/users/${user.id}/edit`">Editar</RouterLink></div></td></tr>
      </tbody></table></div>
      <p v-if="!filtered.length" class="empty-row">No se encontraron usuarios con esos criterios.</p>
    </section>
  </AppLayout>
</template>
