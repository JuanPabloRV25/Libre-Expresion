<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { Search, Plus } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { ApiError } from '../api/httpClient'
import { usersService, type ApiUser } from '../services/usersService'
import type { UserStatus } from '../types/models'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const users = ref<ApiUser[]>([])
const search = ref('')
const status = ref<'all' | UserStatus>('all')
const loading = ref(false)
const errorMessage = ref('')
let filterTimer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  errorMessage.value = ''
  try {
    users.value = await usersService.list({
      search: search.value,
      status: status.value === 'all' ? undefined : status.value,
    })
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible consultar los usuarios.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
onUnmounted(() => clearTimeout(filterTimer))
watch([search, status], () => {
  clearTimeout(filterTimer)
  filterTimer = setTimeout(load, 250)
})
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Usuarios" title="Usuarios" description="Administra las personas, su estado, acceso y roles asignados.">
      <RouterLink v-if="auth.hasPermission('users.create')" class="button primary" to="/users/new"><Plus :size="18" /> Crear usuario</RouterLink>
    </PageHeader>
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section class="panel table-panel">
      <div class="table-toolbar">
        <label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar usuarios" placeholder="Buscar usuarios" /></label>
        <select v-model="status" aria-label="Filtrar por estado"><option value="all">Todos los estados</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select>
        <span><strong>{{ users.length }}</strong> usuarios</span>
      </div>
      <div class="table-scroll"><table>
        <thead><tr><th>Usuario</th><th>Documento</th><th>Área</th><th>Roles</th><th>Estado</th><th>Acciones</th></tr></thead>
        <tbody>
          <tr v-if="loading"><td class="empty-row" colspan="6">Cargando usuarios…</td></tr>
          <tr v-else-if="users.length === 0"><td class="empty-row" colspan="6">No se encontraron usuarios.</td></tr>
          <template v-else>
            <tr v-for="user in users" :key="user.id">
              <td><div class="person-cell"><span class="avatar">{{ user.firstName[0] }}{{ user.lastName[0] }}</span><span><strong>{{ user.firstName }} {{ user.lastName }}</strong><small>{{ user.email }}</small></span></div></td>
              <td>{{ user.documentNumber }}</td>
              <td>{{ user.area?.name ?? '—' }}</td>
              <td class="role-cell">{{ user.roles.map((role) => role.name).join(', ') || 'Sin rol' }}</td>
              <td><StatusBadge :active="user.isActive" /></td>
              <td><div class="row-actions"><RouterLink :to="`/users/${user.id}`">Ver</RouterLink><RouterLink v-if="auth.hasPermission('users.edit')" :to="`/users/${user.id}/edit`">Editar</RouterLink></div></td>
            </tr>
          </template>
        </tbody>
      </table></div>
    </section>
  </AppLayout>
</template>
