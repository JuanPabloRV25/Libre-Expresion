<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import { usersService } from '../services/usersService'
import { areasService } from '../services/areasService'
import { rolesService } from '../services/rolesService'
import type { Area, PortalUser, Role } from '../types/models'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const auth = useAuthStore()
const user = ref<PortalUser>()
const areas = ref<Area[]>([])
const roles = ref<Role[]>([])
const resetOpen = ref(false)
const statusOpen = ref(false)
const notice = ref('')
onMounted(async () => {
  ;[user.value, areas.value, roles.value] = await Promise.all([usersService.get(Number(route.params.id)), areasService.list(), rolesService.list()])
})
const fullName = computed(() => user.value ? `${user.value.firstName} ${user.value.lastName}` : '')
const area = computed(() => areas.value.find((item) => item.id === user.value?.areaId)?.name ?? '—')
const assignedRoles = computed(() => roles.value.filter((role) => user.value?.roleIds.includes(role.id)))
async function resetPassword() {
  if (user.value) await usersService.resetPassword(user.value.id)
  resetOpen.value = false
  notice.value = 'Contraseña restablecida. Se generó una notificación simulada y el próximo ingreso exigirá cambio.'
}
async function toggleStatus() {
  if (user.value) user.value = await usersService.toggleStatus(user.value.id)
  statusOpen.value = false
}
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/users">← Volver a usuarios</RouterLink>
    <section v-if="user" class="detail-hero"><span class="avatar huge">{{ user.firstName[0] }}{{ user.lastName[0] }}</span><div><p class="eyebrow">DETALLE DE USUARIO</p><h1>{{ fullName }}</h1><p>{{ user.email }}</p></div><StatusBadge :active="user.status === 'active'" /><div class="detail-actions"><button v-if="auth.hasPermission('users.reset_password')" class="button secondary" @click="resetOpen = true">Restablecer contraseña</button><RouterLink v-if="auth.hasPermission('users.edit')" class="button secondary" :to="`/users/${user.id}/edit`">Editar</RouterLink><button v-if="auth.hasPermission('users.activate')" class="button" :class="user.status === 'active' ? 'danger ghost' : 'primary'" @click="statusOpen = true">{{ user.status === 'active' ? 'Inactivar' : 'Activar' }}</button></div></section>
    <p v-if="notice" class="notice success" role="status">{{ notice }}</p>
    <div v-if="user" class="detail-grid">
      <section class="panel"><p class="eyebrow">INFORMACIÓN GENERAL</p><h3>Datos del usuario</h3><dl class="data-list"><div><dt>Tipo de documento</dt><dd>{{ user.documentType }}</dd></div><div><dt>Número de documento</dt><dd>{{ user.document }}</dd></div><div><dt>Correo</dt><dd>{{ user.email }}</dd></div><div><dt>Área</dt><dd>{{ area }}</dd></div><div><dt>Estado</dt><dd>{{ user.status === 'active' ? 'Usuario activo' : 'Usuario inactivo' }}</dd></div><div><dt>Próximo ingreso</dt><dd>{{ user.mustChangePassword ? 'Debe cambiar contraseña' : 'Ingreso normal' }}</dd></div></dl></section>
      <section class="panel"><p class="eyebrow">ROLES ASIGNADOS</p><h3>{{ assignedRoles.length }} {{ assignedRoles.length === 1 ? 'rol' : 'roles' }}</h3><div class="stack-list"><div v-for="role in assignedRoles" :key="role.id"><strong>{{ role.name }}</strong><small>{{ role.description }}</small></div></div></section>
      <section class="panel"><p class="eyebrow">AUDITORÍA VISUAL</p><h3>Actividad reciente</h3><ul class="activity-list"><li v-if="user.mustChangePassword"><b>Contraseña restablecida</b><span>Restablecida en esta sesión demo</span></li><li><b>Usuario editado</b><span>Información general actualizada · Hoy</span></li><li><b>Usuario creado</b><span>Creado por Superadmin · 22 ago</span></li></ul></section>
    </div>
    <ConfirmDialog :open="resetOpen" :title="`¿Deseas restablecer la contraseña de ${fullName}?`" description="La contraseña temporal volverá a corresponder al número de documento y deberá cambiarla en su próximo ingreso." confirm-label="Restablecer contraseña" @close="resetOpen = false" @confirm="resetPassword" />
    <ConfirmDialog :open="statusOpen" :title="`${user?.status === 'active' ? 'Inactivar' : 'Activar'} usuario`" :description="`Confirma el cambio de estado para ${fullName}.`" :confirm-label="user?.status === 'active' ? 'Inactivar' : 'Activar'" :tone="user?.status === 'active' ? 'danger' : 'primary'" @close="statusOpen = false" @confirm="toggleStatus" />
  </AppLayout>
</template>
