<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { Check, CircleAlert, CircleCheck, FileSpreadsheet, Save, Search, X } from '@lucide/vue'
import { inputMoney, money } from './reportService'
import ReportPagination from './ReportPagination.vue'
import ReportExcelPreview from './ReportExcelPreview.vue'
import type { GroupEdit, OpRecord, SalesDetail, SalesGroup } from './types'
import './record-editor.css'

type Task = 'op' | 'excel'
const props = defineProps<{
  group: SalesGroup; details: SalesDetail[]; ops: OpRecord[]; edit: GroupEdit | null
  groups: SalesGroup[]; edits: GroupEdit[]; canEdit: boolean; busy: boolean
  dirty: boolean; regroup: boolean; codeChoices: Record<string, boolean>; initialTask?: Task
}>()
const emit = defineEmits<{
  change: []; selectOp: [detail: SalesDetail]; structuralChange: []; setCode: [id: string, value: boolean]
  undoDetail: [detail: SalesDetail]; undoGroup: []; save: []; openGroup: [key: string, task: Task]; close: []
}>()
const task = ref<Task>('op')
const detailId = ref('')
const detailPage = ref(1)
const pageSize = 5
const pickerOpen = ref(false)
const opSearch = ref('')
const searchInput = ref<HTMLInputElement | null>(null)
const tabButtons = ref<HTMLButtonElement[]>([])
const included = computed(() => props.details.filter(d => !d.excluded))
const needsOp = (d: SalesDetail) => !d.excluded && (!d.selectedOpId || !d.reviewed || !d.selectedProduct.trim())
const pendingDetails = computed(() => included.value.filter(needsOp))
const missingReasons = computed(() => props.details.filter(d => d.excluded && !d.reason.trim()))
const detail = computed(() => props.details.find(d => d.id === detailId.value) ?? props.details[0] ?? null)
const selectedOp = computed(() => props.ops.find(op => op.id === detail.value?.selectedOpId) ?? null)
const visibleDetails = computed(() => props.details.slice((detailPage.value - 1) * pageSize, detailPage.value * pageSize))
const detailIndex = computed(() => Math.max(0, props.details.findIndex(d => d.id === detailId.value)))
const factura = computed(() => props.edit?.factura ?? props.group.factura)
const amount = computed(() => props.edit ? props.edit.amount : props.group.amount)
const remainingSavedIssues = computed(() => props.group.issues.filter(issue => ![
  'Confirma la OP y el producto de cada detalle.', 'Completa el producto que falta en el registro histórico.',
  'Completa FACTURA.', 'Completa el importe de este registro.',
].includes(issue)))
const pending = computed(() => {
  const items: string[] = []
  if (missingReasons.value.length) items.push('Completa el motivo de la exclusión.')
  if (pendingDetails.value.length) items.push(pendingDetails.value.length === 1
    ? 'Confirma la OP y el producto de un detalle.' : `Confirma la OP y el producto de ${pendingDetails.value.length} detalles.`)
  if (!props.regroup) {
    if (!factura.value.trim()) items.push('Completa FACTURA en los datos del Excel.')
    if (amount.value === null) items.push('Completa el importe del registro.')
    items.push(...remainingSavedIssues.value)
  }
  return items
})
const status = computed(() => {
  if (pending.value.length) return pending.value[0]
  if (props.regroup) return 'Guarda el reporte para actualizar los registros resultantes.'
  if (props.dirty) return 'Datos del registro completos. Guarda para actualizar la revisión.'
  return 'Registro revisado y guardado.'
})
const nextTask = computed<Task>(() => pendingDetails.value.length || missingReasons.value.length || remainingSavedIssues.value.length ? 'op' : 'excel')
const candidates = computed(() => {
  const query = opSearch.value.trim().toLocaleLowerCase('es')
  return props.ops.filter(op => `${op.number} ${op.code} ${op.client} ${op.product}`.toLocaleLowerCase('es').includes(query)).slice(0, 50)
})
const selectedRetained = computed(() => !!detail.value?.selectedOpId && !candidates.value.some(op => op.id === detail.value?.selectedOpId))
const codeValue = computed(() => detail.value ? props.codeChoices[detail.value.id] ?? detail.value.usePortalCode : false)
const canConfirm = computed(() => !!detail.value?.selectedOpId && !!detail.value.selectedProduct.trim() && !detail.value.excluded)

