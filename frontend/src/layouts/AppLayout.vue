<script setup lang="ts">
import AppSidebar from './AppSidebar.vue'
import AppHeader from './AppHeader.vue'
</script>

<template>
  <div class="app-shell">
    <AppSidebar />
    <div class="app-main">
      <AppHeader />
      <main class="content"><slot /></main>
    </div>
  </div>
</template>

