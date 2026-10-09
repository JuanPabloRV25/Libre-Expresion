<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { CircleAlert, Pencil, Search } from '@lucide/vue'
import type { ReportChange, ReportReview, SalesGroup } from './types'
import { finalFields, formatFinalValue, searchRow } from './preparedReport'
import WorkspacePagination from './WorkspacePagination.vue'
import { reviewDate } from './reportReviewPresentation'
const props = defineProps<{ rows: SalesGroup[]; changes?: ReportChange[]; review?: ReportReview | null; editable?: boolean; facturaEditable?: boolean; savingFactura?: string; highlightedKey?: string }>()
const emit = defineEmits<{ edit: [row: SalesGroup]; dirty: [value: boolean]; saveFactura: [value: { key: string; factura: string }] }>()
const facturaDrafts = ref<Record<string, string>>({})
const editingFacturas = ref<Record<string, boolean>>({})
function facturaValue(row: SalesGroup) { return facturaDrafts.value[row.key] ?? row.factura }
function facturaChanged(row: SalesGroup, value: string) {
  if (value === row.factura) delete facturaDrafts.value[row.key]
  else facturaDrafts.value[row.key] = value
  emit('dirty', Object.keys(facturaDrafts.value).length > 0)
}
async function editFactura(row: SalesGroup, event: Event) {
  if (!props.facturaEditable || props.savingFactura) return
  const cell = (event.currentTarget as HTMLElement).closest('.rw-inline-factura')
  editingFacturas.value[row.key] = true
  await nextTick()
  cell?.querySelector<HTMLInputElement>('input')?.focus()
}
function cancelFactura(row: SalesGroup) {
  if (props.savingFactura) return
  delete facturaDrafts.value[row.key]
  delete editingFacturas.value[row.key]
  emit('dirty', Object.keys(facturaDrafts.value).length > 0)
}
function saveFactura(row: SalesGroup) {
  if (!props.facturaEditable || props.savingFactura || facturaDrafts.value[row.key] === undefined) return
  emit('saveFactura', { key: row.key, factura: facturaValue(row) })
}
function opDecision(row: SalesGroup) {
  const findings = props.review?.cases.filter(item => item.rowKey === row.key).flatMap(item => item.findings) ?? []
  if (findings.some(finding => finding.field === 'NUMERO OP' && finding.resolution === 'pending')) return null
  const resolved = findings.filter(finding => finding.field === 'NUMERO OP' && finding.resolution === 'resolved' && finding.provenance === 'human')
  return [...(props.review?.decisions ?? [])].reverse().find(decision => resolved.some(finding => finding.decisionId === decision.id)) ?? null
}
const search = ref(''), page = ref(1)
const filtered = computed(() => props.rows.filter(row => searchRow(row, search.value)))
const visible = computed(() => filtered.value.slice((page.value - 1) * 5, page.value * 5))
function observations(row: SalesGroup, field: string) {
  if (props.review) return props.review.cases.filter(item => item.rowKey === row.key)
    .flatMap(item => item.findings).filter(finding => finding.field === field && finding.resolution === 'pending').map(finding => finding.reason).join('\n')
  const reasons = props.changes?.filter(change => change.rowKey === row.key && change.field === field &&
    (change.kind === 'observation' || change.kind === 'shared_amount_observation') && row.issues.includes(change.reason)).map(change => change.reason) ?? []
  return [...new Set(reasons)].join('\n')
}
watch(() => props.rows, rows => {
  const keys = new Set(rows.map(row => row.key))
  for (const key of Object.keys(editingFacturas.value)) if (!keys.has(key)) {
    delete facturaDrafts.value[key]
    delete editingFacturas.value[key]
  }
  for (const row of rows) if (facturaDrafts.value[row.key] === row.factura) {
    delete facturaDrafts.value[row.key]
    delete editingFacturas.value[row.key]
  }
  emit('dirty', Object.keys(facturaDrafts.value).length > 0)
})
watch(search, () => { page.value = 1 })
watch(() => props.rows.length, () => { page.value = Math.min(page.value, Math.max(1, Math.ceil(filtered.value.length / 5))) })
watch(() => props.highlightedKey, key => {
  const target = props.rows.find(row => row.key === key)
  if (!target) return
  if (!searchRow(target, search.value)) search.value = ''
  const position = filtered.value.findIndex(row => row.key === key)
  if (position >= 0) page.value = Math.floor(position / 5) + 1
}, { immediate: true })
</script>
<template>
  <div class="rw-result-table">
    <div class="rw-table-tools">
      <label class="rw-search"><Search :size="18" aria-hidden="true" /><input v-model="search" aria-label="Buscar en VENTAS MES" placeholder="Buscar en el reporte" /></label>
      <span class="rw-table-count">{{ filtered.length }} {{ filtered.length === 1 ? 'fila' : 'filas' }}</span>
    </div>
    <p v-if="!visible.length" class="rw-empty">{{ search ? 'No hay filas que coincidan con tu búsqueda.' : 'Este reporte no contiene filas.' }}</p>
    <div v-else class="rw-table-adaptive" role="region" aria-label="Tabla VENTAS MES">
      <table class="rw-excel-table"><caption class="sr-only">VENTAS MES · Resultado guardado con las diez columnas del Excel</caption>
        <thead><tr><th v-for="field in finalFields" :key="field.key" :class="`rw-col-${field.key}`" scope="col">{{ field.label }}</th><th v-if="editable" class="rw-col-action" scope="col"><span class="sr-only">Editar fila</span></th></tr></thead>
        <tbody><tr v-for="row in visible" :key="row.key" :class="{ 'rw-highlighted': row.key === highlightedKey }" :data-row-key="row.key">
          <td v-for="field in finalFields" :key="field.key" :class="`rw-col-${field.key}`" :data-label="field.label">
            <span v-if="observations(row, field.label)" class="rw-observation" tabindex="0" :aria-label="`Observación de ${field.label}: ${observations(row, field.label)}`"><CircleAlert :size="15" /><span class="rw-observation-tip">{{ observations(row, field.label) }}</span></span>
            <div v-if="field.key === 'factura' && facturaEditable" class="rw-inline-factura">
              <template v-if="editingFacturas[row.key]">
                <input :value="facturaValue(row)" :aria-label="`FACTURA de venta ${row.number || 'sin número'}`" type="text" :disabled="!!savingFactura" placeholder="Escribir" @input="facturaChanged(row, ($event.target as HTMLInputElement).value)" @keydown.enter.prevent="saveFactura(row)" @keydown.esc.prevent="cancelFactura(row)" />
                <div class="rw-factura-actions"><button type="button" :aria-label="`Guardar factura de venta ${row.number || 'sin número'}`" :disabled="!!savingFactura || facturaDrafts[row.key] === undefined" class="rw-button rw-button-outlined" @click="saveFactura(row)">{{ savingFactura === row.key ? 'Guardando…' : 'Guardar' }}</button><button type="button" :aria-label="`Cancelar edición de factura de venta ${row.number || 'sin número'}`" :disabled="!!savingFactura" class="rw-button rw-button-subtle" @click="cancelFactura(row)">Cancelar</button></div>
              </template>
              <div v-else class="rw-factura-display"><span class="rw-cell-value" :class="{ 'rw-cell-empty': !row.factura }">{{ row.factura || '—' }}</span><button type="button" :aria-label="`Editar factura de venta ${row.number || 'sin número'}`" :disabled="!!savingFactura" class="rw-button rw-button-subtle" @click="editFactura(row, $event)"><Pencil :size="14" aria-hidden="true" />Editar</button></div>
            </div>
            <span v-else class="rw-cell-value" :class="{ 'rw-cell-empty': !formatFinalValue(row, field.key) }">{{ formatFinalValue(row, field.key) || '—' }}</span>
            <small v-if="field.key === 'op' && opDecision(row)" class="rw-cell-human" :title="`${opDecision(row)!.actor} · ${reviewDate(opDecision(row)!.occurredAt)}`">{{ opDecision(row)!.action === 'keep_na' ? 'N/A confirmado por la auxiliar' : 'OP confirmada por la auxiliar' }}</small>
          </td>
          <td v-if="editable" class="rw-col-action"><button type="button" class="rw-icon-button" :aria-label="`Editar fila de OP ${row.op || 'sin número'}`" @click="emit('edit', row)"><Pencil :size="17" /><span class="rw-adaptive-edit-label">Editar fila</span></button></td>
        </tr></tbody>
      </table>
    </div>
    <WorkspacePagination v-model="page" :total="filtered.length" />
  </div>
</template>
