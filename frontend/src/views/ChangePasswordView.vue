<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { useAuthStore } from '../stores/auth'
import { passwordChangedNotificationMessage } from '../services/notificationMessages'

const auth = useAuthStore()
const router = useRouter()
const currentPassword = ref('')
const newPassword = ref('')
const confirmation = ref('')
const error = ref('')
const success = ref('')
async function submit() {
  error.value = ''; success.value = ''
  if (newPassword.value.length < 8) return void (error.value = 'La nueva contraseña debe tener al menos 8 caracteres.')
  if (newPassword.value !== confirmation.value) return void (error.value = 'La confirmación no coincide.')
  try {
    const notificationStatus = await auth.changeOwnPassword(currentPassword.value, newPassword.value, confirmation.value)
    if (!notificationStatus) return void (error.value = 'No existe una sesión activa.')
    success.value = passwordChangedNotificationMessage(notificationStatus)
  } catch {
    return void (error.value = 'No fue posible cambiar la contraseña. Verifica la contraseña actual y la política de seguridad.')
  }
  currentPassword.value = ''; newPassword.value = ''; confirmation.value = ''
}
</script>

<template>
  <AppLayout><RouterLink class="back-link" to="/profile">← Volver a Mi perfil</RouterLink><PageHeader eyebrow="Portal · Cambiar contraseña" title="Cambiar contraseña" description="Actualiza tu contraseña personal. Esta opción está disponible para cualquier usuario autenticado." /><div class="password-layout"><form class="panel form-panel compact-form" @submit.prevent="submit"><p class="eyebrow">MI CUENTA</p><h2>Define una nueva contraseña</h2><label>Contraseña actual <em>*</em><input v-model="currentPassword" type="password" required autocomplete="current-password" /></label><label>Nueva contraseña <em>*</em><input v-model="newPassword" type="password" required autocomplete="new-password" /></label><label>Confirmar nueva contraseña <em>*</em><input v-model="confirmation" type="password" required autocomplete="new-password" /></label><p class="field-hint">La contraseña se procesa exclusivamente en el servidor.</p><p v-if="error" class="form-error">{{ error }}</p><p v-if="success" class="notice success">{{ success }}</p><div class="form-actions"><button class="button secondary" type="button" @click="router.push('/profile')">Cancelar</button><button class="button primary" type="submit">Cambiar contraseña</button></div></form><aside class="panel guidance-card"><h3>Cambio personal</h3><p>Esta acción pertenece a Mi cuenta. No depende de permisos administrativos.</p><ul><li>No mostramos contraseñas en la actividad.</li><li>El correo de seguridad tampoco incluye credenciales.</li><li>La sesión permanece abierta después del cambio.</li></ul></aside></div></AppLayout>
</template>
