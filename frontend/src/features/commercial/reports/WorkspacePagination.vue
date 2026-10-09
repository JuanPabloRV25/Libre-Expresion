<script setup lang="ts">
import { computed } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
const props = defineProps<{ modelValue: number; total: number; label?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()
const pages = computed(() => Math.max(1, Math.ceil(props.total / 5)))
const range = computed(() => props.total ? `${(props.modelValue - 1) * 5 + 1}–${Math.min(props.modelValue * 5, props.total)} de ${props.total}` : '0 registros')
function go(value: number) { emit('update:modelValue', Math.max(1, Math.min(pages.value, value))) }
</script>
<template>
  <nav class="rw-pagination" :aria-label="label || 'Páginas del reporte'">
    <span class="rw-range" aria-live="polite">{{ range }}<small>5 filas por página</small></span>
    <div class="rw-page-controls">
      <button type="button" class="rw-page-direction" :disabled="modelValue <= 1" aria-label="Página anterior" @click="go(modelValue - 1)"><ChevronLeft :size="17" aria-hidden="true" /><span>Anterior</span></button>
      <label class="rw-page-selector"><span>Página</span><select :value="modelValue" aria-label="Ir a la página" @change="go(Number(($event.target as HTMLSelectElement).value))"><option v-for="number in pages" :key="number" :value="number">{{ number }}</option></select><span>de {{ pages }}</span></label>
      <button type="button" class="rw-page-direction" :disabled="modelValue >= pages" aria-label="Página siguiente" @click="go(modelValue + 1)"><span>Siguiente</span><ChevronRight :size="17" aria-hidden="true" /></button>
    </div>
  </nav>
</template>
