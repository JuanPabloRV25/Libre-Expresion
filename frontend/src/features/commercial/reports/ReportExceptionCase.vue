<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref } from 'vue'
import { ChevronLeft, ChevronRight, Eye, Pencil, Save } from '@lucide/vue'
import type { PreparedRowEdit, ReviewAction, ReviewCase, ReviewCommand, ReviewFinding, SalesReport } from './types'
import { reportService } from './reportService'
import { reviewCommand, reviewFieldEditor, reviewFieldPatch } from './reportReviewPresentation'
import { formatFinalValue } from './preparedReport'
import WorkspacePagination from './WorkspacePagination.vue'
const props = defineProps<{ report: SalesReport; item: ReviewCase; index: number; total: number; editable: boolean; findingId?: string; hideNavigation?: boolean }>()
const emit = defineEmits<{ saved: [report: SalesReport]; dirty: [value: boolean]; navigate: [direction: number]; edit: [key: string] }>()
const row = computed(() => props.report.groups.find(value => value.key === props.item.rowKey))
const pending = computed(() => props.item.findings.filter(finding => finding.resolution === 'pending'))
const selectedFindingId = ref(props.findingId ?? pending.value[0]?.id ?? '')
const finding = computed(() => pending.value.find(value => value.id === selectedFindingId.value) ?? pending.value[0])
const fieldEditor = computed(() => row.value && finding.value ? reviewFieldEditor(row.value, finding.value) : null)
const fieldValue = ref(fieldEditor.value?.value ?? ''), fieldEdited = ref(false)
const mode = ref<ReviewAction | null>(null), chosenIds = ref<string[]>([]), manualOp = ref('')
const factura = ref(row.value?.factura ?? ''), dirty = ref(false), working = ref(false), error = ref('')
const editingFactura = ref(false), facturaInput = ref<HTMLInputElement | null>(null), facturaEditButton = ref<HTMLButtonElement | null>(null)
const preview = ref<SalesReport | null>(null), candidatePage = ref(1), sourcePage = ref(1)
const fileSourcePage = ref(1)
const fileSources = computed(() => finding.value?.sourceRows ?? [])
const visibleFileSources = computed(() => fileSources.value.slice((fileSourcePage.value - 1) * 5, fileSourcePage.value * 5))
const candidates = computed(() => [...new Map((finding.value?.evidence ?? []).flatMap(value => value.candidates).map(candidate => [candidate.historyId, candidate])).values()])
const visibleCandidates = computed(() => candidates.value.slice((candidatePage.value - 1) * 5, candidatePage.value * 5))
const records = computed(() => new Map((props.report.data.preparation?.history ?? []).map(record => [record.id, record])))
const sources = computed(() => props.report.data.details.filter(detail => props.item.findings.some(issue => issue.detailIds.includes(detail.id))))
const visibleSources = computed(() => sources.value.slice((sourcePage.value - 1) * 5, sourcePage.value * 5))
const selectedOp = computed(() => mode.value === 'keep_na' ? 'N/A' : mode.value === 'set_manual_op' ? manualOp.value.trim() : mode.value === 'select_candidates' ? candidates.value.filter(candidate => chosenIds.value.includes(candidate.historyId)).map(candidate => candidate.number).join('/') : row.value?.op ?? '')
const previewRow = computed(() => preview.value?.groups.find(value => value.key === row.value?.key))
const previewField = computed(() => previewRow.value && fieldEditor.value ? formatFinalValue(previewRow.value, fieldEditor.value.key) : '')
const hasOpActions = computed(() => !!finding.value?.allowedActions.length)
const saveLabel = computed(() => mode.value ? 'Guardar decisión' : fieldEdited.value ? `Guardar ${fieldEditor.value?.label ?? 'cambio'}` : 'Guardar FACTURA')
function changed() { dirty.value = fieldEdited.value || !!mode.value || factura.value !== row.value?.factura; preview.value = null; error.value = ''; emit('dirty', dirty.value) }
async function editFactura() {
  if (!props.editable || working.value) return
  editingFactura.value = true
  await nextTick()
  facturaInput.value?.focus()
}
async function cancelFactura() {
  if (working.value) return
  factura.value = row.value?.factura ?? ''
  editingFactura.value = false
  changed()
  await nextTick()
  facturaEditButton.value?.focus()
}
function editField() { if (fieldEditor.value?.readOnly && !fieldEditor.value.confirmUnchanged) return; fieldEdited.value = true; changed() }
function inputField(event: Event) { fieldValue.value = (event.target as HTMLInputElement).value; editField() }
function selectMode(action: ReviewAction) { mode.value = action; changed() }
function choose(id: string, checked: boolean) { chosenIds.value = checked ? [...new Set([...chosenIds.value, id])] : chosenIds.value.filter(value => value !== id); changed() }
function request(): { edits: PreparedRowEdit[]; decisions: ReviewCommand[] } {
  const edits: PreparedRowEdit[] = []
  if (row.value) {
    const edit = fieldEdited.value && finding.value ? reviewFieldPatch(row.value, finding.value, fieldValue.value) : { key: row.value.key }
    if (factura.value !== row.value.factura) edit.factura = factura.value
    if (Object.keys(edit).length > 1) edits.push(edit)
  }
  const commands = mode.value && finding.value ? [reviewCommand(props.item, [finding.value.id], mode.value, chosenIds.value, manualOp.value)] : []
  return { edits, decisions: commands }
}
async function showPreview() {
  if (!props.editable || working.value || !dirty.value) return
  working.value = true; error.value = ''
  try { const data = request(); preview.value = await reportService.previewPrepared(props.report, data.edits, data.decisions) }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible previsualizar. Tus datos siguen aquí.' }
  finally { working.value = false }
}
async function save() {
  if (!props.editable || working.value || !dirty.value) return !dirty.value
  working.value = true; error.value = ''
  try { const data = request(); const updated = await reportService.savePrepared(props.report, data.edits, data.decisions); factura.value = updated.groups.find(value => value.key === row.value?.key)?.factura ?? factura.value; editingFactura.value = false; dirty.value = false; emit('dirty', false); emit('saved', updated); return true }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible guardar. Tus datos siguen aquí.'; return false }
  finally { working.value = false }
}
function reset() { mode.value = null; chosenIds.value = []; manualOp.value = ''; factura.value = row.value?.factura ?? ''; editingFactura.value = false; fieldValue.value = fieldEditor.value?.value ?? ''; fieldEdited.value = false; preview.value = null; error.value = ''; dirty.value = false; emit('dirty', false) }
function switchFinding(value: ReviewFinding) { if (!dirty.value) { selectedFindingId.value = value.id; reset(); candidatePage.value = 1; fileSourcePage.value = 1; sourcePage.value = 1 } }
onUnmounted(() => emit('dirty', false))
defineExpose({ save })
</script>
<template>
  <section class="rw-exception-case" aria-labelledby="rw-case-title">
    <header class="rw-case-heading"><div><p class="rw-eyebrow">INCIDENCIA {{ index + 1 }} DE {{ total }}</p><h2 id="rw-case-title">{{ row ? `Venta ${row.number || 'sin número'}` : 'Incidencia del archivo' }}</h2><p v-if="row">{{ row.client || 'Cliente sin informar' }} · {{ row.line || 'Línea sin informar' }}</p></div><span class="rw-case-status">{{ finding?.initialClassification === 'conflict' ? 'Conflicto' : 'Requiere validación' }}</span></header>
    <div class="rw-case-body">
      <p v-if="error" class="rw-error" role="alert">{{ error }} No se descartó lo que escribiste ni tu selección.</p>
      <nav v-if="pending.length > 1 && !findingId" class="rw-case-findings" aria-label="Incidencias de este registro"><button v-for="issue in pending" :key="issue.id" type="button" :aria-pressed="finding?.id === issue.id" :class="['rw-button', finding?.id === issue.id ? 'rw-button-selected' : 'rw-button-outlined']" :disabled="working || dirty" @click="switchFinding(issue)">{{ issue.field }}</button></nav>
      <div v-if="finding" class="rw-case-problem"><p class="rw-eyebrow">INCIDENCIA EN {{ finding.field }}</p><p>{{ finding.reason }}</p></div>
      <section v-if="item.scope === 'file' && fileSources.length" class="rw-file-incidence-evidence" aria-label="Filas originales de la incidencia"><article v-for="source in visibleFileSources" :key="`${source.sourceFile}:${source.sheet}:${source.sourceRow}`"><header><h3>Fila {{ source.sourceRow }}</h3><p>{{ source.sourceFile }}</p><span>Hoja: {{ source.sheet }}</span></header><dl><div v-for="cell in source.cells" :key="cell.column"><dt>{{ cell.header || `Columna ${cell.column}` }}<small>Columna {{ cell.column }}</small></dt><dd>{{ cell.value || 'Vacío' }}</dd></div></dl></article><WorkspacePagination v-model="fileSourcePage" :total="fileSources.length" label="Páginas de filas del archivo con incidencias" /></section>
      <dl v-if="row && hasOpActions" class="rw-case-record"><div><dt>NUMERO OP actual</dt><dd>{{ row.op || 'N/A' }}</dd></div><div><dt>LINEA</dt><dd>{{ row.line || 'Sin informar' }}</dd></div><div class="rw-case-detail"><dt>DETALLE</dt><dd>{{ row.details.join('\n') }}</dd></div></dl>
      <label v-if="fieldEditor" class="rw-case-input rw-case-inline-field"><span>{{ fieldEditor.label }}<small v-if="fieldEditor.modeLabel">{{ fieldEditor.modeLabel }}</small></span><textarea v-if="fieldEditor.input === 'textarea'" v-model="fieldValue" :aria-label="`Corregir ${fieldEditor.label}`" :disabled="working || !editable" rows="5" @input="editField" /><input v-else :value="fieldValue" :aria-label="`${fieldEditor.readOnly ? 'Consultar' : 'Corregir'} ${fieldEditor.label}`" :aria-describedby="fieldEditor.help ? 'rw-case-amount-help' : undefined" :type="fieldEditor.input" :step="fieldEditor.input === 'number' ? '0.01' : undefined" :inputmode="fieldEditor.input === 'number' ? 'decimal' : undefined" :readonly="fieldEditor.readOnly" :placeholder="fieldEditor.key === 'amount' && fieldEditor.modeLabel ? (fieldEditor.readOnly ? 'Sin determinar' : 'Escribe el total') : undefined" :disabled="working || !editable" @input="inputField" /><small v-if="fieldEditor.help" id="rw-case-amount-help" class="rw-amount-help">{{ fieldEditor.help }}</small><button v-if="editable && fieldEditor.key === 'amount' && fieldValue !== '' && !fieldEdited && (!fieldEditor.readOnly || fieldEditor.confirmUnchanged)" type="button" class="rw-button rw-button-outlined" :disabled="working" @click="editField">Mantener este importe</button></label>
      <p v-if="finding && !row" class="rw-small-note">La incidencia está en el archivo de origen, no en una celda del informe. Revisa ese archivo para corregirla.</p>
      <p v-else-if="finding && !fieldEditor && !hasOpActions" class="rw-small-note">Esta incidencia no tiene una corrección de celda disponible. Se conserva pendiente para su revisión.</p>
      <div v-if="editable && finding && hasOpActions" class="rw-case-choice"><h3>Corregir NUMERO OP</h3><div class="rw-case-action-options"><button v-if="finding.allowedActions.includes('select_candidates') && candidates.length" type="button" :class="['rw-button', mode === 'select_candidates' ? 'rw-button-selected' : 'rw-button-outlined']" :disabled="working" @click="selectMode('select_candidates')">Elegir OPs</button><button v-if="finding.allowedActions.includes('set_manual_op')" type="button" :class="['rw-button', mode === 'set_manual_op' ? 'rw-button-selected' : 'rw-button-outlined']" :disabled="working" @click="selectMode('set_manual_op')">Escribir OP</button><button v-if="finding.allowedActions.includes('keep_na')" type="button" :class="['rw-button', mode === 'keep_na' ? 'rw-button-selected' : 'rw-button-outlined']" :disabled="working" @click="selectMode('keep_na')">Mantener N/A</button></div>
        <p v-if="finding.allowedActions.includes('select_candidates') && !candidates.length" class="rw-small-note">No hay OPs compatibles disponibles para elegir. Puedes escribir la OP o mantener N/A.</p>
        <div v-if="mode === 'select_candidates'" class="rw-case-candidates"><p>Elige una o varias OP que correspondan a esta venta.</p><label v-for="candidate in visibleCandidates" :key="candidate.historyId" class="rw-history-option"><input type="checkbox" :checked="chosenIds.includes(candidate.historyId)" :disabled="working" @change="choose(candidate.historyId, ($event.target as HTMLInputElement).checked)" /><span><strong>OP {{ candidate.number }}</strong><small>{{ records.get(candidate.historyId)?.data.cells[5] || 'Cliente no informado' }}</small><small>Referencia: {{ records.get(candidate.historyId)?.data.cells[6] || 'Sin informar' }}</small><small>Producto: {{ records.get(candidate.historyId)?.data.cells[9] || 'Sin informar' }}</small><small>Coincide en {{ candidate.matchedFields.join(' y ') }}</small></span></label><WorkspacePagination v-model="candidatePage" :total="candidates.length" label="Páginas de OPs compatibles" /></div>
        <label v-if="mode === 'set_manual_op'" class="rw-case-input"><span>Número OP correcto</span><input v-model="manualOp" type="text" :disabled="working" placeholder="Escribe la OP" @input="changed" /></label>
        <p v-if="mode === 'keep_na'" class="rw-small-note">Confirmarás que esta venta se mantiene como N/A. Quedará registrada como una decisión de la auxiliar.</p>
        <div v-if="mode" class="rw-case-cell-preview"><span>Así quedaría NUMERO OP</span><strong>{{ selectedOp || 'Elige o escribe una OP' }}</strong></div>
      </div>
      <div v-if="row" class="rw-case-input rw-case-factura"><span>FACTURA</span><div v-if="editingFactura && editable" class="rw-inline-factura"><input ref="facturaInput" v-model="factura" type="text" :disabled="working" aria-label="FACTURA del caso actual" placeholder="Escribir" @input="changed" @keydown.esc.prevent="cancelFactura" /><button type="button" class="rw-button rw-button-subtle" :disabled="working" aria-label="Cancelar edición de FACTURA del caso actual" @click="cancelFactura">Cancelar edición</button></div><div v-else class="rw-factura-display"><span class="rw-cell-value" :class="{ 'rw-cell-empty': !row.factura }">{{ row.factura || '—' }}</span><button v-if="editable" ref="facturaEditButton" type="button" class="rw-button rw-button-subtle" :disabled="working" aria-label="Editar FACTURA del caso actual" @click="editFactura"><Pencil :size="14" aria-hidden="true" />Editar</button></div></div>
      <div v-if="preview && previewRow" class="rw-case-cell-preview rw-case-preview" role="status"><span>Vista previa · sin guardar</span><strong v-if="fieldEditor">{{ fieldEditor.label }}: {{ previewField || 'Sin completar' }}</strong><strong v-else>NUMERO OP: {{ previewRow.op || 'N/A' }}</strong><span v-if="factura !== row?.factura">FACTURA: {{ previewRow.factura || 'Sin completar' }}</span></div>
      <div v-if="editable && row" class="rw-case-save"><button type="button" class="rw-button rw-button-primary" :disabled="working || !dirty" @click="save"><Save :size="17" />{{ working ? 'Procesando…' : saveLabel }}</button><button type="button" class="rw-button rw-button-outlined" :disabled="working || !dirty" @click="showPreview"><Eye :size="17" />Previsualizar</button><button v-if="dirty" type="button" class="rw-button rw-button-subtle" :disabled="working" @click="reset">Descartar cambios locales</button></div>
      <details v-if="row" class="rw-case-evidence"><summary>Ver datos de origen y coincidencias</summary><div v-if="finding" class="rw-case-match-evidence"><article v-for="evidence in finding.evidence" :key="evidence.detailId"><strong>Producto usado para el cruce</strong><p>{{ evidence.segment || 'No informado' }}</p><small>{{ evidence.candidates.length }} {{ evidence.candidates.length === 1 ? 'OP compatible' : 'OPs compatibles' }} · {{ evidence.candidates.map(candidate => candidate.number).join(' / ') || 'Sin coincidencias' }}</small></article></div><article v-for="source in visibleSources" :key="source.id" class="rw-case-source"><h4>Informe de ventas · fila {{ source.sourceRow }}</h4><dl><div><dt>NUMERO_OP de origen</dt><dd>{{ source.managerOp || 'No informado' }}</dd></div><div><dt>NUMERO</dt><dd>{{ source.number }}</dd></div><div><dt>FECHA</dt><dd>{{ source.date }}</dd></div><div><dt>NOMBRE</dt><dd>{{ source.client }}</dd></div><div><dt>PLAZO</dt><dd>{{ source.term }}</dd></div><div><dt>VALOR_BRUT</dt><dd>{{ source.rawAmount }}</dd></div><div><dt>LINEA</dt><dd>{{ source.line }}</dd></div><div><dt>VENDEDOR</dt><dd>{{ source.seller }}</dd></div><div class="rw-case-detail"><dt>DETALLE</dt><dd>{{ source.detail }}</dd></div></dl></article><WorkspacePagination v-model="sourcePage" :total="sources.length" label="Páginas de datos de este caso" /></details>
    </div>
    <footer v-if="!hideNavigation" class="rw-case-navigation"><button type="button" class="rw-button rw-button-outlined" :disabled="index === 0 || working || dirty" @click="emit('navigate', -1)"><ChevronLeft :size="17" />Anterior</button><span>{{ index + 1 }} de {{ total }} incidencias</span><button type="button" class="rw-button rw-button-outlined" :disabled="index >= total - 1 || working || dirty" @click="emit('navigate', 1)">Siguiente<ChevronRight :size="17" /></button></footer>
  </section>
