<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { Eye, RotateCcw, Save, Search, X } from '@lucide/vue'
import { reportService } from './reportService'
import { amountCanBeEdited, amountPresentation, finalFields, formatFinalValue, rowDraft, rowPatch, type RowDraft } from './preparedReport'
import WorkspacePagination from './WorkspacePagination.vue'
import type { PreparedRowEdit, SalesGroup, SalesReport } from './types'
const props = defineProps<{ report: SalesReport; row: SalesGroup }>()
const emit = defineEmits<{ close: []; saved: [report: SalesReport]; dirty: [value: boolean]; download: [] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const draft = ref<RowDraft>(rowDraft(props.row)), dirty = ref(false), working = ref(false), error = ref('')
const amountConfirmed = ref(false)
const preview = ref<SalesReport | null>(null), previewValid = ref(false), restore = ref(false)
const historyReferenceId = ref<string | undefined>(), historySearch = ref(''), historyPage = ref(1)
const historyMode = ref<'reference' | 'individual'>('reference'), chosenHistoryIds = ref<string[]>([]), individualHistoryChanged = ref(false)
const automatic = computed(() => props.report.data.preparation?.automaticRows.find(row => row.key === props.row.key) ?? props.row)
const amountOpChanged = computed(() => !!props.row.amountMode && props.row.amountMode !== 'legacy' && draft.value.op !== props.row.op)
const amountInfo = computed(() => amountOpChanged.value
  ? { label: 'OP modificadas', help: 'Guarda el cambio de OP para actualizar el importe.' } : amountPresentation(props.row))
const editableAmount = computed(() => amountCanBeEdited(props.row) && !amountOpChanged.value)
const canConfirmAmount = computed(() => amountOpChanged.value || props.row.amountMode === 'validation' ? false : props.row.amountMode === 'automatic'
  ? props.report.data.preparation?.review?.cases.some(item => item.rowKey === props.row.key && item.findings.some(finding => finding.code === 'shared_amount_observation' && finding.resolution === 'pending')) ?? false
  : props.row.issues.some(issue => issue.includes('total compartido')))
const history = computed(() => (props.report.data.preparation?.history ?? []).filter(record =>
  `${record.number} ${record.data.cells[5]} ${record.data.cells[6]} ${record.product}`.toLowerCase().includes(historySearch.value.toLowerCase())))
const historyVisible = computed(() => history.value.slice((historyPage.value - 1) * 5, historyPage.value * 5))
const source = computed(() => props.report.data.details.filter(detail => props.row.detailIds.includes(detail.id)))
const sourcePage = ref(1), sourceVisible = computed(() => source.value.slice((sourcePage.value - 1) * 5, sourcePage.value * 5))
const previewRows = computed(() => preview.value?.groups.filter(row => row.key === props.row.key || row.detailIds.some(id => props.row.detailIds.includes(id))) ?? [])
const referenceIds = computed(() => new Set(props.report.data.preparation?.changes.filter(change => change.rowKey === props.row.key).flatMap(change => change.historyIds) ?? []))
const linkedHistory = computed(() => props.report.data.preparation?.history.filter(record => referenceIds.value.has(record.id)) ?? [])
function changed() { if (amountOpChanged.value) amountConfirmed.value = false; dirty.value = true; restore.value = false; previewValid.value = false; error.value = ''; emit('dirty', true) }
function setAmount(event: Event) {
  if (!editableAmount.value) return
  const raw = (event.target as HTMLInputElement).value
  draft.value.amount = raw.trim() === '' ? null : Number(raw)
  amountConfirmed.value = true
  changed()
}
function edits(): PreparedRowEdit[] {
  const patch = restore.value ? { key: props.row.key, restore: true } : rowPatch(props.row, draft.value,
    individualHistoryChanged.value ? chosenHistoryIds.value : undefined, amountConfirmed.value)
  if (!restore.value && historyReferenceId.value) patch.historyReferenceId = historyReferenceId.value
  return [patch]
}
async function showPreview() {
  working.value = true; error.value = ''
  try { preview.value = await reportService.previewPrepared(props.report, edits()); previewValid.value = true; await nextTick(); dialog.value?.querySelector<HTMLElement>('.rw-edit-preview')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); return true }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible previsualizar el ajuste.'; return false }
  finally { working.value = false }
}
async function save() {
  if (working.value || !dirty.value) return !dirty.value
  if (!previewValid.value && !await showPreview()) return false
  working.value = true; error.value = ''
  try { const saved = await reportService.savePrepared(props.report, edits()); dirty.value = false; emit('dirty', false); emit('saved', saved); return true }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible guardar. Tus cambios siguen aquí.'; return false }
  finally { working.value = false }
}
function restoreProposal() { restore.value = true; draft.value = rowDraft(automatic.value); amountConfirmed.value = false; dirty.value = true; previewValid.value = false; historyReferenceId.value = undefined; individualHistoryChanged.value = false; emit('dirty', true); void showPreview() }
function selectReference(id: string) { changed(); individualHistoryChanged.value = false; historyReferenceId.value = id }
function chooseIndividual(id: string, checked: boolean) {
  changed(); historyReferenceId.value = undefined; individualHistoryChanged.value = true
  chosenHistoryIds.value = checked ? [...new Set([...chosenHistoryIds.value, id])] : chosenHistoryIds.value.filter(value => value !== id)
}
function switchHistory(mode: 'reference' | 'individual') { historyMode.value = mode; if (mode === 'individual' && !individualHistoryChanged.value) chosenHistoryIds.value = linkedHistory.value.map(record => record.id) }
function resetField(field: typeof finalFields[number]['key']) {
  const original = rowDraft(automatic.value)
  if (field === 'amount') draft.value.amount = original.amount
  else draft.value[field] = original[field]
  if (field === 'op') { historyReferenceId.value = undefined; individualHistoryChanged.value = false }
  changed()
}
function close() { if (!working.value) { emit('dirty', false); emit('close') } }
onMounted(async () => { await nextTick(); dialog.value?.showModal(); dialog.value?.querySelector<HTMLInputElement>('input')?.focus() })
onUnmounted(() => { dialog.value?.close() })
defineExpose({ save })
</script>
<template>
  <Teleport to="body">
    <dialog ref="dialog" class="rw-row-dialog" aria-labelledby="rw-editor-title" @cancel.prevent="close">
      <header class="rw-editor-heading"><div><p>VENTAS MES</p><h2 id="rw-editor-title">Editar fila</h2><span>{{ row.op ? `OP ${row.op}` : 'OP sin determinar' }}</span></div><button class="rw-icon-button" type="button" aria-label="Cancelar y cerrar edición" :disabled="working" @click="close"><X :size="21" /></button></header>
      <div class="rw-editor-body">
        <p v-if="error" class="rw-error" role="alert">{{ error }}</p>
        <p class="rw-editor-note">Modifica solo los datos que necesites. El archivo original se conserva.</p>
        <div class="rw-edit-fields">
          <div v-for="field in finalFields" :key="field.key" class="rw-edit-field" :class="{ wide: field.key === 'detail' || field.key === 'op' }"><div class="rw-field-heading"><label :for="`rw-edit-${field.key}`">{{ field.label }}<small v-if="field.key === 'factura'">Editable</small><small v-if="field.key === 'amount' && amountInfo">{{ amountInfo.label }}</small></label><button v-if="(field.key !== 'amount' || editableAmount) && draft[field.key] !== rowDraft(automatic)[field.key]" type="button" class="rw-field-reset" :disabled="working" :aria-label="`Recuperar propuesta de ${field.label}`" title="Recuperar el dato automático" @click="resetField(field.key)"><RotateCcw :size="13" /></button></div>
            <textarea v-if="field.key === 'detail'" :id="`rw-edit-${field.key}`" v-model="draft.detail" :aria-label="field.label" rows="5" :disabled="working || restore" @input="changed" />
            <input v-else-if="field.key === 'amount'" :id="`rw-edit-${field.key}`" :value="draft.amount ?? ''" :aria-label="field.label" :aria-describedby="amountInfo ? 'rw-edit-amount-help' : undefined" type="number" step="0.01" inputmode="decimal" :readonly="!editableAmount" :disabled="working || restore" :placeholder="amountInfo ? (editableAmount ? 'Escribe el total' : 'Sin determinar') : undefined" @input="setAmount" />
            <input v-else :id="`rw-edit-${field.key}`" v-model="draft[field.key]" :aria-label="field.label" :type="field.key === 'date' ? 'date' : 'text'" :disabled="working || restore" @input="changed" />
            <p v-if="field.key === 'amount' && amountInfo" id="rw-edit-amount-help" class="rw-small-note">{{ amountInfo.help }}</p>
            <button v-if="field.key === 'amount' && draft.amount !== null && canConfirmAmount" type="button" class="rw-amount-confirm" :disabled="working || restore || amountConfirmed" @click="amountConfirmed = true; changed()">{{ amountConfirmed ? 'Importe confirmado para esta fila' : 'Mantener este importe en esta fila' }}</button>
          </div>
        </div>
        <details class="rw-editor-disclosure"><summary>Ver datos de origen</summary><p class="rw-small-note">Informe de ventas · valores conservados sin IVA, TOTAL ni NUMERO_REM.</p>
          <div class="rw-source-scroll" role="region" aria-label="Filas originales de ventas"><table class="rw-source-table"><thead><tr><th>Fila</th><th>NUMERO</th><th>FECHA</th><th>NOMBRE</th><th>PLAZO</th><th>VALOR_BRUT</th><th>DETALLE</th><th>LINEA</th><th>VENDEDOR</th><th>NUMERO_OP</th></tr></thead><tbody><tr v-for="detail in sourceVisible" :key="detail.id"><td data-label="Fila">{{ detail.sourceRow }}</td><td data-label="NUMERO">{{ detail.number }}</td><td data-label="FECHA">{{ detail.date }}</td><td data-label="NOMBRE">{{ detail.client }}</td><td data-label="PLAZO">{{ detail.term }}</td><td data-label="VALOR_BRUT">{{ detail.rawAmount }}</td><td data-label="DETALLE">{{ detail.detail }}</td><td data-label="LINEA">{{ detail.line }}</td><td data-label="VENDEDOR">{{ detail.seller }}</td><td data-label="NUMERO_OP">{{ detail.managerOp }}</td></tr></tbody></table></div>
          <WorkspacePagination v-model="sourcePage" :total="source.length" label="Páginas de datos de origen" />
        </details>
        <details v-if="!report.data.preparation?.review" class="rw-editor-disclosure"><summary>Ver referencia histórica</summary><p class="rw-small-note">Esta es la copia del Informe de OPs utilizada al preparar el reporte. Selecciona una OP de referencia para obtener las coincidencias de F y G.</p>
          <ul v-if="linkedHistory.length" class="rw-history-linked"><li v-for="record in linkedHistory" :key="record.id"><strong>OP {{ record.number }}</strong><span>{{ record.data.cells[5] }} · {{ record.data.cells[6] }}</span></li></ul>
          <label class="rw-search"><Search :size="17" /><input v-model="historySearch" aria-label="Buscar una referencia histórica" placeholder="Buscar número, cliente o referencia" @input="historyPage = 1" /></label>
          <p v-if="!historyVisible.length" class="rw-empty">No se encontraron registros en la copia del histórico.</p>
          <div class="rw-history-mode"><button type="button" :disabled="working || restore" :class="['rw-button', historyMode === 'reference' ? 'rw-button-selected' : 'rw-button-subtle']" @click="switchHistory('reference')">Por referencia F/G</button><button type="button" :disabled="working || restore" :class="['rw-button', historyMode === 'individual' ? 'rw-button-selected' : 'rw-button-subtle']" @click="switchHistory('individual')">Elegir OPs</button></div>
          <label v-for="record in historyVisible" :key="record.id" class="rw-history-option"><input v-if="historyMode === 'reference'" type="radio" name="report-history-reference" :checked="historyReferenceId === record.id" :disabled="working || restore" @change="selectReference(record.id)" /><input v-else type="checkbox" :checked="chosenHistoryIds.includes(record.id)" :disabled="working || restore" @change="chooseIndividual(record.id, ($event.target as HTMLInputElement).checked)" /><span><strong>OP {{ record.number }}</strong><small>F · CLIENTE: {{ record.data.cells[5] || 'Vacío' }}</small><small>G · REFERENCIA: {{ record.data.cells[6] || 'Vacío' }}</small></span></label>
          <WorkspacePagination v-model="historyPage" :total="history.length" label="Páginas de referencias históricas" />
        </details>
        <section v-if="previewValid && previewRows.length" class="rw-edit-preview" aria-label="Vista previa del ajuste" tabindex="-1"><h3>Así quedaría la fila</h3><dl v-for="result in previewRows" :key="result.key"><div v-for="field in finalFields" :key="field.key" :class="{ changed: formatFinalValue(result, field.key) !== formatFinalValue(row, field.key) }"><dt>{{ field.label }}</dt><dd>{{ formatFinalValue(result, field.key) || '—' }}</dd></div></dl><p class="rw-small-note">Guardar actualiza el borrador. Si quedan decisiones pendientes, todavía podrás descargar BORRADOR; el archivo final requiere aprobación.</p></section>
        <button type="button" class="rw-button rw-button-subtle rw-restore" :disabled="working" @click="restoreProposal"><RotateCcw :size="16" /> Recuperar propuesta automática</button>
      </div>
      <footer class="rw-editor-footer"><button type="button" class="rw-button rw-button-subtle" :disabled="working" @click="close">Cancelar</button><button type="button" class="rw-button rw-button-subtle" :disabled="working" @click="emit('download')">Descargar borrador</button><button type="button" class="rw-button rw-button-outlined" :disabled="working || !dirty" @click="showPreview"><Eye :size="17" />{{ working ? 'Procesando…' : 'Previsualizar' }}</button><button type="button" class="rw-button rw-button-primary" :disabled="working || !dirty || !previewValid" @click="save"><Save :size="17" /> Guardar cambios</button></footer>
    </dialog>
  </Teleport>
</template>
