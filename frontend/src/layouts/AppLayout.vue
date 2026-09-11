<script setup lang="ts">
import { ref } from 'vue'
import { Menu } from '@lucide/vue'
import AppSidebar from './AppSidebar.vue'
import AppHeader from './AppHeader.vue'

const sidebarCollapsed = ref(false)
const mobileSidebarOpen = ref(false)
</script>

<template>
  <div class="app-shell" :class="{ 'sidebar-collapsed': sidebarCollapsed }">
    <AppSidebar
      :collapsed="sidebarCollapsed"
      :mobile-open="mobileSidebarOpen"
      @toggle-collapsed="sidebarCollapsed = !sidebarCollapsed"
      @close-mobile="mobileSidebarOpen = false"
    />
    <div class="app-main">
      <button class="mobile-sidebar-trigger" type="button" aria-label="Abrir navegación" @click="mobileSidebarOpen = true"><Menu :size="21" /></button>
      <AppHeader />
      <main class="content"><slot /></main>
    </div>
  </div>
</template>
