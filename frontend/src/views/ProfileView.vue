<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { useAuthStore } from '../stores/auth'
import { areasService } from '../services/areasService'
import { rolesService } from '../services/rolesService'
import type { Area, Role } from '../types/models'

const auth = useAuthStore()
const areas = ref<Area[]>([])
const roles = ref<Role[]>([])
onMounted(async () => { ;[areas.value, roles.value] = await Promise.all([areasService.list(), rolesService.list()]) })
const areaName = computed(() => areas.value.find((area) => area.id === auth.currentUser?.areaId)?.name ?? '—')
const roleNames = computed(() => roles.value.filter((role) => auth.currentUser?.roleIds.includes(role.id)).map((role) => role.name).join(', '))
function logout() { auth.logout() }
</script>

<template>
  <AppLayout><PageHeader eyebrow="Portal · Mi perfil" title="Mi perfil" description="Consulta tu información y administra tu propia contraseña." /><section v-if="auth.currentUser" class="profile-card panel"><div class="profile-main"><span class="avatar huge">{{ auth.currentUser.firstName[0] }}{{ auth.currentUser.lastName[0] }}</span><div><h2>{{ auth.fullName }}</h2><p>{{ auth.currentUser.email }}</p><StatusBadge :active="auth.currentUser.status === 'active'" /></div><div class="profile-actions"><RouterLink class="button primary" to="/profile/change-password">Cambiar contraseña</RouterLink><RouterLink class="button secondary" to="/login" @click="logout">Cerrar sesión</RouterLink></div></div><dl class="profile-data"><div><dt>Tipo de documento</dt><dd>{{ auth.currentUser.documentType }}</dd></div><div><dt>Número de documento</dt><dd>{{ auth.currentUser.document }}</dd></div><div><dt>Correo electrónico</dt><dd>{{ auth.currentUser.email }}</dd></div><div><dt>Área</dt><dd>{{ areaName }}</dd></div><div><dt>Rol o roles</dt><dd>{{ roleNames }}</dd></div><div><dt>Permisos habilitados</dt><dd>{{ auth.permissionCodes.length }}</dd></div></dl></section></AppLayout>
</template>