function activateDetail(id: string) {
  detailId.value = id; pickerOpen.value = false; opSearch.value = ''
  detailPage.value = Math.floor(Math.max(0, props.details.findIndex(d => d.id === id)) / pageSize) + 1
}
function initialize() {
  const first = props.details.find(d => d.excluded && !d.reason.trim()) ?? props.details.find(needsOp) ?? props.details[0]
  activateDetail(first?.id ?? '')
  task.value = props.initialTask ?? nextTask.value
}
watch(() => props.group.key, initialize, { immediate: true })
watch(() => props.initialTask, value => { if (value) task.value = value })
watch(() => props.details.length, () => {
  if (!props.details.some(d => d.id === detailId.value)) initialize()
  detailPage.value = Math.min(detailPage.value, Math.max(1, Math.ceil(props.details.length / pageSize)))
})
function changeDetailPage(page: number) {
  detailPage.value = page
  const first = visibleDetails.value[0]
  if (first) { detailId.value = first.id; pickerOpen.value = false; opSearch.value = '' }
}
async function goToPending() {
  task.value = nextTask.value
  const first = props.details.find(d => d.excluded && !d.reason.trim()) ?? pendingDetails.value[0]
  if (first) activateDetail(first.id)
  await nextTick()
  if (task.value === 'excel') {
    const id = !factura.value.trim() ? `record-factura-${props.group.key}` : `record-amount-${props.group.key}`
    const field = document.getElementById(id) as HTMLInputElement | null
    if (field) { field.focus({ preventScroll: true }); field.scrollIntoView({ behavior: 'smooth', block: 'center' }) }
  }
}
async function showPicker() {
  pickerOpen.value = !pickerOpen.value
  if (pickerOpen.value) { opSearch.value = ''; await nextTick(); searchInput.value?.focus() }
}
function chooseOp(event: Event) {
  const current = detail.value
  if (!current || !props.canEdit || props.busy) return
  current.selectedOpId = (event.target as HTMLSelectElement).value || null
  emit('selectOp', current)
}
function changeDetail<K extends keyof SalesDetail>(field: K, value: SalesDetail[K], structural = false) {
  const current = detail.value
  if (!current || !props.canEdit || props.busy) return
  current[field] = value
  if (field === 'selectedProduct') current.reviewed = false
  if (structural) emit('structuralChange')
  else emit('change')
}
function confirmDetail() {
  const current = detail.value
  if (!current || !canConfirm.value || !props.canEdit || props.busy) return
  current.reviewed = true; emit('change'); pickerOpen.value = false
  const next = pendingDetails.value[0]
  if (next) activateDetail(next.id)
  else if (!props.regroup && !missingReasons.value.length) task.value = 'excel'
}
function changeCode(event: Event) {
  if (detail.value && props.canEdit && !props.busy) emit('setCode', detail.value.id, (event.target as HTMLInputElement).checked)
}
function changeEdit<K extends keyof GroupEdit>(field: K, value: GroupEdit[K]) {
  const current = props.edit
  if (!current || !props.canEdit || props.busy || props.regroup) return
  current[field] = value; emit('change')
}
function text(event: Event) { return (event.target as HTMLInputElement).value }
function detailState(item: SalesDetail) { return item.excluded ? 'Excluido' : needsOp(item) ? 'Por confirmar' : 'Confirmado' }
function moveTab(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  task.value = event.key === 'Home' ? 'op' : event.key === 'End' ? 'excel' : task.value === 'op' ? 'excel' : 'op'
  tabButtons.value[task.value === 'op' ? 0 : 1]?.focus()
}
</script>