</template>
<style scoped>
.rw-case-inline-field { margin-top: 0; max-width: 100%; }
.rw-case-inline-field textarea { width: 100%; min-width: 0; padding: 11px 12px; border: 1px solid var(--line); border-radius: 8px; background: var(--surface); color: var(--charcoal); font: inherit; line-height: 1.6; resize: vertical; }
.rw-case-inline-field .rw-button { justify-self: start; }
.rw-amount-help { color: var(--muted); line-height: 1.6; font-size: .82rem; }
.rw-case-preview { margin-top: 0; white-space: pre-wrap; }
.rw-file-incidence-evidence { min-width: 0; display: grid; gap: 16px; }
.rw-file-incidence-evidence article { min-width: 0; border: 1px solid var(--line); border-radius: 10px; overflow: hidden; }
.rw-file-incidence-evidence header { padding: 16px; background: var(--rw-soft); border-bottom: 1px solid var(--line); }
.rw-file-incidence-evidence header h3 { margin: 0 0 7px; font-size: 1rem; }
.rw-file-incidence-evidence header p { margin: 0 0 5px; font-size: .88rem; line-height: 1.5; overflow-wrap: anywhere; }
.rw-file-incidence-evidence header span { color: var(--muted); font-size: .82rem; }
.rw-file-incidence-evidence dl { margin: 0; padding: 16px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; }
.rw-file-incidence-evidence dl > div { min-width: 0; }
.rw-file-incidence-evidence dt { color: var(--muted); font-size: .79rem; font-weight: 650; }
.rw-file-incidence-evidence dt small { display: block; margin-top: 4px; font-size: .71rem; font-weight: 400; }
.rw-file-incidence-evidence dd { margin: 8px 0 0; color: var(--charcoal); font-size: .9rem; line-height: 1.6; white-space: pre-wrap; overflow-wrap: anywhere; }
@media (max-width: 450px) { .rw-file-incidence-evidence dl { grid-template-columns: minmax(0, 1fr); } }
</style>
