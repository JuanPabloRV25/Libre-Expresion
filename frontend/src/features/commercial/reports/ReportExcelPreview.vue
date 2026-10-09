<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ChevronDown, FileSpreadsheet } from '@lucide/vue'
import ReportPagination from './ReportPagination.vue'
import { money } from './reportService'
import type { GroupEdit, SalesGroup } from './types'
import './report-excel-preview.css'

const props = defineProps<{
  groups: SalesGroup[]
  edits: GroupEdit[]
  activeKey: string
  canEdit: boolean
  busy: boolean
}>()
const emit = defineEmits<{ open: [key: string] }>()
const pageSize = 5
const page = ref(1)
const columns = [
  { key: 'op', letter: 'B', label: 'NUMERO OP' },
  { key: 'factura', letter: 'C', label: 'FACTURA' },
  { key: 'number', letter: 'D', label: 'NUMERO' },
  { key: 'date', letter: 'E', label: 'FECHA' },
  { key: 'client', letter: 'F', label: 'NOMBRE' },
  { key: 'term', letter: 'G', label: 'PLAZO' },
  { key: 'amount', letter: 'H', label: 'VALOR_BRUT' },
  { key: 'details', letter: 'I', label: 'DETALLE' },
  { key: 'line', letter: 'J', label: 'LINEA' },
  { key: 'seller', letter: 'K', label: 'VENDEDOR' },
] as const

const editsByKey = computed(() => new Map(props.edits.map(edit => [edit.key, edit])))
const rows = computed(() => props.groups.map((group, index) => {
  const edit = editsByKey.value.get(group.key)
  return {
    ...group,
    excelRow: index + 4,
    factura: edit ? edit.factura : group.factura,
    amount: edit ? edit.amount : group.amount,
    client: edit?.client ?? group.client,
    line: edit?.line ?? group.line,
    seller: edit?.seller ?? group.seller,
  }
}))
const pages = computed(() => Math.max(1, Math.ceil(rows.value.length / pageSize)))
const visibleRows = computed(() => rows.value.slice((page.value - 1) * pageSize, page.value * pageSize))

function positionActive() {
  const index = props.groups.findIndex(group => group.key === props.activeKey)
  if (index >= 0) page.value = Math.floor(index / pageSize) + 1
}
watch(() => props.activeKey, positionActive, { immediate: true })
watch(() => props.groups.map(group => group.key), () => {
  page.value = Math.min(page.value, pages.value)
  positionActive()
})
watch(pages, count => { page.value = Math.min(page.value, count) })

function date(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return match ? `${match[3]}/${match[2]}/${match[1]}` : value
}
function value(row: typeof rows.value[number], key: typeof columns[number]['key']) {
  if (key === 'amount') return money(row.amount)
  if (key === 'date') return date(row.date) || 'Sin completar'
  if (key === 'details') return row.details.join('\n') || 'Sin completar'
  return row[key] || 'Sin completar'
}
</script>

<template>
  <section class="report-excel-preview" aria-label="Vista previa de VENTAS MES">
    <div class="report-excel-preview-heading">
      <span class="report-excel-preview-icon"><FileSpreadsheet :size="22" aria-hidden="true" /></span>
      <div><h3>VENTAS MES</h3><p>Todas las columnas del Excel · {{ groups.length }} registros</p></div>
    </div>
    <p class="report-excel-preview-note">La vista incluye tus cambios. Usa las páginas para ver todos los registros.<span class="report-excel-table-hint"> Desplaza la tabla para ver todas las columnas.</span></p>

    <div v-if="rows.length" class="report-excel-preview-scroll" tabindex="0" role="region" aria-label="Tabla de VENTAS MES, desplazamiento horizontal">
      <table class="report-excel-preview-table">
        <caption class="report-excel-preview-sr">Vista previa de todas las columnas de VENTAS MES. Cinco registros por página.</caption>
        <colgroup><col v-for="column in columns" :key="column.key" :class="`report-excel-col-${column.key}`" /></colgroup>
        <thead><tr><th v-for="column in columns" :key="column.key" scope="col"><span class="report-excel-column-letter">{{ column.letter }}</span>{{ column.label }}</th></tr></thead>
        <tbody>
          <tr v-for="row in visibleRows" :key="row.key" :class="{ 'is-active': row.key === activeKey }" :aria-current="row.key === activeKey ? 'true' : undefined">
            <td v-for="column in columns" :key="column.key" :class="{ 'report-excel-amount': column.key === 'amount', 'report-excel-detail': column.key === 'details' }">
              <template v-if="column.key === 'op'"><span class="report-excel-row-number">Fila {{ row.excelRow }}</span><span class="report-excel-op-value">{{ value(row, column.key) }}</span><span v-if="row.key === activeKey" class="report-excel-active-label">Registro abierto</span></template>
              <template v-else-if="column.key === 'details'"><span v-for="(detail, index) in row.details" :key="index" class="report-excel-detail-item">{{ detail }}</span><span v-if="!row.details.length">Sin completar</span></template>
              <template v-else>{{ value(row, column.key) }}</template>
              <button v-if="column.key === 'op'" type="button" class="report-excel-open" :disabled="busy" :aria-label="`${canEdit ? 'Editar' : 'Ver'} datos de la fila ${row.excelRow}, NUMERO ${row.number}`" @click="emit('open', row.key)">{{ canEdit ? 'Editar datos' : 'Ver datos' }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="rows.length" class="report-excel-preview-cards">
      <details v-for="row in visibleRows" :key="row.key" class="report-excel-preview-card" :class="{ 'is-active': row.key === activeKey }" :aria-label="`Fila ${row.excelRow} del Excel`" :open="row.key === activeKey">
        <summary class="report-excel-card-summary"><div class="report-excel-card-identifiers"><strong>Fila {{ row.excelRow }} · OP {{ row.op || 'por confirmar' }}</strong><span>NUMERO {{ row.number }}</span><span v-if="row.key === activeKey" class="report-excel-active-label">Registro abierto</span></div><ChevronDown :size="18" class="report-excel-card-chevron" aria-hidden="true" /></summary>
        <div class="report-excel-card-content"><dl>
          <div v-for="column in columns" :key="column.key" class="report-excel-card-field" :class="{ 'report-excel-detail': column.key === 'details' }">
            <dt>{{ column.label }}</dt>
            <dd v-if="column.key === 'details'"><span v-for="(detail, index) in row.details" :key="index" class="report-excel-detail-item">{{ detail }}</span><span v-if="!row.details.length">Sin completar</span></dd>
            <dd v-else>{{ value(row, column.key) }}</dd>
          </div>
        </dl>
        <button type="button" class="report-excel-open" :disabled="busy" :aria-label="`${canEdit ? 'Editar' : 'Ver'} datos de la fila ${row.excelRow}, NUMERO ${row.number}`" @click="emit('open', row.key)">{{ canEdit ? 'Editar datos' : 'Ver datos' }}</button>
        </div>
      </details>
    </div>
    <p v-if="!rows.length" class="report-excel-preview-empty">No hay registros incluidos para mostrar en el Excel.</p>
    <ReportPagination :page="page" :total="groups.length" :page-size="pageSize" label="Páginas de la vista previa" @change="page = $event" />
  </section>
</template>