<template>
  <div class="report-record-editor">
    <header class="record-review-heading">
      <div><p class="record-review-label">NUMERO {{ group.number }} de Manager</p><h2>Revisar registro</h2><p class="record-review-context">{{ edit?.client ?? group.client }}<span>{{ group.product || group.line || 'Producto por confirmar' }}</span></p></div>
      <button class="button small record-review-close" aria-label="Cerrar revisión del registro" :disabled="busy" @click="emit('close')"><X :size="16" aria-hidden="true" /> Cerrar</button>
    </header>

    <div class="record-review-status" :class="{ complete: !pending.length && !regroup, structural: regroup }" role="status">
      <CircleAlert v-if="pending.length || regroup" :size="20" aria-hidden="true" /><CircleCheck v-else :size="20" aria-hidden="true" />
      <div><strong>{{ status }}</strong><span>{{ dirty ? 'Hay cambios sin guardar en el reporte.' : 'Última revisión guardada.' }}</span></div>
      <button v-if="pending.length" class="button small" @click="goToPending">Ir a lo pendiente</button>
    </div>
    <details v-if="pending.length > 1" class="record-review-pending"><summary>Ver qué falta en este registro</summary><ul><li v-for="item in pending" :key="item">{{ item }}</li></ul></details>

    <div class="record-review-tabs" role="tablist" aria-label="Tareas del registro" @keydown="moveTab">
      <button :id="`record-op-tab-${group.key}`" :ref="element => { if (element) tabButtons[0] = element as HTMLButtonElement }" role="tab" :aria-selected="task === 'op'" :tabindex="task === 'op' ? 0 : -1" :aria-controls="`record-op-panel-${group.key}`" @click="task = 'op'"><CircleCheck :size="17" aria-hidden="true" /> OP y detalles</button>
      <button :id="`record-excel-tab-${group.key}`" :ref="element => { if (element) tabButtons[1] = element as HTMLButtonElement }" role="tab" :aria-selected="task === 'excel'" :tabindex="task === 'excel' ? 0 : -1" :aria-controls="`record-excel-panel-${group.key}`" @click="task = 'excel'"><FileSpreadsheet :size="17" aria-hidden="true" /> Datos del Excel</button>
    </div>

    <section v-show="task === 'op'" :id="`record-op-panel-${group.key}`" role="tabpanel" :aria-labelledby="`record-op-tab-${group.key}`">
      <div v-if="details.length > 1" class="record-detail-list">
        <h3>Detalles del registro</h3>
        <div class="record-detail-options" aria-label="Detalles del registro">
          <button v-for="item in visibleDetails" :key="item.id" :aria-label="`Ver detalle ${details.indexOf(item) + 1}`" :aria-pressed="detail?.id === item.id" :disabled="busy" @click="activateDetail(item.id)">
            <div><span class="record-review-label">Detalle {{ details.indexOf(item) + 1 }}</span><span class="record-detail-snippet">{{ item.detail }}</span></div><span class="record-detail-state" :class="{ pending: needsOp(item), excluded: item.excluded }">{{ detailState(item) }}</span>
          </button>
        </div>
        <ReportPagination v-if="details.length > pageSize" :page="detailPage" :total="details.length" :page-size="pageSize" label="Páginas de detalles" @change="changeDetailPage" />
      </div>

      <article v-if="detail" class="record-detail-review" :aria-label="`Revisar detalle ${detailIndex + 1}`">
        <div class="record-detail-heading"><h3>{{ details.length > 1 ? `Detalle ${detailIndex + 1} de ${details.length}` : 'Detalle de Manager' }}</h3><span class="record-detail-state" :class="{ pending: needsOp(detail), excluded: detail.excluded }">{{ detailState(detail) }}</span></div>
        <p class="record-detail-description">{{ detail.detail }}</p>

        <template v-if="!detail.excluded">
          <div class="record-op-match">
            <div><span class="record-review-label">OP seleccionada del registro</span><strong>{{ detail.selectedOpNumber || 'Sin seleccionar' }}</strong><p>{{ detail.selectedProduct || 'Producto por confirmar' }}</p><small v-if="selectedOp">{{ selectedOp.client }}<span v-if="selectedOp.origin"> · {{ selectedOp.origin }}</span></small></div>
            <button v-if="canEdit" class="button small" :disabled="busy" :aria-expanded="pickerOpen" @click="showPicker">{{ pickerOpen ? 'Cerrar búsqueda de OP' : 'Cambiar OP' }}</button>
          </div>
          <div v-if="pickerOpen && canEdit" class="record-op-picker">
            <label class="record-op-search"><Search :size="18" aria-hidden="true" /><span class="sr-only">Buscar OP por número, cliente o producto</span><input ref="searchInput" v-model="opSearch" :disabled="busy" placeholder="Buscar OP, cliente o producto" /></label>
            <label :for="`record-op-${detail.id}`">OP del detalle<select :id="`record-op-${detail.id}`" aria-label="OP del detalle" :value="detail.selectedOpId ?? ''" :disabled="busy" @change="chooseOp"><option value="">Sin seleccionar</option><option v-if="selectedRetained" :value="detail.selectedOpId ?? ''">{{ detail.selectedOpNumber }} · {{ detail.selectedProduct }} (selección conservada)</option><option v-for="op in candidates" :key="op.id" :value="op.id">{{ op.number }}{{ op.code ? ' / ' + op.code : '' }} · {{ op.client }} · {{ op.product || 'Producto por completar' }}</option></select></label>
            <p class="record-review-help">{{ candidates.length ? `${candidates.length} opciones · hasta 50 coincidencias.` : 'No hay coincidencias. Cambia la búsqueda.' }} La selección se confirma con el botón de abajo.</p>
          </div>
          <label v-if="canEdit && detail.selectedOpId && !selectedOp?.product" class="record-missing-product">Producto que falta en el histórico<input :value="detail.selectedProduct" aria-label="Producto que falta en el histórico" :aria-describedby="`record-product-help-${detail.id}`" maxlength="500" :disabled="busy" @input="changeDetail('selectedProduct', text($event), true)" /><small :id="`record-product-help-${detail.id}`">Se completa en este reporte.</small></label>
          <div v-if="canEdit" class="record-confirm-actions">
            <button class="button" :class="{ primary: !(detail.reviewed && canConfirm) }" :disabled="busy || !canConfirm || detail.reviewed" @click="confirmDetail"><Check :size="17" aria-hidden="true" />{{ detail.reviewed && canConfirm ? 'OP y producto confirmados' : 'Confirmar OP y producto' }}</button>
            <button v-if="detail.reviewed" class="record-review-text-button" :disabled="busy" @click="changeDetail('reviewed', false)">Volver a revisar</button>
          </div>
        </template>
        <div v-else class="record-excluded-notice"><strong>Este detalle está excluido del Excel.</strong><label v-if="canEdit">Motivo<input :value="detail.reason" aria-label="Motivo" :aria-describedby="!detail.reason.trim() ? `record-exclusion-help-${detail.id}` : undefined" maxlength="1000" :disabled="busy" :aria-invalid="!detail.reason.trim()" @input="changeDetail('reason', text($event))" /><small v-if="!detail.reason.trim()" :id="`record-exclusion-help-${detail.id}`">Escribe el motivo para guardar esta exclusión.</small></label><p v-else>Motivo: {{ detail.reason || 'Sin completar' }}</p><button v-if="canEdit" class="button small" :disabled="busy" @click="changeDetail('excluded', false, true)">Volver a incluir</button></div>

        <details class="record-origin"><summary>Ver origen del detalle</summary><dl><div><dt>Archivo</dt><dd>{{ detail.sourceFile }}</dd></div><div><dt>Hoja y fila</dt><dd>{{ detail.sheet }} · Fila {{ detail.sourceRow }}</dd></div><div><dt>OP en Manager</dt><dd>{{ detail.managerOp || 'Sin dato' }}</dd></div><div><dt>Valor original</dt><dd>{{ money(detail.rawAmount) }}</dd></div></dl></details>
        <details v-if="canEdit" class="record-other-corrections"><summary>Otras correcciones de este detalle</summary>
          <label class="record-check"><input type="checkbox" :checked="detail.excluded" :disabled="busy" @change="changeDetail('excluded', ($event.target as HTMLInputElement).checked, true)" />Excluir del reporte</label>
          <label v-if="!detail.excluded">Motivo<input :value="detail.reason" maxlength="1000" :disabled="busy" @input="changeDetail('reason', text($event))" /></label>
          <label v-if="selectedOp?.code" class="record-check"><input type="checkbox" :checked="codeValue" :disabled="busy" @change="changeCode" />Usar el código interno del Portal en lugar del número de pedido</label>
          <label>Grupo manual (opcional)<input :value="detail.manualGroup ?? ''" maxlength="100" :disabled="busy" placeholder="Nombre de la agrupación verificada" @input="changeDetail('manualGroup', text($event), true)" /></label>
          <p class="record-review-help">El mismo nombre reúne detalles del mismo NUMERO. Úsalo cuando hayas comprobado que deben aparecer juntos.</p>
          <button class="button small" :disabled="busy" @click="emit('undoDetail', detail)">Deshacer correspondencia y exclusión</button>
        </details>
      </article>
      <p v-else class="record-review-help">Este registro ya no tiene detalles disponibles.</p>
    </section>

    <section v-show="task === 'excel'" :id="`record-excel-panel-${group.key}`" role="tabpanel" :aria-labelledby="`record-excel-tab-${group.key}`">
      <div v-if="regroup" class="record-regroup-notice"><CircleAlert :size="20" aria-hidden="true" /><div><strong>Actualiza la agrupación antes de completar los datos.</strong><p>La agrupación de la vista previa corresponde a la última revisión guardada. Guarda el reporte para ver los registros resultantes.</p><button v-if="canEdit" class="button small" :disabled="busy || !dirty" @click="emit('save')">Guardar y actualizar registros</button></div></div>
      <ReportExcelPreview :groups="groups" :edits="edits" :active-key="group.key" :can-edit="canEdit" :busy="busy" @open="emit('openGroup', $event, 'excel')" />
      <form v-if="edit && canEdit && !regroup" class="record-excel-form" @submit.prevent="emit('save')">
        <div class="record-excel-form-heading"><h3>Completar la fila seleccionada</h3><span>NUMERO {{ group.number }} · OP {{ group.op || 'por confirmar' }}</span></div>
        <div class="record-primary-fields"><label :for="`record-factura-${group.key}`">FACTURA · Manual<input :id="`record-factura-${group.key}`" :value="edit.factura" aria-label="FACTURA · Manual" :aria-describedby="`record-factura-help-${group.key}`" maxlength="50" :disabled="busy" :aria-invalid="!edit.factura.trim()" @input="changeEdit('factura', text($event))" /><small :id="`record-factura-help-${group.key}`">Columna FACTURA de VENTAS MES.</small></label><label :for="`record-amount-${group.key}`">Importe del registro<input :id="`record-amount-${group.key}`" aria-label="Importe del registro" :aria-describedby="`record-amount-help-${group.key}`" type="number" step="0.01" :value="edit.amount ?? ''" :disabled="busy" :aria-invalid="edit.amount === null" @input="changeEdit('amount', inputMoney($event))" /><small :id="`record-amount-help-${group.key}`">Columna VALOR_BRUT del Excel.</small></label></div>
        <details class="record-secondary-fields"><summary>Corregir estos datos</summary><div class="record-secondary-grid"><label>Cliente<input :value="edit.client ?? group.client" maxlength="500" :disabled="busy" @input="changeEdit('client', text($event))" /></label><label>Línea<input :value="edit.line ?? group.line" maxlength="200" :disabled="busy" @input="changeEdit('line', text($event))" /></label><label>Vendedor<input :value="edit.seller ?? group.seller" maxlength="200" :disabled="busy" @input="changeEdit('seller', text($event))" /></label><label class="record-correction-note">Nota de la corrección<input :value="edit.reason" maxlength="1000" :disabled="busy" @input="changeEdit('reason', text($event))" /></label></div><button type="button" class="button small" :disabled="busy" @click="emit('undoGroup')">Deshacer ajustes del registro</button></details>
      </form>
    </section>
    <footer class="record-review-footer"><p>{{ regroup ? 'Guarda las correcciones y continúa con los registros resultantes.' : 'El guardado incluye todos los cambios del reporte.' }}</p><button v-if="canEdit" class="button primary" :disabled="busy || !dirty" @click="emit('save')"><Save :size="17" aria-hidden="true" />{{ busy ? 'Guardando reporte…' : regroup ? 'Guardar reporte y continuar' : 'Guardar reporte' }}</button></footer>
  </div>
</template>
