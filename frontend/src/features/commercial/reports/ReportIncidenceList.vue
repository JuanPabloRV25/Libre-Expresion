<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ChevronRight, FileSpreadsheet } from '@lucide/vue'
import type { ReviewCase, ReviewFinding, SalesReport } from './types'
import { pendingReviewCases } from './reportReviewPresentation'
import WorkspacePagination from './WorkspacePagination.vue'
const props = defineProps<{ report: SalesReport; selectedId: string; locked: boolean }>()
const emit = defineEmits<{ select: [value: { item: ReviewCase; finding: ReviewFinding }] }>()
const page = ref(1)
const entries = computed(() => pendingReviewCases(props.report.data.preparation!.review!).flatMap(item => item.findings
  .filter(finding => finding.resolution === 'pending').map(finding => ({ item, finding,
    row: props.report.groups.find(row => row.key === item.rowKey) }))))
const visible = computed(() => entries.value.slice((page.value - 1) * 5, page.value * 5))
watch(() => props.selectedId, id => {
  const index = entries.value.findIndex(value => value.finding.id === id)
  if (index >= 0) page.value = Math.floor(index / 5) + 1
}, { immediate: true })
watch(() => entries.value.length, total => { page.value = Math.min(page.value, Math.max(1, Math.ceil(total / 5))) })
</script>
<template>
  <section class="rw-incidence-list" aria-labelledby="rw-incidence-list-title">
    <header><h2 id="rw-incidence-list-title">Incidencias pendientes <span>{{ entries.length }}</span></h2></header>
    <ul>
      <li v-for="entry in visible" :key="entry.finding.id">
        <button type="button" :class="{ selected: entry.finding.id === selectedId }" :disabled="locked"
          :aria-current="entry.finding.id === selectedId ? 'true' : undefined" aria-controls="rw-active-incidence"
          @click="emit('select', entry)">
          <span class="rw-incidence-item-title"><strong>{{ entry.finding.field }}</strong><small>{{ entry.finding.initialClassification === 'conflict' ? 'Conflicto' : 'Validación' }}</small></span>
          <span v-if="entry.row" class="rw-incidence-sale">Venta {{ entry.row.number || 'sin número' }} · {{ entry.row.client || 'Cliente sin informar' }}</span>
          <span v-else class="rw-incidence-sale"><FileSpreadsheet :size="15" /> Archivo de origen</span>
          <span class="rw-incidence-reason">{{ entry.finding.reason }}</span>
          <span class="rw-incidence-open">Ver incidencia <ChevronRight :size="15" /></span>
        </button>
      </li>
    </ul>
    <p v-if="locked" class="rw-small-note" role="status">Guarda o descarta tu cambio para abrir otra incidencia.</p>
    <fieldset :disabled="locked"><WorkspacePagination v-model="page" :total="entries.length" label="Páginas de incidencias" /></fieldset>
  </section>
</template>
<style scoped>
.rw-incidence-list { min-width: 0; align-self: start; border: 1px solid var(--rw-border); border-radius: 16px; background: white; overflow: hidden; }
header { padding: 20px; border-bottom: 1px solid var(--rw-border); }
h2 { display: flex; gap: 10px; align-items: center; justify-content: space-between; font-size: 16px; margin: 0; }
h2 span { border-radius: 20px; padding: 3px 9px; font-size: 13px; background: var(--rw-soft); }
ul { list-style: none; padding: 10px; margin: 0; display: grid; gap: 8px; }
li { min-width: 0; }
li button { display: grid; gap: 8px; text-align: left; width: 100%; min-width: 0; padding: 15px; border: 1px solid var(--rw-border); border-radius: 12px; background: white; color: var(--rw-ink); cursor: pointer; font: inherit; }
li button:hover, li button.selected { border-color: var(--rw-accent); background: var(--orange-soft); }
li button:focus-visible { outline: 2px solid var(--rw-accent); outline-offset: 2px; }
li button:disabled { cursor: default; opacity: .65; }
.rw-incidence-item-title { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; }
.rw-incidence-item-title small { font-size: 11px; color: var(--rw-muted); }
.rw-incidence-sale { display: flex; align-items: center; gap: 6px; font-size: 13px; overflow-wrap: anywhere; }
.rw-incidence-reason { display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; color: var(--rw-muted); font-size: 13px; line-height: 1.5; overflow-wrap: anywhere; }
.rw-incidence-open { display: flex; align-items: center; gap: 4px; color: var(--rw-accent); font-size: 12px; font-weight: 600; }
fieldset { border: 0; padding: 0 12px 12px; margin: 0; min-width: 0; }
.rw-small-note { padding: 0 20px; }
:deep(.rw-pagination) { flex-direction: column; align-items: stretch; gap: 10px; }
:deep(.rw-page-controls) { justify-content: center; flex-wrap: wrap; }
:deep(.rw-page-selector) { flex-wrap: wrap; }
@media (max-width: 400px) { :deep(.rw-page-direction span) { display: none; } }
</style>
