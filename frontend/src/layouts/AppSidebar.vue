<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { Grid2X2, Users, ShieldCheck, KeyRound, Building2, ScrollText } from '@lucide/vue'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'
import { visibleSidebarItems, type SidebarIconName } from '../navigation/sidebarItems'

const auth = useAuthStore()
const route = useRoute()
const icons: Record<SidebarIconName, typeof Grid2X2> = {
  home: Grid2X2,
  users: Users,
  areas: Building2,
  roles: ShieldCheck,
  permissions: KeyRound,
  audit: ScrollText,
}
const items = computed(() => visibleSidebarItems(auth.hasPermission)
  .map((item) => ({ ...item, icon: icons[item.icon] })))

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
      </RouterLink>
    </nav>
    <RouterLink v-if="auth.currentUser" class="sidebar-account" to="/profile">
      <span class="avatar">{{ auth.currentUser.firstName[0] }}{{ auth.currentUser.lastName[0] }}</span>
      <span><strong>{{ auth.fullName }}</strong><small>{{ auth.roleNames }}</small></span>
    </RouterLink>
  </aside>
</template>
