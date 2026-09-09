<script setup lang="ts">
import { ref } from 'vue'
import { ChevronDown, LogOut, KeyRound, UserRound } from '@lucide/vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const open = ref(false)
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

function logout() {
  auth.logout()
  router.push('/login')
}
</script>

<template>
  <header class="app-header">
    <div><strong>{{ route.meta.title }}</strong><span>Portal Libre Expresión</span></div>
    <div class="account-menu">
      <button class="account-trigger" type="button" :aria-expanded="open" @click="open = !open">
        <span class="avatar">{{ auth.currentUser?.firstName[0] }}{{ auth.currentUser?.lastName[0] }}</span>
        <span><strong>{{ auth.fullName }}</strong><small>{{ auth.currentUser?.demoProfile === 'superadmin' ? 'Superadmin' : auth.currentUser?.demoProfile === 'limited' ? 'Administrador limitado' : 'Usuario interno' }}</small></span>
        <ChevronDown :size="16" />
      </button>
      <div v-if="open" class="account-popover">
        <RouterLink to="/profile" @click="open = false"><UserRound :size="17" /> Mi perfil</RouterLink>
        <RouterLink to="/profile/change-password" @click="open = false"><KeyRound :size="17" /> Cambiar contraseña</RouterLink>
        <button type="button" @click="logout"><LogOut :size="17" /> Cerrar sesión</button>
      </div>
    </div>
  </header>
</template>

