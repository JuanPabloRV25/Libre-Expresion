<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, CircleCheckBig, Eye, EyeOff, IdCard, LockKeyhole, ShieldCheck } from '@lucide/vue'
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
  <main class="login-page login-page-refined" :data-backend-health="backendHealth">
    <section class="login-visual" aria-label="Portal Libre Expresión">
      <div class="login-brand-surface"><BrandMark compact /></div>

      <div class="login-message">
        <span class="soft-pill">PORTAL CORPORATIVO</span>
        <h1>Todo tu acceso,<br />en un solo lugar.</h1>
        <p>Una experiencia segura y clara para gestionar las funciones de Libre Expresión.</p>
      </div>

      <div class="login-trust-row" aria-label="Características de acceso">
        <span><ShieldCheck :size="19" /> Acceso protegido</span>
        <span><CircleCheckBig :size="19" /> Permisos por rol</span>
      </div>

      <small class="secure-note">
        <span class="health-dot" :class="backendHealth" />
        {{ backendHealth === 'healthy' ? 'Servicios disponibles' : backendHealth === 'checking' ? 'Verificando servicios' : 'Servicio temporalmente no disponible' }}
      </small>
    </section>

    <section class="login-form-panel">
      <div class="login-form-wrap">
        <div class="login-mobile-brand"><BrandMark /></div>
        <form class="login-form" @submit.prevent="submit">
          <p class="eyebrow">BIENVENIDO DE NUEVO</p>
          <h2>Iniciar sesión</h2>
          <p>Ingresa tus credenciales para continuar al portal.</p>

          <label>
            Número de documento <em>*</em>
            <span class="input-with-icon">
              <IdCard :size="19" />
              <input v-model="document" required inputmode="numeric" autocomplete="username" placeholder="Escribe tu documento" />
            </span>
          </label>

          <label>
            Contraseña <em>*</em>
            <span class="input-with-icon">
              <LockKeyhole :size="19" />
              <input v-model="password" required :type="showPassword ? 'text' : 'password'" autocomplete="current-password" placeholder="Escribe tu contraseña" />
              <button type="button" :aria-label="showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'" @click="showPassword = !showPassword">
                <component :is="showPassword ? EyeOff : Eye" :size="19" />
              </button>
            </span>
          </label>

          <p v-if="error" class="form-error" role="alert">{{ error }}</p>

          <button class="button primary full large login-submit" type="submit">
            Iniciar sesión <ArrowRight :size="18" />
          </button>

          <p class="login-support-note">Si tienes problemas para ingresar, comunícate con el administrador del portal.</p>
        </form>
      </div>
    </section>
  </main>
</template>
