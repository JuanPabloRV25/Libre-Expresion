<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { Building2, Grid2X2, KeyRound, PanelLeftClose, PanelLeftOpen, ScrollText, ShieldCheck, Users, X } from '@lucide/vue'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'
import { visibleSidebarItems, type SidebarIconName } from '../navigation/sidebarItems'

const props = defineProps<{ collapsed: boolean; mobileOpen: boolean }>()
const emit = defineEmits<{ toggleCollapsed: []; closeMobile: [] }>()

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
  <button v-if="props.mobileOpen" class="sidebar-backdrop" type="button" aria-label="Cerrar navegación" @click="emit('closeMobile')" />
  <aside class="sidebar" :class="{ collapsed: props.collapsed, 'mobile-open': props.mobileOpen }">
    <div class="sidebar-topbar">
      <RouterLink class="sidebar-brand" to="/home" aria-label="Ir al inicio" @click="emit('closeMobile')">
        <BrandMark />
      </RouterLink>
      <button class="sidebar-collapse-button" type="button" :aria-label="props.collapsed ? 'Expandir navegación' : 'Contraer navegación'" @click="emit('toggleCollapsed')">
        <component :is="props.collapsed ? PanelLeftOpen : PanelLeftClose" :size="19" />
      </button>
      <button class="sidebar-mobile-close" type="button" aria-label="Cerrar navegación" @click="emit('closeMobile')"><X :size="21" /></button>
    </div>

    <p class="nav-label">NAVEGACIÓN</p>
    <nav aria-label="Navegación principal">
      <RouterLink
        v-for="item in items"
        :key="item.to"
        :to="item.to"
        :title="props.collapsed ? item.label : undefined"
        :class="{ active: active(item.to) }"
        @click="emit('closeMobile')"
      >
        <component :is="item.icon" :size="20" stroke-width="1.8" />
        <span>{{ item.label }}</span>
      </RouterLink>
    </nav>

    <RouterLink v-if="auth.currentUser" class="sidebar-account" to="/profile" :title="props.collapsed ? auth.fullName : undefined" @click="emit('closeMobile')">
      <span class="avatar">{{ auth.currentUser.firstName[0] }}{{ auth.currentUser.lastName[0] }}</span>
      <span><strong>{{ auth.fullName }}</strong><small>{{ auth.roleSummary }}</small></span>
    </RouterLink>
  </aside>
</template>
