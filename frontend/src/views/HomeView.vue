<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Users, ShieldCheck, KeyRound, Building2, ArrowRight, Activity } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import { useAuthStore } from '../stores/auth'
import { usersService } from '../services/usersService'
import { rolesService } from '../services/rolesService'

const auth = useAuthStore()
const userCount = ref(0)
const roleCount = ref(0)
onMounted(async () => {
  userCount.value = (await usersService.list()).filter((user) => user.status === 'active').length
  roleCount.value = (await rolesService.list()).length
})

const cards = computed(() => [
  { label: 'Usuarios', description: 'Consulta y administra las personas con acceso al Portal.', value: `${userCount.value} activos`, to: '/users', icon: Users, permission: 'users.view' },
  { label: 'Áreas', description: 'Organiza las dependencias informativas del Portal.', value: '6 configuradas', to: '/areas', icon: Building2, permission: 'areas.view' },
  { label: 'Roles', description: 'Organiza responsabilidades sin reglas rígidas por cargo.', value: `${roleCount.value} configurados`, to: '/roles', icon: ShieldCheck, permission: 'roles.view' },
  { label: 'Permisos', description: 'Revisa qué acciones habilita cada permiso.', value: `${auth.permissionCodes.length} disponibles`, to: '/permissions', icon: KeyRound, permission: 'permissions.view' },
].filter((card) => auth.hasPermission(card.permission)))

const profileName = computed(() => auth.roleNames)
</script>

<template>
  <AppLayout>
    <section class="hero-card">
      <div><p class="eyebrow">MIÉRCOLES, 9 DE SEPTIEMBRE</p><h1>Hola, {{ auth.currentUser?.firstName }}</h1><p>Este es tu espacio de acceso en Portal Libre Expresión.</p><span>Tu perfil actual <b>{{ profileName }}</b></span></div>
      <div class="hero-monogram">le</div>
    </section>
    <section v-if="cards.length" class="section-block">
      <div class="section-title"><div><p class="eyebrow">ACCESOS DISPONIBLES</p><h2>Administración de la Fase 1</h2></div><span>{{ cards.length }} secciones habilitadas</span></div>
      <div class="access-grid">
        <RouterLink v-for="card in cards" :key="card.to" :to="card.to" class="access-card"><span class="icon-box"><component :is="card.icon" :size="23" /></span><span><strong>{{ card.label }}</strong><small>{{ card.description }}</small><em>{{ card.value }}</em></span><ArrowRight :size="18" /></RouterLink>
      </div>
    </section>
    <section v-else class="standard-welcome">
      <p class="eyebrow">ACCESO VERIFICADO</p><h2>Tu cuenta está lista</h2><p>Actualmente tienes acceso al inicio y a tu perfil. Las opciones administrativas no aparecen porque no hacen parte de tus permisos.</p><RouterLink class="button secondary" to="/profile">Ver mi perfil</RouterLink>
    </section>
    <div class="dashboard-grid">
      <section class="panel"><p class="eyebrow">RESUMEN DE ACCESO</p><h3>Permisos de tu sesión</h3><div class="metrics"><span><strong>{{ auth.permissionCodes.length }}</strong><small>Permisos habilitados</small></span><span><strong>{{ cards.length }}</strong><small>Opciones del Portal</small></span><span><strong>{{ profileName === 'Superadmin' ? 'Global' : 'Limitado' }}</strong><small>Nivel de acceso</small></span></div></section>
      <section class="panel"><p class="eyebrow">ACTIVIDAD RECIENTE</p><h3><Activity :size="19" /> Cambios relevantes</h3><ul class="activity-list"><li><b>Usuario creado</b><span>Andrés Pardo · Hoy</span></li><li><b>Contraseña restablecida</b><span>Registro disponible en el prototipo</span></li><li><b>Usuario inactivado</b><span>Camilo Vargas · 27 ago</span></li></ul></section>
    </div>
  </AppLayout>
</template>
