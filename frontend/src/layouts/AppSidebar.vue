<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  BriefcaseBusiness,
  Building2,
  ChevronDown,
  ClipboardList,
  FileSpreadsheet,
  Folder,
  Grid2X2,
  KeyRound,
  PanelLeftClose,
  PanelLeftOpen,
  ScrollText,
  Settings2,
  ShieldCheck,
  Users,
  X,
} from '@lucide/vue'
import BrandMark from '../components/BrandMark.vue'
import { useAuthStore } from '../stores/auth'
import {
  visibleSidebarGroups,
  visibleStandaloneSidebarItems,
  type SidebarIconName,
  type SidebarFolder,
  type SidebarItem,
} from '../navigation/sidebarItems'

const props = defineProps<{ collapsed: boolean; mobileOpen: boolean }>()
const emit = defineEmits<{ toggleCollapsed: []; closeMobile: [] }>()

const auth = useAuthStore()
const route = useRoute()
const openGroups = ref(new Set<string>())
const openFolders = ref(new Set<string>())

const icons: Record<SidebarIconName, typeof Grid2X2> = {
  home: Grid2X2,
  users: Users,
  areas: Building2,
  roles: ShieldCheck,
  permissions: KeyRound,
  audit: ScrollText,
  administration: Settings2,
  commercial: BriefcaseBusiness,
  orders: ClipboardList,
  reports: FileSpreadsheet,
  folder: Folder,
}

const standaloneItems = computed(() => visibleStandaloneSidebarItems(auth.hasPermission)
  .map((item) => ({ ...item, icon: icons[item.icon] })))
const groups = computed(() => visibleSidebarGroups(auth.hasPermission)
  .map((group) => ({
    ...group,
    icon: icons[group.icon],
    items: group.items.map((item) => 'children' in item
      ? { ...item, icon: icons[item.icon], children: item.children.map((child) => ({ ...child, icon: icons[child.icon] })) }
      : { ...item, icon: icons[item.icon] }),
  })))

const active = (to: string) => route.path === to || ((to !== '/home' && to !== '/commercial') && route.path.startsWith(`${to}/`))
const itemActive = (item: Pick<SidebarItem, 'to' | 'activePrefixes'>) => active(item.to) || item.activePrefixes?.some(active) === true
const entryActive = (item: Pick<SidebarFolder, 'routePrefix'> | Pick<SidebarItem, 'to' | 'activePrefixes'>) => 'routePrefix' in item ? active(item.routePrefix) : itemActive(item)
const groupActive = (id: string) => groups.value.find((group) => group.id === id)?.items.some(entryActive) ?? false
const groupOpen = (id: string) => openGroups.value.has(id)
const folderOpen = (id: string) => openFolders.value.has(id)

function toggleGroup(id: string) {
  const next = new Set(openGroups.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  openGroups.value = next
}

function toggleFolder(id: string) {
  const next = new Set(openFolders.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  openFolders.value = next
}

function openActiveGroup() {
  const next = new Set(openGroups.value)
  const folders = new Set(openFolders.value)
  groups.value.forEach((group) => {
    if (group.items.some(entryActive)) next.add(group.id)
    group.items.forEach((item) => { if ('children' in item && entryActive(item)) folders.add(item.id) })
  })
  openGroups.value = next
  openFolders.value = folders
}

watch([() => route.path, groups], openActiveGroup, { immediate: true })
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
        v-for="item in standaloneItems"
        :key="item.to"
        :to="item.to"
        :title="props.collapsed ? item.label : undefined"
        :class="{ active: active(item.to) }"
        @click="emit('closeMobile')"
      >
        <component :is="item.icon" :size="20" stroke-width="1.8" />
        <span>{{ item.label }}</span>
      </RouterLink>

      <section v-for="group in groups" :key="group.id" class="sidebar-nav-group" :class="{ open: groupOpen(group.id), active: groupActive(group.id) }">
        <button
          class="sidebar-group-trigger"
          type="button"
          :title="props.collapsed ? group.label : undefined"
          :aria-expanded="groupOpen(group.id)"
          :aria-controls="`sidebar-group-${group.id}`"
          :aria-label="group.label"
          @click="toggleGroup(group.id)"
        >
          <component :is="group.icon" :size="20" stroke-width="1.8" />
          <span>{{ group.label }}</span>
          <ChevronDown class="sidebar-group-chevron" :size="17" />
        </button>
        <div :id="`sidebar-group-${group.id}`" v-show="groupOpen(group.id)" class="sidebar-group-items">
          <template v-for="item in group.items" :key="'children' in item ? item.id : item.to">
            <section v-if="'children' in item" class="sidebar-folder" :class="{ open: folderOpen(item.id), active: entryActive(item) }">
              <button
                class="sidebar-folder-trigger"
                type="button"
                :title="props.collapsed ? item.label : undefined"
                :aria-label="item.label"
                :aria-expanded="folderOpen(item.id)"
                :aria-controls="`sidebar-folder-${item.id}`"
                @click="toggleFolder(item.id)"
              >
                <component :is="item.icon" :size="18" stroke-width="1.8" />
                <span>{{ item.label }}</span>
                <ChevronDown class="sidebar-folder-chevron" :size="16" />
              </button>
              <div :id="`sidebar-folder-${item.id}`" v-show="folderOpen(item.id)" class="sidebar-folder-items">
                <RouterLink
                  v-for="child in item.children"
                  :key="child.to"
                  :to="child.to"
                  :title="props.collapsed ? child.label : undefined"
                  :aria-label="child.label"
                  :aria-current="itemActive(child) ? 'page' : undefined"
                  :class="{ active: itemActive(child) }"
                  @click="emit('closeMobile')"
                >
                  <component :is="child.icon" :size="18" stroke-width="1.8" />
                  <span>{{ child.label }}</span>
                </RouterLink>
              </div>
            </section>
            <RouterLink
              v-else
              :to="item.to"
              :title="props.collapsed ? item.label : undefined"
              :class="{ active: itemActive(item) }"
              @click="emit('closeMobile')"
            >
              <component :is="item.icon" :size="18" stroke-width="1.8" />
              <span>{{ item.label }}</span>
            </RouterLink>
          </template>
        </div>
      </section>
    </nav>

    <RouterLink v-if="auth.currentUser" class="sidebar-account" to="/profile" :title="props.collapsed ? auth.fullName : undefined" @click="emit('closeMobile')">
      <span class="avatar">{{ auth.currentUser.firstName[0] }}{{ auth.currentUser.lastName[0] }}</span>
      <span><strong>{{ auth.fullName }}</strong><small>{{ auth.roleSummary }}</small></span>
    </RouterLink>
  </aside>
</template>

<style src="../features/commercial/commercial.css"></style>
