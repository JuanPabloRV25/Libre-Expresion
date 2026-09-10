<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Eye, EyeOff, IdCard, LockKeyhole, ShieldCheck, Users, KeyRound, CircleCheckBig } from '@lucide/vue'
import BrandMark from '../components/BrandMark.vue'
import { healthService } from '../services/healthService'
import { useAuthStore } from '../stores/auth'

const router = useRouter()
const auth = useAuthStore()
const document = ref('')
const password = ref('')
const showPassword = ref(false)
const error = ref('')
const backendHealth = ref<'checking' | 'healthy' | 'unavailable'>('checking')

onMounted(async () => {
  try {
    const result = await healthService.check()
    backendHealth.value = result.status === 'Healthy' ? 'healthy' : 'unavailable'
  } catch {
    backendHealth.value = 'unavailable'
  }
})

async function submit() {
  error.value = ''
  const result = await auth.login(document.value.trim(), password.value)
  if (result.status === 'success') await router.push('/home')
  else if (result.status === 'first-login') await router.push('/first-login')
  else if (result.status === 'inactive') error.value = 'La cuenta está inactiva. Comunícate con el administrador del sistema.'
  else if (result.status === 'locked') error.value = 'La cuenta está temporalmente bloqueada. Intenta nuevamente más tarde.'
  else error.value = 'Número de documento o contraseña incorrectos.'
}
</script>

<template>
  <main class="login-page" :data-backend-health="backendHealth">
    <section class="login-visual">
      <BrandMark compact />
      <div class="login-message">
        <span class="soft-pill">Portal · Fase 1</span>
        <h1>El acceso correcto<br />para cada persona.</h1>
        <p>Ingreso por número de documento y permisos definidos para cada rol.</p>
      </div>
      <div class="authorization-model" aria-label="Modelo de autorización">
        <span><Users :size="19" /> Usuario</span><b>›</b>
        <span><ShieldCheck :size="19" /> Roles</span><b>›</b>
        <span><KeyRound :size="19" /> Permisos</span><b>›</b>
        <span><CircleCheckBig :size="19" /> Acceso</span>
      </div>
      <small class="secure-note"><LockKeyhole :size="14" /> Sesión protegida mediante cookie segura</small>
    </section>

    <section class="login-form-panel">
      <form class="login-form" @submit.prevent="submit">
        <p class="eyebrow">PORTAL LIBRE EXPRESIÓN</p>
        <h2>Iniciar sesión</h2>
        <p>Ingresa con tu número de documento y contraseña.</p>
        <label>Número de documento <em>*</em>
          <span class="input-with-icon"><IdCard :size="18" /><input v-model="document" required inputmode="numeric" autocomplete="username" /></span>
        </label>
        <label>Contraseña <em>*</em>
          <span class="input-with-icon"><LockKeyhole :size="18" /><input v-model="password" required :type="showPassword ? 'text' : 'password'" autocomplete="current-password" /><button type="button" :aria-label="showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'" @click="showPassword = !showPassword"><component :is="showPassword ? EyeOff : Eye" :size="18" /></button></span>
        </label>
        <p v-if="error" class="form-error" role="alert">{{ error }}</p>
        <button class="button primary full large" type="submit">Iniciar sesión <span>›</span></button>
      </form>
    </section>
  </main>
</template>
