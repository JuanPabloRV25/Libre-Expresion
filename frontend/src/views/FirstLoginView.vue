<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()
const currentPassword = ref('')
const newPassword = ref('')
const confirmation = ref('')
const error = ref('')

async function submit() {
  error.value = ''
  if (newPassword.value.length < 8) return void (error.value = 'La nueva contraseña debe tener al menos 8 caracteres.')
  if (newPassword.value !== confirmation.value) return void (error.value = 'La confirmación no coincide con la nueva contraseña.')
  if (!(await auth.completeFirstLogin(currentPassword.value, newPassword.value))) return void (error.value = 'La contraseña temporal no es correcta.')
  await router.push('/password-updated')
}
</script>

<template>
  <main class="secure-page">
    <section class="secure-brand-panel">
      <BrandMark compact />
      <div><span class="soft-pill">Primer ingreso</span><h1>Cambio obligatorio<br />de contraseña</h1><p>Por seguridad, antes de continuar debe cambiar la contraseña asignada inicialmente.</p></div>
      <div class="user-summary"><span class="avatar large">{{ auth.pendingUser?.firstName[0] }}{{ auth.pendingUser?.lastName[0] }}</span><strong>{{ auth.pendingUser?.firstName }} {{ auth.pendingUser?.lastName }}</strong><small>Documento {{ auth.pendingUser?.document }}</small></div>
    </section>
    <section class="secure-form-panel">
      <form class="secure-form" @submit.prevent="submit">
        <p class="eyebrow">ACCESO PROTEGIDO</p>
        <h2>Establece una nueva contraseña</h2>
        <p>No se mostrará el inicio ni el menú hasta completar este paso.</p>
        <label>Contraseña actual <em>*</em><input v-model="currentPassword" type="password" required autocomplete="current-password" /></label>
        <label>Nueva contraseña <em>*</em><input v-model="newPassword" type="password" required autocomplete="new-password" /></label>
        <label>Confirmar nueva contraseña <em>*</em><input v-model="confirmation" type="password" required autocomplete="new-password" /></label>
        <p class="field-hint">La contraseña solo se usa dentro de esta simulación local.</p>
        <p v-if="error" class="form-error" role="alert">{{ error }}</p>
        <button class="button primary full" type="submit">Cambiar contraseña</button>
      </form>
    </section>
  </main>
</template>

