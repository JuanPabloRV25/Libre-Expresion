<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { rolesService } from '../services/rolesService'
import { permissionsService } from '../services/permissionsService'
import { usersService } from '../services/usersService'
import type { Permission, PortalUser, Role } from '../types/models'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const auth = useAuthStore()
const role = ref<Role>()
const permissions = ref<Permission[]>([])
const users = ref<PortalUser[]>([])
onMounted(async () => { ;[role.value, permissions.value, users.value] = await Promise.all([rolesService.get(Number(route.params.id)), permissionsService.list(), usersService.list()]) })
const assigned = computed(() => permissions.value.filter((permission) => role.value?.permissionCodes.includes(permission.code)))
const assignedUsers = computed(() => users.value.filter((user) => user.roleIds.includes(role.value?.id ?? -1)))
</script>

<template>
  <AppLayout><RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink><section v-if="role" class="detail-hero"><div><p class="eyebrow">DETALLE DE ROL</p><h1>{{ role.name }}</h1><p>{{ role.description }}</p></div><StatusBadge :active="role.status === 'active'" /><div class="detail-actions"><RouterLink v-if="auth.hasPermission('roles.edit')" class="button secondary" :to="`/roles/${role.id}/edit`">Editar</RouterLink><RouterLink v-if="auth.hasPermission('roles.assign_permissions')" class="button primary" :to="`/roles/${role.id}/permissions`">Gestionar permisos</RouterLink></div></section><div v-if="role" class="detail-grid"><section class="panel"><p class="eyebrow">PERMISOS ASIGNADOS</p><h3>{{ assigned.length }} permisos</h3><div class="stack-list"><div v-for="permission in assigned" :key="permission.code"><strong>{{ permission.name }}</strong><small>{{ permission.code }}</small></div><p v-if="!assigned.length">Este rol todavía no tiene permisos.</p></div></section><section class="panel"><p class="eyebrow">USUARIOS ASIGNADOS</p><h3>{{ assignedUsers.length }} usuarios</h3><div class="stack-list"><div v-for="user in assignedUsers" :key="user.id"><strong>{{ user.firstName }} {{ user.lastName }}</strong><small>{{ user.email }}</small></div></div></section></div></AppLayout>
</template>
