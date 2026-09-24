<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import BrandMark from '../components/BrandMark.vue'
import PasswordInput from '../components/PasswordInput.vue'
import { ApiError } from '../api/httpClient'
import { authService } from '../services/authService'
import { passwordChangedNotificationMessage } from '../services/notificationMessages'

const route = useRoute()
const router = useRouter()
const userId = ref(typeof route.query.userId === 'string' ? route.query.userId : '')
const token = ref(typeof route.query.token === 'string' ? route.query.token : '')
const newPassword = ref('')
const confirmation = ref('')
const submitting = ref(false)
const completed = ref(false)
const error = ref('')
const notice = ref('')
const hasLinkData = computed(() => Boolean(userId.value && token.value))

onMounted(() => {
  if (hasLinkData.value) void router.replace({ path: '/reset-password' })
})

function validatePassword() {
  if (newPassword.value.length < 8) return 'La contraseña debe tener al menos 8 caracteres.'
  if (!/[A-Z]/.test(newPassword.value)) return 'La contraseña debe incluir una letra mayúscula.'
  if (!/[a-z]/.test(newPassword.value)) return 'La contraseña debe incluir una letra minúscula.'
  if (!/[0-9]/.test(newPassword.value)) return 'La contraseña debe incluir un número.'
  if (!/[^A-Za-z0-9]/.test(newPassword.value)) return 'La contraseña debe incluir un carácter especial.'
  if (newPassword.value !== confirmation.value) return 'La confirmación no coincide con la nueva contraseña.'
  return ''
}

async function submit() {
  error.value = ''
  if (!hasLinkData.value) {
    error.value = 'El enlace es inválido, está incompleto o venció.'
    return
  }

  const validationError = validatePassword()
  if (validationError) {
    error.value = validationError
    return
  }

  submitting.value = true
  try {
    const notificationStatus = await authService.resetPassword(
      userId.value,
      token.value,
      newPassword.value,
      confirmation.value,
    )
    notice.value = passwordChangedNotificationMessage(notificationStatus)
    completed.value = true
    newPassword.value = ''
    confirmation.value = ''
  } catch (caught) {
    if (caught instanceof ApiError && caught.code === 'invalid_or_expired_password_reset') {
      error.value = 'El enlace es inválido, venció o ya fue utilizado. Solicita uno nuevo al administrador.'
    } else if (caught instanceof ApiError && caught.code === 'invalid_new_password') {
      error.value = 'La nueva contraseña no cumple la política de seguridad.'
    } else if (caught instanceof ApiError && caught.code === 'password_confirmation_mismatch') {
      error.value = 'La confirmación no coincide con la nueva contraseña.'
    } else {
      error.value = 'No fue posible establecer la contraseña. Intenta nuevamente.'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="secure-page">
    <section class="secure-brand-panel">
      <BrandMark compact />
      <div>
        <span class="soft-pill">Enlace seguro</span>
        <h1>Establece tu<br />contraseña</h1>
        <p>Este enlace es personal, temporal y solo puede utilizarse una vez.</p>
      </div>
      <small>El ingreso al Portal continuará utilizando tu número de documento.</small>
    </section>
    <section class="secure-form-panel">
      <section v-if="completed" class="success-card">
        <span class="soft-pill">Proceso completado</span>
        <h2>Contraseña establecida</h2>
        <p>Ya puedes ingresar al Portal con tu documento y la nueva contraseña.</p>
        <p v-if="notice" class="notice success">{{ notice }}</p>
        <RouterLink class="button primary" to="/login">Volver al inicio de sesión</RouterLink>
      </section>
      <form v-else class="secure-form" @submit.prevent="submit">
        <p class="eyebrow">ACCESO PROTEGIDO</p>
        <h2>Nueva contraseña</h2>
        <p>Define la contraseña que utilizarás junto con tu número de documento.</p>
        <label><span class="field-label">Nueva contraseña <em>*</em></span><PasswordInput v-model="newPassword" placeholder="Escribe la nueva contraseña" /></label>
        <label><span class="field-label">Confirmar contraseña <em>*</em></span><PasswordInput v-model="confirmation" placeholder="Repite la nueva contraseña" /></label>
        <ul class="field-hint">
          <li>Mínimo 8 caracteres.</li>
          <li>Al menos una mayúscula, una minúscula, un número y un carácter especial.</li>
        </ul>
        <p v-if="!hasLinkData && !error" class="form-error" role="alert">El enlace es inválido, está incompleto o venció.</p>
        <p v-if="error" class="form-error" role="alert">{{ error }}</p>
        <button class="button primary full" type="submit" :disabled="submitting || !hasLinkData">
          {{ submitting ? 'Guardando…' : 'Establecer contraseña' }}
        </button>
        <RouterLink class="button secondary full" to="/login">Volver al inicio de sesión</RouterLink>
      </form>
    </section>
  </main>
</template>
