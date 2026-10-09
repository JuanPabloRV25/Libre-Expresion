<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ArrowLeft, Download, FileSpreadsheet, Pencil } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { useAuthStore } from '../../../stores/auth'
import { reportService } from './reportService'
import ReportResultTable from './ReportResultTable.vue'
import ReportRowEditor from './ReportRowEditor.vue'
import ReportReviewSummary from './ReportReviewSummary.vue'
import ReportExceptionCase from './ReportExceptionCase.vue'
import ReportIncidenceList from './ReportIncidenceList.vue'
import ReportSourceIdentity from './ReportSourceIdentity.vue'
import { pendingReviewCases } from './reportReviewPresentation'
import { preparedReportTotal } from './preparedReport'
import type { ReviewCase, ReviewFinding, SalesGroup, SalesReport } from './types'
import './workspace.css'
const props = defineProps<{ initialReport: SalesReport }>()
const auth = useAuthStore(), report = ref<SalesReport>(props.initialReport), error = ref(''), message = ref('')
const editingEnabled = ref(false), editingRow = ref<SalesGroup | null>(null), downloading = ref(false), approving = ref(false), savingFactura = ref('')
const editorDirty = ref(false), caseDirty = ref(false), tableDirty = ref(false)
const result = ref<HTMLElement | null>(null), reviewRegion = ref<HTMLElement | null>(null)
const downloadChoice = ref<HTMLDialogElement | null>(null), downloadKind = ref<'draft' | 'final'>('draft')
const showReview = ref(false), selectedFindingId = ref(''), fullDraft = ref(false)
const preparation = computed(() => report.value.data.preparation!)
const review = computed(() => preparation.value.review)
const pending = computed(() => review.value ? pendingReviewCases(review.value) : [])
const incidences = computed(() => pending.value.flatMap(item => item.findings.filter(finding => finding.resolution === 'pending').map(finding => ({ item, finding }))))
const activeIncidence = computed(() => incidences.value.find(value => value.finding.id === selectedFindingId.value) ?? incidences.value[0])
const incidenceIndex = computed(() => incidences.value.findIndex(value => value.finding.id === activeIncidence.value?.finding.id))
const canEdit = computed(() => auth.hasPermission('commercial.reports.edit'))
const canExport = computed(() => auth.hasPermission('commercial.reports.export'))
const dirty = computed(() => editorDirty.value || caseDirty.value || tableDirty.value)
const busy = computed(() => downloading.value || approving.value || !!savingFactura.value)
const total = computed(() => preparedReportTotal(report.value.groups, preparation.value.changes))
const totalDisplay = computed(() => total.value === null ? 'Sin determinar' : new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(total.value))
function saved(updated: SalesReport) {
  // Exact file evidence recovered by GET for older snapshots remains applicable
  // after a cell edit only while the immutable source and finding are unchanged.
  if (updated.data.currentSourceId === report.value.data.currentSourceId && updated.data.sha256 === report.value.data.sha256) {
    const previous = report.value.data.preparation?.review?.cases.flatMap(item => item.findings) ?? []
    for (const item of updated.data.preparation?.review?.cases ?? []) {
      if (item.scope !== 'file') continue
      for (const finding of item.findings) {
        const evidence = previous.find(value => value.id === finding.id && value.reason === finding.reason)?.sourceRows
        if (!finding.sourceRows?.length && evidence?.length) finding.sourceRows = evidence.map(row => ({
          ...row, cells: row.cells.map(cell => ({ ...cell })),
        }))
      }
    }
  }
  report.value = updated; editingRow.value = null; editorDirty.value = false; caseDirty.value = false; error.value = ''
  selectedFindingId.value = activeIncidence.value?.finding.id ?? ''
  message.value = 'Cambios guardados. Los pendientes y las descargas reflejan esta versión.'
  if (review.value && !pending.value.length) { showReview.value = false; message.value = 'Sin excepciones pendientes. Puedes aprobar el contenido guardado.' }
}
async function openReview() { showReview.value = true; await nextTick(); reviewRegion.value?.focus(); reviewRegion.value?.scrollIntoView({ block: 'start', behavior: 'smooth' }) }
async function selectIncidence(value: { item: ReviewCase; finding: ReviewFinding }) {
  if (dirty.value || busy.value) return
  selectedFindingId.value = value.finding.id
  await nextTick()
  const target = document.getElementById('rw-active-incidence')
  target?.focus()
  target?.scrollIntoView({ block: 'start', behavior: 'smooth' })
}
async function openComplete() { fullDraft.value = true; await nextTick(); result.value?.scrollIntoView({ block: 'start', behavior: 'smooth' }) }
function editCase(key: string) { editingRow.value = report.value.groups.find(row => row.key === key) ?? null }
async function saveFactura(value: { key: string; factura: string }) {
  if (!canEdit.value || busy.value || caseDirty.value || editorDirty.value) return
  savingFactura.value = value.key; error.value = ''
  try { saved(await reportService.savePrepared(report.value, [value])) }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible guardar FACTURA. El texto sigue disponible.' }
  finally { savingFactura.value = '' }
}
async function approve() {
  if (dirty.value || !report.value.canApprove || !canEdit.value) return
  approving.value = true; error.value = ''
  try { report.value = await reportService.approve(report.value); message.value = 'Contenido aprobado. Ya puedes descargar VENTAS MES.' }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible aprobar. Revisa los pendientes de esta versión.' }
  finally { approving.value = false }
}
async function exportReport(kind: 'draft' | 'final') {
  downloading.value = true; error.value = ''; downloadChoice.value?.close()
  try {
    const blob = await (kind === 'draft' ? reportService.exportDraft(report.value) : reportService.export(report.value))
    const url = URL.createObjectURL(blob), link = document.createElement('a')
    link.href = url; link.download = kind === 'draft' ? `BORRADOR-VENTAS-MES-${report.value.id}.xlsx` : `VENTAS-MES-${report.value.id}.xlsx`; link.click()
    setTimeout(() => URL.revokeObjectURL(url), 1500)
    message.value = kind === 'draft' ? 'Borrador descargado con los datos guardados. El archivo está identificado como BORRADOR.' : 'VENTAS MES aprobado descargado con los datos de esta versión.'
  } catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible descargar. Puedes volver a intentarlo.' }
  finally { downloading.value = false }
}
async function download(kind: 'draft' | 'final') {
  downloadKind.value = kind
  if (dirty.value) { await nextTick(); downloadChoice.value?.showModal() } else await exportReport(kind)
}
function closeEditor() { editingRow.value = null; editorDirty.value = false }
function protect(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
onBeforeRouteLeave(() => !dirty.value || window.confirm('Hay cambios sin guardar. ¿Quieres salir y descartarlos?'))
onMounted(() => window.addEventListener('beforeunload', protect))
onUnmounted(() => window.removeEventListener('beforeunload', protect))
</script>
<template>
  <AppLayout>
    <div class="rw-workspace">
      <PageHeader eyebrow="COMERCIAL · REPORTES · INFORME MENSUAL" title="INFORME DE VENTAS MENSUAL"><RouterLink class="rw-button rw-button-outlined" to="/commercial/reports/monthly"><ArrowLeft :size="17" /> Informe Mensual</RouterLink></PageHeader>
      <p v-if="error" class="rw-error" role="alert">{{ error }}</p><p v-if="message" class="rw-success" role="status">{{ message }}</p>
      <ReportReviewSummary v-if="review" :report="report" :editable="canEdit" :exportable="canExport" :busy="busy" :dirty="dirty" @resolve="openReview" @complete="openComplete" @draft="download('draft')" @approve="approve" @final="download('final')" />
      <section v-else class="rw-review-summary rw-previous-version"><header><div><p class="rw-eyebrow">BORRADOR · VERSIÓN ANTERIOR</p><h2>INFORME DE VENTAS MENSUAL</h2><ReportSourceIdentity :source-file="report.data.sourceFile" :version="report.version" /></div><button v-if="canExport" type="button" class="rw-button rw-button-outlined" :disabled="busy || !report.canExportDraft" @click="download('draft')"><Download :size="18" />Descargar borrador</button></header><p class="rw-review-guidance">Este reporte conserva los datos y ediciones de su preparación anterior. Todavía no tiene una aprobación del nuevo flujo.</p></section>
      <div v-if="showReview && activeIncidence" ref="reviewRegion" class="rw-review-region rw-incidence-layout" tabindex="-1"><ReportIncidenceList :report="report" :selected-id="activeIncidence.finding.id" :locked="dirty || busy" @select="selectIncidence" /><div id="rw-active-incidence" class="rw-active-incidence" tabindex="-1"><ReportExceptionCase :key="`${activeIncidence.item.id}:${activeIncidence.finding.id}:${report.version}`" :report="report" :item="activeIncidence.item" :finding-id="activeIncidence.finding.id" :index="incidenceIndex" :total="incidences.length" hide-navigation :editable="canEdit && !tableDirty && !editorDirty" @dirty="caseDirty = $event" @saved="saved" @edit="editCase" /></div></div>
      <details ref="result" class="rw-optional-panel rw-complete-draft" :open="fullDraft || !review" @toggle="fullDraft = ($event.target as HTMLDetailsElement).open"><summary><FileSpreadsheet :size="18" /><span>Informe completo</span><small>Consulta y correcciones opcionales</small></summary>
        <section v-if="fullDraft || !review || tableDirty" class="rw-result-surface" aria-labelledby="rw-result-title"><header class="rw-result-heading"><div class="rw-result-identity"><span class="rw-file-icon"><FileSpreadsheet :size="24" /></span><div><h2 id="rw-result-title">INFORME DE VENTAS MENSUAL</h2><ReportSourceIdentity :source-file="report.data.sourceFile" :version="report.version" /><span>{{ report.groups.length }} registros</span></div></div><button v-if="canEdit" type="button" :class="['rw-button', editingEnabled ? 'rw-button-selected' : 'rw-button-subtle']" :aria-pressed="editingEnabled" :disabled="busy || caseDirty || tableDirty" @click="editingEnabled = !editingEnabled"><Pencil :size="16" />{{ editingEnabled ? 'Terminar edición' : 'Editar otros campos' }}</button></header><p class="rw-edit-hint">Para cambiar FACTURA, pulsa Editar en su campo. Las demás correcciones son opcionales.</p><div class="rw-worksheet-heading"><div><h3>VENTAS</h3><p>Diez columnas del archivo VENTAS MES</p></div><div class="rw-worksheet-total" aria-label="Total de VALOR_BRUT"><span>TOTAL · VALOR_BRUT</span><strong>{{ totalDisplay }}</strong><small v-if="total === null">Hay importes por completar o confirmar.</small></div></div><ReportResultTable :rows="report.groups" :changes="preparation.changes" :review="review" :editable="canEdit && editingEnabled && !caseDirty && !tableDirty" :factura-editable="canEdit && !caseDirty && !editorDirty && !downloading && !approving" :saving-factura="savingFactura" @dirty="tableDirty = $event" @save-factura="saveFactura" @edit="editingRow = $event" /></section>
      </details>
      <ReportRowEditor v-if="editingRow && canEdit" :key="editingRow.key" :report="report" :row="editingRow" @dirty="editorDirty = $event" @close="closeEditor" @saved="saved" @download="download('draft')" />
      <dialog ref="downloadChoice" class="rw-download-dialog" aria-labelledby="rw-download-choice"><h2 id="rw-download-choice">Hay cambios sin guardar</h2><p>La descarga contiene la versión guardada. Lo que estás escribiendo seguirá aquí.</p><div><button type="button" class="rw-button rw-button-outlined" @click="exportReport(downloadKind)">Descargar versión guardada</button><button type="button" class="rw-button rw-button-subtle" @click="downloadChoice?.close()">Volver y guardar mis cambios</button></div></dialog>
    </div>
  </AppLayout>
</template>
<style scoped>
.rw-incidence-layout { display: grid; grid-template-columns: minmax(290px, 340px) minmax(0, 1fr); gap: 20px; align-items: start; }
.rw-active-incidence { min-width: 0; scroll-margin-top: 20px; }
@media (max-width: 1100px) { .rw-incidence-layout { grid-template-columns: minmax(0, 1fr); } }
</style>
