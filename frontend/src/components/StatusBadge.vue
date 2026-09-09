<script setup lang="ts">
defineProps<{ active: boolean; label?: string }>()
</script>

<template>
  <span class="status-badge" :class="active ? 'is-active' : 'is-inactive'">
    {{ label ?? (active ? 'Activo' : 'Inactivo') }}
  </span>
</template>

