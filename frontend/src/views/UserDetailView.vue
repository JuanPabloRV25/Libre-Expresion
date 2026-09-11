<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import { ApiError } from '../api/httpClient'
import { usersService, type ApiUser } from '../services/usersService'
import { isNotificationStatus, passwordResetNotificationMessage, userCreatedNotificationMessage } from '../services/notificationMessages'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const auth = useAuthStore()
const user = ref<ApiUser>()
const resetOpen = ref(false)
const statusOpen = ref(false)
const notice = ref('')
const errorMessage = ref('')
const fullName = computed(() => user.value ? `${user.value.firstName} ${user.value.lastName}` : '')

async function load() {
  try {
    user.value = await usersService.get(String(route.params.id))
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el usuario.'
  }
}

onMounted(async () => {
  await load()
  if (route.query.operation === 'created' && isNotificationStatus(route.query.notification)) {
    notice.value = userCreatedNotificationMessage(route.query.notification)
  }
})

async function resetPassword() {
  if (!user.value) return
  errorMessage.value = ''
  try {
    const result = await usersService.resetPassword(user.value.id)
    await load()
    notice.value = passwordResetNotificationMessage(result.notificationStatus)
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible restablecer la contraseña.'
  } finally {
    resetOpen.value = false
  }
}

async function toggleStatus() {
  if (!user.value) return
  errorMessage.value = ''
  try {
    user.value = await usersService.setStatus(user.value.id, !user.value.isActive)
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cambiar el estado.'
  } finally {
    statusOpen.value = false
  }
}
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/users">← Volver a usuarios</RouterLink>
    <p v-if="notice" class="info-banner">{{ notice }}</p>
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section v-if="user" class="detail-hero">
      <span class="avatar huge">{{ user.firstName[0] }}{{ user.lastName[0] }}</span>
      <div><p class="eyebrow">DETALLE DE USUARIO</p><h1>{{ fullName }}</h1><p>{{ user.email }}</p></div>
      <StatusBadge :active="user.isActive" />
      <div class="detail-actions">
        <button v-if="auth.hasPermission('users.reset_password') && user.id !== auth.currentUser?.id" class="button secondary" @click="resetOpen = true">Enviar restablecimiento</button>
        <RouterLink v-if="auth.hasPermission('users.edit')" class="button secondary" :to="`/users/${user.id}/edit`">Editar</RouterLink>
        <button v-if="auth.hasPermission('users.activate')" class="button" :class="user.isActive ? 'danger ghost' : 'primary'" @click="statusOpen = true">{{ user.isActive ? 'Inactivar' : 'Activar' }}</button>
      </div>
    </section>
    <div v-if="user" class="detail-grid">
      <section class="panel"><p class="eyebrow">INFORMACIÓN</p><div class="stack-list"><div><strong>Documento</strong><small>{{ user.documentNumber }}</small></div><div><strong>Área</strong><small>{{ user.area?.name ?? 'Sin área' }}</small></div><div><strong>Código de asesor</strong><small>{{ user.advisorCode ?? 'No aplica' }}</small></div><div><strong>Estado de contraseña</strong><small>{{ user.mustChangePassword ? 'Pendiente de establecer contraseña' : 'Contraseña establecida' }}</small></div></div></section>
      <section class="panel"><p class="eyebrow">ROLES ASIGNADOS</p><h3>{{ user.roles.length }} roles</h3><div class="stack-list"><div v-for="role in user.roles" :key="role.id"><strong>{{ role.name }}</strong><small>{{ role.isActive ? 'Activo' : 'Inactivo' }}</small></div><p v-if="!user.roles.length">Este usuario no tiene roles asignados.</p></div></section>
    </div>
    <ConfirmDialog :open="resetOpen" title="Enviar restablecimiento" description="Se invalidará la contraseña actual y se enviará al correo registrado un enlace personal y temporal para establecer una nueva." confirm-label="Enviar enlace" @close="resetOpen = false" @confirm="resetPassword" />
    <ConfirmDialog :open="statusOpen" :title="`${user?.isActive ? 'Inactivar' : 'Activar'} usuario`" :description="`Confirma el cambio de estado para ${fullName}.`" confirm-label="Confirmar" @close="statusOpen = false" @confirm="toggleStatus" />
  </AppLayout>
</template>
