<script setup lang="ts">
import { useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()
async function logout() {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <AppLayout><PageHeader eyebrow="Portal · Mi perfil" title="Mi perfil" description="Consulta tu información y administra tu propia contraseña." /><section v-if="auth.currentUser" class="profile-card panel"><div class="profile-main"><span class="avatar huge">{{ auth.currentUser.firstName[0] }}{{ auth.currentUser.lastName[0] }}</span><div><h2>{{ auth.fullName }}</h2><p>{{ auth.currentUser.email }}</p><StatusBadge :active="auth.currentUser.isActive" /></div><div class="profile-actions"><RouterLink class="button primary" to="/profile/change-password">Cambiar contraseña</RouterLink><button class="button secondary" type="button" @click="logout">Cerrar sesión</button></div></div><dl class="profile-data"><div><dt>Número de documento</dt><dd>{{ auth.currentUser.documentNumber }}</dd></div><div><dt>Correo electrónico</dt><dd>{{ auth.currentUser.email }}</dd></div><div><dt>Área</dt><dd>{{ auth.currentUser.area?.name ?? '—' }}</dd></div><div><dt>Rol o roles</dt><dd>{{ auth.roleNames }}</dd></div><div><dt>Permisos habilitados</dt><dd>{{ auth.permissionCodes.length }}</dd></div></dl></section></AppLayout>
</template>
