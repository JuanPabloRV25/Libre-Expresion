<script setup lang="ts">
import { FileSpreadsheet } from '@lucide/vue'
defineProps<{ sourceFile: string; version: number }>()
</script>

<template>
  <div class="rw-source-identity">
    <span class="rw-source-identity-icon" aria-hidden="true"><FileSpreadsheet :size="21" /></span>
    <div class="rw-source-identity-name"><span>Archivo de origen</span><strong>{{ sourceFile }}</strong></div>
    <span class="rw-source-identity-version">Versión {{ version }}</span>
  </div>
</template>

<style scoped>
.rw-source-identity { display: flex; align-items: center; flex-wrap: wrap; gap: 10px 12px; padding: 12px 14px; border: 1px solid var(--line); border-radius: 10px; background: var(--surface); min-width: 0; margin-top: 14px; }
.rw-source-identity-icon { display: inline-flex; align-items: center; justify-content: center; width: 38px; height: 38px; flex: 0 0 auto; border-radius: 9px; color: var(--orange-dark); background: var(--orange-soft); }
.rw-source-identity-name { display: grid; gap: 4px; flex: 1 1 180px; min-width: 0; }
.rw-source-identity-name > span { color: var(--muted); font-size: .74rem; }
.rw-source-identity-name > strong { font-size: .86rem; font-weight: 600; line-height: 1.5; overflow-wrap: anywhere; }
.rw-source-identity-version { flex: 0 0 auto; border: 1px solid var(--line); border-radius: 6px; padding: 4px 8px; color: var(--muted); font-size: .73rem; font-variant-numeric: tabular-nums; }
@media (max-width: 450px) { .rw-source-identity { gap: 8px 10px; padding: 11px; align-items: flex-start; }.rw-source-identity-icon { width: 30px; height: 30px; }.rw-source-identity-name { flex-basis: calc(100% - 40px); }.rw-source-identity-version { margin-left: 40px; } }
</style>
