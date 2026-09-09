<script setup lang="ts">
defineProps<{ compact?: boolean; inverse?: boolean }>()
</script>

<template>
  <div class="brand-mark" :class="{ compact, inverse }">
    <img src="/logo-horizontal.png" alt="Libre Expresión" />
    <div v-if="!compact" class="brand-copy">
      <strong>Portal</strong>
      <span>Libre Expresión</span>
    </div>
  </div>
</template>

