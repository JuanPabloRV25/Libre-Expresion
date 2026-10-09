<script setup lang="ts">
import { computed } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
const props = defineProps<{ page: number; total: number; pageSize: number; label: string }>()
const emit = defineEmits<{ change: [page: number] }>()
const pages = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
const pageItems = computed(() => {
  if (pages.value <= 7) return Array.from({ length: pages.value }, (_, index) => index + 1)
  const start = props.page <= 3 ? 2 : props.page >= pages.value - 2 ? pages.value - 3 : props.page - 1
  const end = start + 2
  const items: (number | string)[] = [1]
  if (start > 2) items.push('gap-start')
  for (let number = start; number <= end; number++) items.push(number)
  if (end < pages.value - 1) items.push('gap-end')
  items.push(pages.value)
  return items
})
const first = computed(() => props.total ? (props.page - 1) * props.pageSize + 1 : 0)
const last = computed(() => Math.min(props.page * props.pageSize, props.total))
function jump(event: Event) {
  emit('change', Number((event.target as HTMLSelectElement).value))
}
</script>
<template>
  <nav class="reports-pagination" :aria-label="label">
    <span class="reports-pagination-range">{{ first }}–{{ last }} de {{ total }} · {{ pageSize }} por página</span>
    <div class="reports-pagination-controls">
      <div class="reports-pagination-buttons">
        <button class="reports-page-button reports-page-arrow" aria-label="Anterior" title="Página anterior" :disabled="page <= 1" @click="emit('change', page - 1)"><ChevronLeft :size="17" aria-hidden="true" /></button>
        <template v-for="item in pageItems" :key="item">
          <button v-if="typeof item === 'number'" class="reports-page-button reports-page-number" :class="{ selected: item === page, nearby: Math.abs(item - page) <= 1 }" :aria-label="`Página ${item}`" :aria-current="item === page ? 'page' : undefined" @click="emit('change', item)">{{ item }}</button>
          <span v-else class="reports-page-gap" aria-hidden="true">…</span>
        </template>
        <button class="reports-page-button reports-page-arrow" aria-label="Siguiente" title="Página siguiente" :disabled="page >= pages" @click="emit('change', page + 1)"><ChevronRight :size="17" aria-hidden="true" /></button>
      </div>
      <label class="reports-page-select">Página <select :value="page" :aria-label="`Ir a página: ${label}`" :disabled="pages === 1" @change="jump"><option v-for="number in pages" :key="number" :value="number">{{ number }}</option></select><span>de {{ pages }}</span></label>
    </div>
  </nav>
</template>
