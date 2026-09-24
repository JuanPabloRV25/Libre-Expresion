<script setup lang="ts">
defineProps<{ compact?: boolean; inverse?: boolean }>()
</script>

<template>
  <div class="brand-mark" :class="{ compact, inverse }">
    <span class="brand-logo-frame"><img src="/logo-horizontal.png" alt="Libre Expresión" /></span>
  </div>
</template>

