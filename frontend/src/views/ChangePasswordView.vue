<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import PasswordInput from '../components/PasswordInput.vue'
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
  if (!/[A-Z]/.test(newPassword.value)) return void (error.value = 'La nueva contraseña debe incluir al menos una letra mayúscula.')
  if (!/[a-z]/.test(newPassword.value)) return void (error.value = 'La nueva contraseña debe incluir al menos una letra minúscula.')
  if (!/[0-9]/.test(newPassword.value)) return void (error.value = 'La nueva contraseña debe incluir al menos un número.')
  if (!/[^A-Za-z0-9]/.test(newPassword.value)) return void (error.value = 'La nueva contraseña debe incluir al menos un carácter especial.')
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
  <AppLayout><RouterLink class="back-link" to="/profile">← Volver a Mi perfil</RouterLink><PageHeader eyebrow="Portal · Cambiar contraseña" title="Cambiar contraseña" description="Actualiza tu contraseña personal. Esta opción está disponible para cualquier usuario autenticado." /><div class="password-layout"><form class="panel form-panel compact-form" @submit.prevent="submit"><p class="eyebrow">MI CUENTA</p><h2>Define una nueva contraseña</h2><label><span class="field-label">Contraseña actual <em>*</em></span><PasswordInput v-model="currentPassword" autocomplete="current-password" placeholder="Escribe tu contraseña actual" /></label><label><span class="field-label">Nueva contraseña <em>*</em></span><PasswordInput v-model="newPassword" placeholder="Escribe la nueva contraseña" /></label><label><span class="field-label">Confirmar nueva contraseña <em>*</em></span><PasswordInput v-model="confirmation" placeholder="Repite la nueva contraseña" /></label><p class="field-hint">La contraseña se procesa exclusivamente en el servidor.</p><p v-if="error" class="form-error">{{ error }}</p><p v-if="success" class="notice success">{{ success }}</p><div class="form-actions"><button class="button secondary" type="button" @click="router.push('/profile')">Cancelar</button><button class="button primary" type="submit">Cambiar contraseña</button></div></form><aside class="panel guidance-card password-requirements"><p class="eyebrow">CONTRASEÑA SEGURA</p><h3>¿Qué debe incluir tu nueva contraseña?</h3><p>Para que el sistema la acepte, asegúrate de cumplir todas estas condiciones:</p><ul class="requirement-list"><li><strong>8 caracteres como mínimo</strong><span>Puede ser más larga para mayor seguridad.</span></li><li><strong>Una letra mayúscula</strong><span>Por ejemplo: A, B o C.</span></li><li><strong>Una letra minúscula</strong><span>Por ejemplo: a, b o c.</span></li><li><strong>Un número</strong><span>Del 0 al 9.</span></li><li><strong>Un carácter especial</strong><span>Por ejemplo: @, #, $, %, &amp; o *.</span></li></ul><div class="password-tip"><strong>Recomendación</strong><span>Evita usar tu nombre, documento o información fácil de adivinar. Confirma escribiendo exactamente la misma contraseña en ambos campos.</span></div></aside></div></AppLayout>
</template>
