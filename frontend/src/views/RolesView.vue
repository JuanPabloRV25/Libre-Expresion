<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Plus, Search } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { rolesService } from '../services/rolesService'
import { usersService } from '../services/usersService'
import type { PortalUser, Role } from '../types/models'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const roles = ref<Role[]>([])
const users = ref<PortalUser[]>([])
const search = ref('')
onMounted(async () => { ;[roles.value, users.value] = await Promise.all([rolesService.list(), usersService.list()]) })
const filtered = computed(() => roles.value.filter((role) => `${role.name} ${role.description}`.toLowerCase().includes(search.value.toLowerCase())))
const userCount = (id: number) => users.value.filter((user) => user.roleIds.includes(id)).length
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Roles" title="Roles" description="Define responsabilidades flexibles y asocia permisos a cada rol."><RouterLink v-if="auth.hasPermission('roles.create')" class="button primary" to="/roles/new"><Plus :size="18" /> Crear rol</RouterLink></PageHeader>
    <section class="info-banner"><strong>Los roles no están ligados a cargos fijos</strong><p>Puedes crear cualquier rol nuevo. El acceso se determina por los permisos que tenga asociados.</p></section>
    <section class="panel table-panel"><div class="table-toolbar"><label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar roles" placeholder="Buscar roles" /></label><span><strong>{{ filtered.length }}</strong> roles</span></div><div class="table-scroll"><table><thead><tr><th>Rol</th><th>Usuarios</th><th>Permisos</th><th>Estado</th><th>Acciones</th></tr></thead><tbody><tr v-for="role in filtered" :key="role.id"><td><strong>{{ role.name }}</strong><small class="cell-description">{{ role.description }}</small></td><td>{{ userCount(role.id) }}</td><td>{{ role.permissionCodes.length }} asignados</td><td><StatusBadge :active="role.status === 'active'" /></td><td><div class="row-actions"><RouterLink :to="`/roles/${role.id}`">Ver</RouterLink><RouterLink v-if="auth.hasPermission('roles.edit')" :to="`/roles/${role.id}/edit`">Editar</RouterLink><RouterLink v-if="auth.hasPermission('roles.assign_permissions')" :to="`/roles/${role.id}/permissions`">Permisos</RouterLink></div></td></tr></tbody></table></div></section>
  </AppLayout>
</template>
