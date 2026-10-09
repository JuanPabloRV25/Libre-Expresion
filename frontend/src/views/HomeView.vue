<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Users, ShieldCheck, KeyRound, Building2, ArrowRight } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'
import { usersService } from '../services/usersService'
import { rolesService } from '../services/rolesService'
import { areasService } from '../services/areasService'

const auth = useAuthStore()
const visiblePermissionCount = computed(() => auth.permissionCodes.filter((code) => !code.startsWith('audit.')).length)
const userCount = ref(0)
const roleCount = ref(0)
const areaCount = ref(0)
onMounted(async () => {
  if (auth.hasPermission('users.view')) {
    userCount.value = (await usersService.list({ status: 'active' })).length
  }
  if (auth.hasPermission('roles.view')) {
    roleCount.value = (await rolesService.list()).length
  }
  if (auth.hasPermission('areas.view')) {
    areaCount.value = (await areasService.list()).length
  }
})

const cards = computed(() => [
  { label: 'Usuarios', description: 'Consulta y administra las personas con acceso al Portal.', value: `${userCount.value} activos`, to: '/users', icon: Users, permission: 'users.view' },
  { label: 'Áreas', description: 'Organiza las dependencias informativas del Portal.', value: `${areaCount.value} configuradas`, to: '/areas', icon: Building2, permission: 'areas.view' },
  { label: 'Roles', description: 'Organiza responsabilidades sin reglas rígidas por cargo.', value: `${roleCount.value} configurados`, to: '/roles', icon: ShieldCheck, permission: 'roles.view' },
  { label: 'Permisos', description: 'Revisa qué acciones habilita cada permiso.', value: `${visiblePermissionCount.value} disponibles`, to: '/permissions', icon: KeyRound, permission: 'permissions.view' },
].filter((card) => auth.hasPermission(card.permission)))

const profileName = computed(() => auth.roleNames)
const currentDate = new Intl.DateTimeFormat('es-CO', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
}).format(new Date()).toUpperCase()
</script>

<template>
  <AppLayout>
    <section class="hero-card">
      <div><p class="eyebrow">{{ currentDate }}</p><h1>Hola, {{ auth.currentUser?.firstName }}</h1><p>Este es tu espacio de acceso en Portal Libre Expresión.</p><span>Tu perfil actual <b>{{ profileName }}</b></span></div>
      <div class="hero-monogram" aria-hidden="true"><BrandMark compact /></div>
    </section>
    <section v-if="cards.length" class="section-block">
      <div class="section-title"><div><p class="eyebrow">ACCESOS DISPONIBLES</p><h2>Administración</h2></div><span>{{ cards.length }} secciones habilitadas</span></div>
      <div class="access-grid">
        <RouterLink v-for="card in cards" :key="card.to" :to="card.to" class="access-card"><span class="icon-box"><component :is="card.icon" :size="23" /></span><span><strong>{{ card.label }}</strong><small>{{ card.description }}</small><em>{{ card.value }}</em></span><ArrowRight :size="18" /></RouterLink>
      </div>
    </section>
    <section v-else class="standard-welcome">
      <p class="eyebrow">ACCESO VERIFICADO</p><h2>Tu cuenta está lista</h2><p>Actualmente tienes acceso al inicio y a tu perfil. Las opciones administrativas no aparecen porque no hacen parte de tus permisos.</p><RouterLink class="button secondary" to="/profile">Ver mi perfil</RouterLink>
    </section>
    <section class="panel section-block"><p class="eyebrow">RESUMEN DE ACCESO</p><h3>Permisos de tu sesión</h3><div class="metrics"><span><strong>{{ visiblePermissionCount }}</strong><small>Permisos habilitados</small></span><span><strong>{{ cards.length }}</strong><small>Opciones del Portal</small></span><span><strong>{{ auth.activeRoles.length }}</strong><small>Roles activos</small></span></div></section>
  </AppLayout>
</template>
