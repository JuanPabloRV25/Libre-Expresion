<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import type { ProductionOrderReviewer } from './types'

const props = defineProps<{
  open: boolean; reviewers: ProductionOrderReviewer[]; selectedId: string
  previousReviewer: { id: string; name: string } | null
  loading: boolean; loadError: string; submitError: string; busy: boolean
}>()
const emit = defineEmits<{ select: [id: string]; retry: []; close: []; confirm: [] }>()
const dialog = ref<HTMLElement>()
const selected = computed(() => props.reviewers.find(candidate => candidate.id === props.selectedId))
const previousAvailable = computed(() => props.reviewers.some(candidate => candidate.id === props.previousReviewer?.id))
let previousFocus: HTMLElement | null = null
watch(() => props.open, async (open) => {
  if (open) {
    previousFocus = document.activeElement as HTMLElement | null
    await nextTick()
    dialog.value?.focus()
  } else previousFocus?.focus()
})
function close() { if (!props.busy) emit('close') }
function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') { event.preventDefault(); close() }
  if (event.key !== 'Tab') return
  const controls = Array.from(dialog.value?.querySelectorAll<HTMLElement>('button:not(:disabled), select:not(:disabled)') ?? [])
  const first = controls[0], last = controls.at(-1)
  if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog.value)) { event.preventDefault(); last?.focus() }
  else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialog.value)) { event.preventDefault(); first?.focus() }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="dialog-backdrop" @click.self="close">
      <section ref="dialog" class="dialog-card" role="dialog" aria-modal="true" aria-label="Enviar a revisión comercial" tabindex="-1" :aria-busy="busy || loading" @keydown="onKeydown">
        <h2>Enviar a revisión comercial</h2>
        <p>La OP y sus documentos quedarán bloqueados. La auxiliar elegida será responsable de la revisión y recibirá el aviso.</p>
        <p v-if="loading" role="status">Consultando auxiliares…</p>
        <div v-else-if="loadError" role="alert"><p class="form-error">{{ loadError }}</p><button class="button secondary" type="button" :disabled="busy" @click="emit('retry')">Reintentar</button></div>
        <div v-else-if="!reviewers.length"><p role="status">No hay auxiliares comerciales disponibles para revisar esta orden.</p><button class="button secondary" type="button" :disabled="busy" @click="emit('retry')">Actualizar consulta</button></div>
        <template v-else>
          <p v-if="reviewers.length === 1"><strong>Revisará: {{ reviewers[0]!.name }}</strong></p>
          <label v-else>Auxiliar Comercial
            <select :value="selectedId" :disabled="busy" @change="emit('select', ($event.target as HTMLSelectElement).value)">
              <option value="" disabled>Selecciona una auxiliar</option>
              <option v-for="candidate in reviewers" :key="candidate.id" :value="candidate.id">{{ candidate.name }}{{ candidate.secondaryLabel ? ` · ${candidate.secondaryLabel}` : '' }}{{ candidate.id === previousReviewer?.id ? ' · Revisión anterior' : '' }}</option>
            </select>
          </label>
          <p v-if="previousReviewer && !previousAvailable">{{ previousReviewer.name }} ya no está disponible. Elige a otra auxiliar para este envío.</p>
          <p v-else-if="previousReviewer && reviewers.length > 1">Revisión anterior: {{ previousReviewer.name }}. Puedes elegirla nuevamente o seleccionar otra auxiliar.</p>
          <p v-if="selected && previousReviewer && selected.id !== previousReviewer.id">Este envío quedará asignado a {{ selected.name }} en lugar de {{ previousReviewer.name }}.</p>
        </template>
        <p v-if="submitError" class="form-error" role="alert">{{ submitError }}</p>
        <div class="dialog-actions">
          <button class="button secondary" type="button" :disabled="busy" @click="close">Cancelar</button>
          <button class="button primary" type="button" :disabled="busy || loading || !!loadError || !selected" @click="emit('confirm')">{{ busy ? 'Enviando…' : 'Enviar a revisión' }}</button>
        </div>
      </section>
    </div>
  </Teleport>
</template>
