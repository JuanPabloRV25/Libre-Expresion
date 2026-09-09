<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { Grid2X2, Users, ShieldCheck, KeyRound, Building2, Mail } from '@lucide/vue'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const route = useRoute()
const items = computed(() => [
  { label: 'Inicio', to: '/home', icon: Grid2X2 },
  { label: 'Usuarios', to: '/users', icon: Users, permission: 'users.view' },
  { label: 'Áreas', to: '/areas', icon: Building2, permission: 'areas.view' },
  { label: 'Roles', to: '/roles', icon: ShieldCheck, permission: 'roles.view' },
  { label: 'Permisos', to: '/permissions', icon: KeyRound, permission: 'permissions.view' },
  { label: 'Correos simulados', to: '/demo/emails', icon: Mail, permission: 'users.view', demo: true },
].filter((item) => auth.hasPermission(item.permission)))

const active = (to: string) => route.path === to || (to !== '/home' && route.path.startsWith(`${to}/`))
</script>

<template>
  <aside class="sidebar">
    <RouterLink class="sidebar-brand" to="/home"><BrandMark /></RouterLink>
    <p class="nav-label">NAVEGACIÓN</p>
    <nav aria-label="Navegación principal">
      <RouterLink v-for="item in items" :key="item.to" :to="item.to" :class="{ active: active(item.to) }">
        <component :is="item.icon" :size="20" stroke-width="1.8" />
        <span>{{ item.label }}</span>
        <small v-if="item.demo">DEMO</small>
      </RouterLink>
    </nav>
    <RouterLink v-if="auth.currentUser" class="sidebar-account" to="/profile">
      <span class="avatar">{{ auth.currentUser.firstName[0] }}{{ auth.currentUser.lastName[0] }}</span>
      <span><strong>{{ auth.fullName }}</strong><small>{{ auth.currentUser.demoProfile === 'superadmin' ? 'Superadmin' : auth.currentUser.demoProfile === 'limited' ? 'Administrador limitado' : 'Usuario interno' }}</small></span>
    </RouterLink>
  </aside>
</template>
