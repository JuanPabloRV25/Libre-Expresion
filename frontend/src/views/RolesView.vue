<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { Plus, Search } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import { ApiError } from '../api/httpClient'
import { rolesService, type ApiRole } from '../services/rolesService'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const roles = ref<ApiRole[]>([])
const search = ref('')
const loading = ref(false)
const errorMessage = ref('')
const toggleTarget = ref<ApiRole>()
let searchTimer: ReturnType<typeof setTimeout> | undefined

function describeError(error: unknown) {
  return error instanceof ApiError ? error.message : 'No fue posible consultar los roles.'
}

async function load() {
  loading.value = true
  errorMessage.value = ''
  try {
    roles.value = await rolesService.list({ search: search.value })
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    loading.value = false
  }
}

onMounted(load)
onUnmounted(() => clearTimeout(searchTimer))
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(load, 250)
})

async function toggleStatus() {
  if (!toggleTarget.value) return
  errorMessage.value = ''
  try {
    await rolesService.setStatus(toggleTarget.value.id, !toggleTarget.value.isActive)
    await load()
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    toggleTarget.value = undefined
  }
}
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Roles" title="Roles" description="Define responsabilidades flexibles y asocia permisos a cada rol.">
      <RouterLink v-if="auth.hasPermission('roles.create')" class="button primary" to="/roles/new"><Plus :size="18" /> Crear rol</RouterLink>
    </PageHeader>
    <section class="info-banner"><strong>Los roles no están ligados a cargos fijos</strong><p>Puedes crear cualquier rol nuevo. El acceso se determina por los permisos que tenga asociados.</p></section>
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section class="panel table-panel">
      <div class="table-toolbar">
        <label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar roles" placeholder="Buscar roles" /></label>
        <span><strong>{{ roles.length }}</strong> roles</span>
      </div>
      <div class="table-scroll"><table>
        <thead><tr><th>Rol</th><th>Usuarios</th><th>Permisos</th><th>Estado</th><th>Acciones</th></tr></thead>
        <tbody>
          <tr v-if="loading"><td class="empty-row" colspan="5">Cargando roles…</td></tr>
          <tr v-else-if="roles.length === 0"><td class="empty-row" colspan="5">No se encontraron roles.</td></tr>
          <template v-else>
            <tr v-for="role in roles" :key="role.id">
              <td><strong>{{ role.name }}</strong><small class="cell-description">{{ role.description || 'Sin descripción' }}</small></td>
              <td>{{ role.userCount }}</td>
              <td>{{ role.permissionCodes.length }} asignados</td>
              <td><StatusBadge :active="role.isActive" /></td>
              <td><div class="row-actions">
                <RouterLink :to="`/roles/${role.id}`">Ver</RouterLink>
                <RouterLink v-if="auth.hasPermission('roles.edit')" :to="`/roles/${role.id}/edit`">Editar</RouterLink>
                <RouterLink v-if="auth.hasPermission('roles.assign_permissions')" :to="`/roles/${role.id}/permissions`">Permisos</RouterLink>
                <button v-if="auth.hasPermission('roles.activate') && !role.isSystem" @click="toggleTarget = role">{{ role.isActive ? 'Inactivar' : 'Activar' }}</button>
              </div></td>
            </tr>
          </template>
        </tbody>
      </table></div>
    </section>
    <ConfirmDialog
      :open="Boolean(toggleTarget)"
      :title="`${toggleTarget?.isActive ? 'Inactivar' : 'Activar'} rol`"
      :description="`Confirma el cambio de estado para ${toggleTarget?.name ?? ''}.`"
      confirm-label="Confirmar"
      @close="toggleTarget = undefined"
      @confirm="toggleStatus"
    />
  </AppLayout>
</template>
