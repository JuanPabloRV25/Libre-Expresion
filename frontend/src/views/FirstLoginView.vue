<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()
const newPassword = ref('')
const confirmation = ref('')
const error = ref('')

async function submit() {
  error.value = ''
  if (newPassword.value.length < 8) return void (error.value = 'La nueva contraseña debe tener al menos 8 caracteres.')
  if (newPassword.value !== confirmation.value) return void (error.value = 'La confirmación no coincide con la nueva contraseña.')
  try {
    const notificationStatus = await auth.completeFirstLogin(newPassword.value, confirmation.value)
    if (!notificationStatus) return void (error.value = 'No existe una sesión de primer ingreso activa.')
    await router.push({ path: '/password-updated', query: { notification: notificationStatus } })
  } catch {
    return void (error.value = 'No fue posible actualizar la contraseña. Verifica la política de seguridad.')
  }
}
</script>

<template>
  <main class="secure-page">
    <section class="secure-brand-panel">
      <BrandMark compact />
      <div><span class="soft-pill">Primer ingreso</span><h1>Cambio obligatorio<br />de contraseña</h1><p>Por seguridad, antes de continuar debe cambiar la contraseña asignada inicialmente.</p></div>
      <div class="user-summary"><span class="avatar large">{{ auth.currentUser?.firstName[0] }}{{ auth.currentUser?.lastName[0] }}</span><strong>{{ auth.fullName }}</strong><small>Documento {{ auth.currentUser?.documentNumber }}</small></div>
    </section>
    <section class="secure-form-panel">
      <form class="secure-form" @submit.prevent="submit">
        <p class="eyebrow">ACCESO PROTEGIDO</p>
        <h2>Establece una nueva contraseña</h2>
        <p>No se mostrará el inicio ni el menú hasta completar este paso.</p>
        <label>Nueva contraseña <em>*</em><input v-model="newPassword" type="password" required autocomplete="new-password" /></label>
        <label>Confirmar nueva contraseña <em>*</em><input v-model="confirmation" type="password" required autocomplete="new-password" /></label>
        <p class="field-hint">Usa una contraseña robusta y distinta de la contraseña temporal.</p>
        <p v-if="error" class="form-error" role="alert">{{ error }}</p>
        <button class="button primary full" type="submit">Cambiar contraseña</button>
      </form>
    </section>
  </main>
</template>
