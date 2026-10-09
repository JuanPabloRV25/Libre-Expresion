<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, onBeforeRouteLeave } from 'vue-router'
import { Download, Save, Search, ArrowLeft, ArrowRight, FileSpreadsheet, ClipboardList, CircleCheck, CircleAlert } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { useAuthStore } from '../../../stores/auth'
import { reportService, money, inputMoney } from './reportService'
import ReportPagination from './ReportPagination.vue'
import ReportRecordEditor from './ReportRecordEditor.vue'
import type { GroupEdit, OpRecord, ReplacementPreview, SalesDetail, SalesGroup, SalesReport } from './types'
import './reports.css'
const auth = useAuthStore(); const route = useRoute()
const report = ref<SalesReport | null>(null); const ops = ref<OpRecord[]>([])
const busy = ref(false); const loading = ref(true); const dirty = ref(false); const error = ref(''); const message = ref('')
const step = ref(2); const search = ref(''); const filter = ref('all'); const page = ref(1)
const pageSize = 5
const reviewTab = ref<'records' | 'amounts'>('records'); const reviewTabs = ref<HTMLElement | null>(null)
const amountSearch = ref(''); const amountFilter = ref('all'); const amountPage = ref(1)
const amountId = ref<string | null>(null); const amountPanel = ref<HTMLElement | null>(null)
const editingKey = ref<string | null>(null); const panel = ref<HTMLElement | null>(null)
const codeChoices = ref<Record<string, boolean>>({})
const savedStructure = ref<Record<string, string>>({})
const editorTask = ref<'op' | 'excel' | undefined>(); const editorEpoch = ref(0)
const resultKeys = ref<string[]>([]); const resultExcluded = ref(false); const resultPanel = ref<HTMLElement | null>(null)
const resultPage = ref(1)
const continuationIds = ref<string[]>([])
const replacement = ref<ReplacementPreview | null>(null); const replacementFile = ref<File | null>(null)
const restoreTo = ref('')
const downloadTab = ref<'records' | 'amounts'>('records'); const downloadSearch = ref(''); const downloadPage = ref(1)
const downloadOverview = ref<HTMLElement | null>(null); const downloadFailure = ref(false)
const canEdit = computed(() => auth.hasPermission('commercial.reports.edit'))
const canDownload = computed(() => auth.hasPermission('commercial.reports.export'))
const needsReview = (g: SalesGroup) => g.issues.length > 0
const filtered = computed(() => (report.value?.groups ?? []).filter(g =>
  (filter.value !== 'review' || needsReview(g)) && (filter.value !== 'changed' || g.modified) &&
  `${g.op} ${g.client} ${g.product} ${g.line} ${g.number}`.toLowerCase().includes(search.value.toLowerCase())))
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize)))
const visible = computed(() => filtered.value.slice((page.value - 1) * pageSize, page.value * pageSize))
const active = computed(() => report.value?.groups.find(g => g.key === editingKey.value) ?? null)
const activeDetails = computed(() => report.value?.data.details.filter(d => active.value?.detailIds.includes(d.id)) ?? [])
const edit = computed(() => report.value?.data.edits.find(e => e.key === editingKey.value) ?? null)
function structure(detail: SalesDetail) {
  return JSON.stringify([detail.selectedOpId, detail.selectedProduct, detail.usePortalCode, detail.excluded, detail.manualGroup])
}
const regroup = computed(() => report.value?.data.details.some(detail => savedStructure.value[detail.id] !== structure(detail)) ?? false)
const resultGroups = computed(() => report.value?.groups.filter(group => resultKeys.value.includes(group.key)) ?? [])
const visibleResultGroups = computed(() => resultGroups.value.slice((resultPage.value - 1) * pageSize, resultPage.value * pageSize))
const unresolvedControls = computed(() => report.value?.controls.filter(c => c.expected === null || c.difference !== 0) ?? [])
const controlsById = computed(() => new Map(report.value?.controls.map(c => [c.id, c]) ?? []))
const amountPending = (id: string) => { const control = controlsById.value.get(id); return !!control && (control.expected === null || control.difference !== 0) }
const amountDocuments = computed(() => (report.value?.data.documents ?? []).filter(doc => controlsById.value.has(doc.id) &&
  (amountFilter.value !== 'pending' || amountPending(doc.id)) && (amountFilter.value !== 'checked' || !amountPending(doc.id)) &&
  `${doc.number} ${doc.client}`.toLowerCase().includes(amountSearch.value.toLowerCase())))
const amountPages = computed(() => Math.max(1, Math.ceil(amountDocuments.value.length / pageSize)))
const visibleAmounts = computed(() => amountDocuments.value.slice((amountPage.value - 1) * pageSize, amountPage.value * pageSize))
const activeAmount = computed(() => report.value?.data.documents.find(d => d.id === amountId.value && controlsById.value.has(d.id)) ?? null)
const pendingGroups = computed(() => report.value?.groups.filter(needsReview) ?? [])
const includedDetails = computed(() => report.value?.data.details.filter(d => !d.excluded).length ?? 0)
const pendingTotal = computed(() => pendingGroups.value.length + unresolvedControls.value.length)
const downloadReady = computed(() => !!report.value?.canExportDraft && !dirty.value)
const downloadState = computed(() => {
  if (dirty.value) return 'dirty'
  if (downloadFailure.value) return 'failed'
  if (!report.value?.groups.length) return 'empty'
  if (pendingTotal.value) return 'pending'
  return downloadReady.value ? 'ready' : 'blocked'
})
const downloadTitle = computed(() => ({ dirty: 'Guarda los cambios antes de descargar', failed: 'No se pudo descargar el Excel',
  empty: 'No hay registros para descargar', pending: 'Borrador con pendientes', ready: 'Borrador disponible',
  blocked: 'Falta completar la revisión' })[downloadState.value])
const downloadDescription = computed(() => ({
  dirty: 'Guarda los cambios para actualizar la revisión y comprobar si el Excel está listo.',
  failed: 'Revisa el mensaje de error antes de intentarlo de nuevo. Puedes recargar el reporte si su información cambió.',
  empty: 'El reporte necesita al menos un registro incluido. Vuelve a revisar los datos y las exclusiones.',
  pending: 'Puedes descargar una copia BORRADOR de esta versión anterior. Los pendientes se conservan.',
  ready: 'Esta preparación anterior se puede descargar como BORRADOR. No tiene una aprobación del nuevo flujo.',
  blocked: 'La revisión guardada aún no permite descargar este Excel. Revisa los datos y los motivos de los detalles excluidos.'
})[downloadState.value])
type DownloadPending = { key: string; number: string; client: string; description: string; issues: string[]; group?: SalesGroup; controlId?: string }
const downloadItems = computed<DownloadPending[]>(() => downloadTab.value === 'records'
  ? pendingGroups.value.map(g => ({ key: g.key, number: g.number, client: g.client,
    description: `${g.op ? 'OP ' + g.op : 'OP por confirmar'} · ${g.product || g.line || 'Producto por confirmar'}`, issues: g.issues, group: g }))
  : unresolvedControls.value.map(c => ({ key: c.id, number: c.number, client: c.client,
    description: c.expected === null ? 'Total por confirmar' : `Confirmado: ${money(c.expected)} · Distribuido: ${money(c.distributed)}`,
    issues: c.expected === null ? ['Falta confirmar el total de este registro.']
      : ['El total confirmado no coincide con el importe distribuido.', ...(c.difference === null ? [] : [`Diferencia: ${money(c.difference)}`])], controlId: c.id })))
const filteredDownloadItems = computed(() => downloadItems.value.filter(item =>
  `${item.number} ${item.client} ${item.description} ${item.issues.join(' ')}`.toLowerCase().includes(downloadSearch.value.toLowerCase())))
const downloadPages = computed(() => Math.max(1, Math.ceil(filteredDownloadItems.value.length / pageSize)))
const visibleDownloadItems = computed(() => filteredDownloadItems.value.slice((downloadPage.value - 1) * pageSize, downloadPage.value * pageSize))
watch(pages, count => { page.value = Math.min(page.value, count) })
watch(amountPages, count => { amountPage.value = Math.min(amountPage.value, count) })
watch(page, () => { editingKey.value = null })
watch(amountPage, () => { amountId.value = null })
watch([search, filter], () => { page.value = 1; editingKey.value = null })
watch([amountSearch, amountFilter], () => { amountPage.value = 1; amountId.value = null })
watch([downloadSearch, downloadTab], () => { downloadPage.value = 1 })
watch(downloadPages, count => { downloadPage.value = Math.min(downloadPage.value, count) })
function changed() { dirty.value = true; message.value = ''; downloadFailure.value = false }
function setReport(value: SalesReport) {
  report.value = value; savedStructure.value = Object.fromEntries(value.data.details.map(detail => [detail.id, structure(detail)]))
  dirty.value = false; codeChoices.value = {}; editingKey.value = null; amountId.value = null; downloadFailure.value = false
  resultKeys.value = []; resultExcluded.value = false; resultPage.value = 1; continuationIds.value = []
}
async function showDownload() {
  if (!pendingGroups.value.length && unresolvedControls.value.length) downloadTab.value = 'amounts'
  step.value = 3
  await nextTick(); downloadOverview.value?.focus(); downloadOverview.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
async function backToReview() {
  step.value = 2
  await nextTick(); reviewTabs.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
async function reviewDownloadPending(item: DownloadPending) {
  step.value = 2
  if (item.group) {
    reviewTab.value = 'records'; search.value = ''; filter.value = 'all'
    await nextTick()
    page.value = Math.floor(Math.max(0, filtered.value.findIndex(g => g.key === item.key)) / pageSize) + 1
    await nextTick(); await open(item.group)
  } else if (item.controlId) {
    reviewTab.value = 'amounts'; amountSearch.value = ''; amountFilter.value = 'all'
    await nextTick()
    amountPage.value = Math.floor(Math.max(0, amountDocuments.value.findIndex(d => d.id === item.controlId)) / pageSize) + 1
    await nextTick(); await openAmount(item.controlId)
  }
}
async function selectReviewTab(tab: 'records' | 'amounts') {
  reviewTab.value = tab
  await nextTick(); reviewTabs.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
async function openAmount(id: string) {
  amountId.value = id
  await nextTick(); amountPanel.value?.focus(); amountPanel.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
async function load() {
  loading.value = true; error.value = ''
  try { const [data, records] = await Promise.all([reportService.get(String(route.params.id)), reportService.ops()]); setReport(data); ops.value = records }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible abrir el reporte.' }
  finally { loading.value = false }
}
async function open(group: SalesGroup, task?: 'op' | 'excel') {
  if (!resultKeys.value.includes(group.key)) { resultKeys.value = []; continuationIds.value = [] }
  editingKey.value = group.key; editorTask.value = task; editorEpoch.value++; resultExcluded.value = false
  if (report.value && canEdit.value && !report.value.data.edits.some(e => e.key === group.key))
    report.value.data.edits.push({ key: group.key, factura: group.factura, amount: group.amount, client: null, line: null, seller: null, reason: '' })
  await nextTick(); panel.value?.focus(); panel.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
function selectOp(detail: SalesDetail) {
  const op = ops.value.find(o => o.id === detail.selectedOpId)
  if (op) { detail.selectedOpNumber = op.number; detail.selectedProduct = op.product }
  else { detail.selectedOpNumber = ''; detail.selectedProduct = '' }
  detail.usePortalCode = false; codeChoices.value[detail.id] = false; detail.reviewed = false
  changed()
}
function setCode(id: string, value: boolean) {
  const detail = report.value?.data.details.find(item => item.id === id)
  if (detail) detail.usePortalCode = value
  codeChoices.value[id] = value; changed()
}
function undoDetail(detail: SalesDetail) {
  detail.selectedOpId = null; selectOp(detail); detail.excluded = false; detail.reason = ''; detail.manualGroup = null
}
async function revealGroup(group: SalesGroup, task?: 'op' | 'excel') {
  if (!filtered.value.some(item => item.key === group.key)) { search.value = ''; filter.value = 'all' }
  await nextTick()
  page.value = Math.floor(Math.max(0, filtered.value.findIndex(item => item.key === group.key)) / pageSize) + 1
  await nextTick(); await open(group, task)
}
async function openPreviewGroup(key: string, task: 'op' | 'excel') {
  const group = report.value?.groups.find(item => item.key === key)
  if (group) {
    await revealGroup(group, task)
    const field = panel.value?.querySelector<HTMLInputElement>('.record-excel-form input')
    if (field) { field.focus({ preventScroll: true }); field.scrollIntoView({ behavior: 'smooth', block: 'center' }) }
  }
}
async function continueReview() {
  const group = report.value?.groups.find(needsReview) ?? report.value?.groups[0]
  resultKeys.value = []; resultExcluded.value = false
  if (group) await revealGroup(group)
}
async function save(context: 'editor' | 'header' | 'other' = 'other') {
  if (!report.value) return
  const keepEditor = context === 'editor' || context === 'header' && step.value === 2 && reviewTab.value === 'records'
  const detailIds = keepEditor ? new Set(active.value?.detailIds ?? []) : new Set<string>()
  const contextIds = new Set(keepEditor && continuationIds.value.length ? continuationIds.value : detailIds)
  busy.value = true; error.value = ''
  try {
    const value = await reportService.save(report.value, codeChoices.value)
    const results = value.groups.filter(group => group.detailIds.some(id => detailIds.has(id)))
    const contextResults = value.groups.filter(group => group.detailIds.some(id => contextIds.has(id)))
    setReport(value); message.value = 'Reporte guardado. Puedes continuar después.'
    if (detailIds.size) { continuationIds.value = [...contextIds]; resultKeys.value = contextResults.map(group => group.key) }
    if (detailIds.size && results.length === 1) await revealGroup(results[0]!)
    else if (detailIds.size) {
      resultExcluded.value = results.length === 0
      await nextTick(); resultPanel.value?.focus(); resultPanel.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }
  catch (e) { error.value = e instanceof Error ? e.message : 'No se guardaron los cambios. Siguen disponibles en pantalla.' }
  finally { busy.value = false }
}
function undoGroup() {
  if (!report.value || !active.value) return
  const documentDetails = report.value.data.details.filter(d => d.documentId === active.value?.documentId)
  const proposedAmount = activeDetails.value.length === documentDetails.length && activeDetails.value.every(d => d.selectedOpId && d.reviewed)
    ? report.value.data.documents.find(d => d.id === active.value?.documentId)?.sourceAmount ?? null : null
  report.value.data.edits = report.value.data.edits.filter(e => e.key !== active.value?.key)
  report.value.data.edits.push({ key: active.value.key, factura: '', amount: proposedAmount, client: null, line: null, seller: null, reason: '' }); changed()
}
function restore(previous: GroupEdit) {
  if (!report.value || !restoreTo.value) return
  report.value.data.edits = report.value.data.edits.filter(e => e.key !== restoreTo.value)
  report.value.data.edits.push({ ...previous, key: restoreTo.value }); changed()
  message.value = 'Ajustes copiados al registro elegido. Revísalos y guarda para confirmar.'
}
async function previewSource(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; (event.target as HTMLInputElement).value = ''
  if (!file || !report.value) return
  if (dirty.value) { error.value = 'Guarda tus cambios antes de actualizar el archivo.'; return }
  busy.value = true; error.value = ''
  try {
    replacement.value = await reportService.replace(report.value, file, false); replacementFile.value = file
    if (replacement.value.identical) { message.value = 'Es el mismo archivo. No se duplicaron ventas ni se cambiaron tus ajustes.'; replacement.value = null }
  } catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible revisar el nuevo archivo.' }
  finally { busy.value = false }
}
async function confirmSource() {
  if (!report.value || !replacementFile.value) return
  busy.value = true; error.value = ''
  try { setReport((await reportService.replace(report.value, replacementFile.value, true)).preview); replacement.value = null; replacementFile.value = null; message.value = 'Fuente actualizada. Revisa los registros y ajustes que necesiten reconfirmación.' }
  catch (e) { error.value = e instanceof Error ? e.message : 'No se cambió la fuente del reporte.' }
  finally { busy.value = false }
}
async function download() {
  if (!report.value || busy.value || dirty.value || !report.value.canExportDraft || !canDownload.value) return
  busy.value = true; error.value = ''; message.value = ''; downloadFailure.value = false
  try {
    const blob = await reportService.exportDraft(report.value); const url = URL.createObjectURL(blob)
    const link = document.createElement('a'); link.href = url; link.download = 'BORRADOR-VENTAS-MES.xlsx'; document.body.append(link); link.click(); link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 10000)
    message.value = `Borrador descargado de la versión guardada ${report.value.version}.`
  } catch (e) { downloadFailure.value = true; error.value = e instanceof Error ? e.message : 'No fue posible generar el Excel.' }
  finally { busy.value = false }
}
function protect(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
function reloadSaved() { if (!dirty.value || window.confirm('¿Recargar la versión guardada y descartar los cambios de esta pantalla?')) void load() }
onBeforeRouteLeave(() => !dirty.value || window.confirm('Hay cambios sin guardar. ¿Quieres salir y conservar solamente la versión guardada?'))
onMounted(() => { void load(); window.addEventListener('beforeunload', protect) })
onUnmounted(() => window.removeEventListener('beforeunload', protect))
</script>
<template>
  <AppLayout>
    <PageHeader eyebrow="COMERCIAL · REPORTES" title="Reporte de ventas" description="Confirma las OP, completa los datos manuales y conserva todos los detalles.">
      <RouterLink class="button" to="/commercial/reports/monthly"><ArrowLeft :size="17" /> Informe Mensual</RouterLink>
      <button v-if="canEdit" class="button primary" :disabled="busy || loading || !dirty" @click="save('header')"><Save :size="17" />{{ busy ? 'Procesando…' : 'Guardar reporte' }}</button>
    </PageHeader>
    <p v-if="error && (step !== 3 || !report)" class="form-error" role="alert">{{ error }} <button v-if="report" class="button small" @click="reloadSaved">Recargar versión guardada</button></p>
    <p v-if="message && step !== 3" class="reports-success" role="status">{{ message }}</p>
    <p v-if="loading" role="status">Cargando reporte…</p>
    <template v-if="report && !loading">
      <ol class="reports-steps" aria-label="Pasos del reporte"><li><b>1</b> Datos preparados</li><li :aria-current="step === 2 ? 'step' : undefined"><button @click="backToReview"><b>2</b> Revisar reporte</button></li><li :aria-current="step === 3 ? 'step' : undefined"><button @click="showDownload"><b>3</b> Descargar Excel</button></li></ol>
      <section class="panel reports-source-summary" aria-label="Datos del reporte">
        <div class="reports-name-field"><label for="report-name">Nombre del reporte</label><input id="report-name" v-model="report.name" maxlength="180" :disabled="!canEdit || busy" @input="changed" /></div>
        <div class="reports-source-file-row">
          <div class="reports-source-file"><span class="reports-source-file-icon"><FileSpreadsheet :size="22" aria-hidden="true" /></span><div class="reports-source-file-info"><span class="reports-source-file-label">Archivo de Manager</span><strong class="reports-source-filename">{{ report.data.sourceFile }}</strong><div class="reports-source-meta"><span>{{ report.data.details.length }} detalles de origen</span><span>Versión {{ report.version }}</span></div></div></div>
          <div class="reports-source-actions"><span class="reports-save-state" :class="{ unsaved: dirty }" role="status"><CircleAlert v-if="dirty" :size="15" aria-hidden="true" /><CircleCheck v-else :size="15" aria-hidden="true" />{{ dirty ? 'Cambios sin guardar' : 'Guardado' }}</span><a class="button small" :href="reportService.originalUrl(report.data.currentSourceId)">Ver Excel original</a></div>
        </div>
      </section>
      <section v-if="replacement" class="panel reports-replacement" aria-label="Revisión del nuevo archivo"><h2>Antes de actualizar la fuente</h2><p>{{ replacement.fileName }}: {{ replacement.retained }} detalles conservados, {{ replacement.added }} nuevos y {{ replacement.removed }} retirados.</p><p>Los ajustes de registros que cambien se conservan para reconfirmarlos. Los nuevos detalles requieren revisión.</p><details><summary>Ver registros de la nueva fuente</summary><ul><li v-for="g in replacement.preview.groups" :key="g.key">NUMERO {{ g.number }} · {{ g.op || 'OP pendiente' }} · {{ g.product || g.line }} · {{ g.details.length }} detalles</li></ul></details><button class="button primary" :disabled="busy" @click="confirmSource">Aplicar este archivo</button><button class="button" :disabled="busy" @click="replacement = null; replacementFile = null">Conservar archivo actual</button></section>
      <template v-if="step === 2">
        <nav ref="reviewTabs" class="reports-review-tabs" aria-label="Secciones de revisión">
          <button :aria-pressed="reviewTab === 'records'" @click="selectReviewTab('records')"><FileSpreadsheet :size="18" aria-hidden="true" /> Informe de ventas <span>{{ report.groups.length }}</span></button>
          <button :aria-pressed="reviewTab === 'amounts'" @click="selectReviewTab('amounts')"><ClipboardList :size="18" aria-hidden="true" /> Informe de OPs <span>{{ unresolvedControls.length }} por comprobar</span></button>
        </nav>
        <section v-show="reviewTab === 'records'" class="panel reports-table-panel" aria-label="Informe de ventas">
          <div class="reports-section-head"><div><h2>Informe de ventas</h2><p class="muted">Revisa las OP y completa los datos de un registro a la vez.</p></div></div>
          <div class="reports-toolbar"><label class="reports-search"><Search :size="18" /><span class="sr-only">Buscar OP, cliente, producto o NUMERO</span><input v-model="search" placeholder="Buscar OP, cliente o producto" @input="page = 1" /></label><label>Mostrar<select v-model="filter" @change="page = 1"><option value="all">Todos</option><option value="review">Por revisar</option><option value="changed">Modificados por mí</option></select></label></div>
          <div class="reports-table-scroll"><table class="reports-table"><thead><tr><th>OP / NUMERO</th><th>Cliente</th><th>Producto / línea</th><th>Importe</th><th>Estado</th><th>Acción</th></tr></thead><tbody><tr v-for="group in visible" :key="group.key"><td data-label="OP / NUMERO"><strong>{{ group.op || 'OP por confirmar' }}</strong><small>NUMERO {{ group.number }}</small></td><td data-label="Cliente">{{ group.client }}</td><td data-label="Producto / línea">{{ group.product || 'Producto por confirmar' }}<small>{{ group.line }}</small></td><td data-label="Importe">{{ money(group.amount) }}</td><td data-label="Estado"><span class="reports-badge" :class="{ pending: needsReview(group) }">{{ needsReview(group) ? 'Por revisar' : 'Revisado' }}</span></td><td data-label="Acción"><button class="button small" :disabled="busy" @click="open(group)">{{ canEdit ? 'Revisar / editar' : 'Ver' }}</button><small>{{ group.details.length }} detalles</small></td></tr><tr v-if="visible.length === 0"><td colspan="6">No hay registros para este filtro.</td></tr></tbody></table></div>
          <ReportPagination :page="page" :total="filtered.length" :page-size="pageSize" label="Páginas del informe de ventas" @change="page = $event" />
        </section>
        <section v-if="active" v-show="reviewTab === 'records'" ref="panel" tabindex="-1" class="panel reports-editor" aria-label="Revisar registro">
          <ReportRecordEditor :key="`${active.key}-${editorEpoch}`" :group="active" :details="activeDetails" :ops="ops" :edit="edit"
            :groups="report.groups" :edits="report.data.edits" :can-edit="canEdit" :busy="busy" :dirty="dirty" :regroup="regroup"
            :code-choices="codeChoices" :initial-task="editorTask" @change="changed" @select-op="selectOp" @structural-change="changed"
            @set-code="setCode" @undo-detail="undoDetail" @undo-group="undoGroup" @save="save('editor')"
            @open-group="openPreviewGroup" @close="editingKey = null" />
        </section>
        <section v-if="resultGroups.length > 1 || resultExcluded" v-show="reviewTab === 'records'" ref="resultPanel" tabindex="-1" class="panel reports-editor reports-save-results" :aria-label="resultExcluded ? 'Resultado de la revisión' : 'Registros resultantes'">
          <h2>{{ resultExcluded ? 'Los detalles quedaron excluidos' : 'Continúa con los registros resultantes' }}</h2>
          <p class="muted">{{ resultExcluded ? 'Puedes consultar sus motivos en Detalles excluidos o continuar con otro registro.' : 'La corrección reorganizó estos detalles. Abre cada registro para revisar sus datos del Excel.' }}</p>
          <ul v-if="resultGroups.length" class="reports-result-list"><li v-for="group in visibleResultGroups" :key="group.key"><div><strong>OP {{ group.op || 'por confirmar' }} · {{ group.product || group.line }}</strong><span>{{ group.client }} · {{ group.details.length }} detalles</span></div><button class="button small" :disabled="busy" @click="revealGroup(group)">Revisar registro</button></li></ul>
          <ReportPagination v-if="resultGroups.length > pageSize" :page="resultPage" :total="resultGroups.length" :page-size="pageSize" label="Páginas de registros resultantes" @change="resultPage = $event" />
          <button v-if="resultExcluded" class="button" :disabled="busy" @click="continueReview">Continuar revisión</button>
        </section>
        <section v-show="reviewTab === 'amounts'" class="panel reports-table-panel" aria-label="Informe de OPs">
          <div class="reports-section-head"><div><h2>Informe de OPs</h2><p class="muted">Comprueba el total de cada NUMERO. Abre solo el importe que necesitas ajustar.</p></div></div>
          <div class="reports-toolbar">
            <label class="reports-search"><Search :size="18" /><span class="sr-only">Buscar importe por NUMERO o cliente</span><input v-model="amountSearch" placeholder="Buscar NUMERO o cliente" @input="amountPage = 1" /></label>
            <label>Mostrar<select v-model="amountFilter" @change="amountPage = 1"><option value="all">Todos</option><option value="pending">Por comprobar</option><option value="checked">Comprobados</option></select></label>
          </div>
          <div class="reports-table-scroll"><table class="reports-table reports-amount-table"><thead><tr><th>NUMERO / Cliente</th><th>Valor de Manager</th><th>Total confirmado</th><th>Distribuido</th><th>Diferencia</th><th>Estado</th><th>Acción</th></tr></thead><tbody>
            <tr v-for="doc in visibleAmounts" :key="doc.id"><td data-label="NUMERO / Cliente"><strong>{{ doc.number }}</strong><small>{{ doc.client }}</small></td><td data-label="Valor de Manager">{{ money(doc.sourceAmount) }}</td><td data-label="Total confirmado">{{ money(doc.confirmedAmount) }}</td><td data-label="Distribuido">{{ money(controlsById.get(doc.id)?.distributed ?? 0) }}</td><td data-label="Diferencia">{{ money(controlsById.get(doc.id)?.difference ?? null) }}</td><td data-label="Estado"><span class="reports-badge" :class="{ pending: amountPending(doc.id) }">{{ amountPending(doc.id) ? 'Por comprobar' : 'Comprobado' }}</span></td><td data-label="Acción"><button class="button small" :disabled="busy" @click="openAmount(doc.id)">{{ canEdit ? 'Revisar importe' : 'Ver importe' }}</button></td></tr>
            <tr v-if="visibleAmounts.length === 0"><td colspan="7">No hay importes para este filtro.</td></tr>
          </tbody></table></div>
          <ReportPagination :page="amountPage" :total="amountDocuments.length" :page-size="pageSize" label="Páginas del informe de OPs" @change="amountPage = $event" />
          <p class="muted reports-table-note">Los totales y las diferencias se actualizan al guardar. Los cambios que hagas se conservan al pasar de página o de sección.</p>
        </section>
        <section v-if="activeAmount" v-show="reviewTab === 'amounts'" ref="amountPanel" tabindex="-1" class="panel reports-editor reports-amount-editor" aria-label="Revisar importe">
          <div class="reports-section-head"><div><p class="eyebrow">NUMERO {{ activeAmount.number }}</p><h2>Revisar este importe</h2><p>{{ activeAmount.client }}</p></div><button class="button small" @click="amountId = null">Cerrar</button></div>
          <p class="muted">El valor de Manager puede repetirse en varios detalles. Confirma el total de este NUMERO y distribúyelo entre sus registros de ventas.</p>
          <div class="reports-amount-summary"><div><span>Valor de Manager</span><strong>{{ money(activeAmount.sourceAmount) }}</strong></div><div><span>Distribuido</span><strong>{{ money(controlsById.get(activeAmount.id)?.distributed ?? 0) }}</strong></div><div><span>Diferencia guardada</span><strong>{{ money(controlsById.get(activeAmount.id)?.difference ?? null) }}</strong></div></div>
          <div v-if="canEdit" class="reports-amount-fields"><label>Total confirmado<input type="number" step="0.01" :value="activeAmount.confirmedAmount ?? ''" :disabled="busy" @input="activeAmount.confirmedAmount = inputMoney($event); changed()" /></label><button v-if="activeAmount.sourceAmount !== null" class="button small" :disabled="busy" @click="activeAmount.confirmedAmount = activeAmount.sourceAmount; changed()">Confirmar este valor de Manager</button><label class="reports-amount-reason">Explicación si el total cambia<input v-model="activeAmount.reason" maxlength="1000" :disabled="busy" @input="changed" /></label></div>
          <button v-if="canEdit" class="button primary" :disabled="busy || !dirty" @click="save()"><Save :size="17" /> Guardar y actualizar revisión</button>
        </section>
        <details v-if="report.data.details.some(d => d.excluded)" class="panel"><summary>Detalles excluidos</summary><p v-for="d in report.data.details.filter(x => x.excluded)" :key="d.id">Fila {{ d.sourceRow }} · {{ d.detail }} · Motivo: {{ d.reason }} <button v-if="canEdit" class="button small" :disabled="busy" @click="d.excluded = false; changed()">Volver a incluir</button></p></details>
        <details v-if="report.data.unappliedEdits.length" class="panel reports-history"><summary>Ajustes anteriores conservados para reconfirmar</summary><p>Cambió el contenido o la agrupación. Estos valores se conservan para que puedas revisarlos y copiarlos al registro correcto.</p><label v-if="canEdit">Registro de destino<select v-model="restoreTo"><option value="">Seleccionar</option><option v-for="g in report.groups" :key="g.key" :value="g.key">{{ g.number }} · {{ g.op }} · {{ g.product }}</option></select></label><article v-for="(previous, index) in report.data.unappliedEdits" :key="index"><p>FACTURA {{ previous.factura || 'Vacía' }} · {{ money(previous.amount) }} · {{ previous.client }} · {{ previous.line }} · {{ previous.seller }} · {{ previous.reason }}</p><button v-if="canEdit" class="button small" :disabled="!restoreTo" @click="restore(previous)">Copiar para reconfirmar</button></article></details>
        <details v-if="canEdit" class="panel reports-history"><summary>Actualizar el informe de Manager</summary><p>Guarda primero tus cambios. Verás las diferencias antes de aplicar el archivo.</p><label for="replace-manager">Nuevo archivo (.xlsx)<input id="replace-manager" type="file" accept=".xlsx" :disabled="busy || dirty" @change="previewSource" /></label></details>
        <button class="button primary" @click="showDownload">Revisar descarga <ArrowRight :size="17" /></button>
      </template>
      <template v-else>
        <section ref="downloadOverview" tabindex="-1" class="panel reports-export-overview" aria-label="Estado de la descarga">
          <p class="eyebrow">PASO 3 · DESCARGAR EXCEL</p>
          <h2>Descargar borrador · versión anterior</h2>
          <div class="reports-export-state" :class="{ ready: downloadState === 'ready', failed: downloadState === 'failed' }" role="status">
            <CircleCheck v-if="downloadState === 'ready'" :size="24" aria-hidden="true" /><Save v-else-if="downloadState === 'dirty'" :size="24" aria-hidden="true" /><CircleAlert v-else :size="24" aria-hidden="true" />
            <div><h3>{{ downloadTitle }}</h3><p>{{ downloadDescription }}</p></div>
          </div>
          <dl class="reports-export-summary"><div><dt>Registros del Excel</dt><dd>{{ report.groups.length }}</dd></div><div><dt>Detalles incluidos</dt><dd>{{ includedDetails }}</dd></div><div><dt>Revisiones pendientes</dt><dd>{{ pendingTotal }}</dd></div></dl>
          <p v-if="dirty" class="muted reports-table-note">Los pendientes corresponden a la última revisión guardada. Guarda para actualizarlos.</p>
        </section>
        <section class="panel reports-export-file" aria-label="Archivo a descargar">
          <div class="reports-export-file-info"><span class="reports-export-file-icon"><FileSpreadsheet :size="26" aria-hidden="true" /></span><div><h2>BORRADOR VENTAS MES.xlsx</h2><p class="muted">Copia de los datos guardados de la preparación anterior, sin aprobación del nuevo flujo.</p></div></div>
          <div class="reports-export-actions"><button v-if="canEdit && dirty" class="button primary" :disabled="busy" @click="save()"><Save :size="17" />{{ busy ? 'Guardando…' : 'Guardar y comprobar' }}</button><button v-if="canDownload" class="button" :class="{ primary: downloadReady }" :disabled="busy || !downloadReady" @click="download"><Download :size="18" />{{ busy ? 'Procesando…' : 'Descargar borrador' }}</button><button class="button" :disabled="busy" @click="backToReview"><ArrowLeft :size="17" /> Volver a revisar</button></div>
          <p v-if="!canDownload" class="muted reports-table-note">Tu usuario no tiene permiso para descargar este Excel. Solicita la descarga a un usuario autorizado.</p>
          <p v-if="!canEdit && !downloadReady" class="muted reports-table-note">La auxiliar comercial debe completar y guardar los pendientes para habilitar la descarga.</p>
          <p v-if="error" class="form-error" role="alert">{{ error }} <button class="button small" :disabled="busy" @click="reloadSaved">Recargar versión guardada</button></p>
          <p v-if="message" class="reports-success" role="status">{{ message }}</p>
          <p v-if="report.lastExportedVersion" class="muted reports-table-note">{{ report.lastExportedVersion === report.version ? 'Esta versión ya se descargó. Puedes descargarla de nuevo.' : 'La última descarga corresponde a una versión anterior del reporte.' }}</p>
        </section>
        <section v-if="pendingTotal" class="panel reports-download-pending" aria-label="Pendientes para descargar">
          <div class="reports-section-head"><div><h2>Qué falta por completar</h2><p class="muted">Abre un pendiente para revisar ese registro. Se muestran 5 por página.</p></div></div>
          <nav class="reports-review-tabs" aria-label="Tipos de pendientes"><button :aria-pressed="downloadTab === 'records'" @click="downloadTab = 'records'"><FileSpreadsheet :size="18" aria-hidden="true" /> Informe de ventas <span>{{ pendingGroups.length }}</span></button><button :aria-pressed="downloadTab === 'amounts'" @click="downloadTab = 'amounts'"><ClipboardList :size="18" aria-hidden="true" /> Informe de OPs <span>{{ unresolvedControls.length }}</span></button></nav>
          <div class="reports-toolbar"><label class="reports-search"><Search :size="18" /><span class="sr-only">Buscar pendiente</span><input v-model="downloadSearch" placeholder="Buscar número, OP o cliente" /></label></div>
          <div class="reports-table-scroll"><table class="reports-table reports-pending-table"><thead><tr><th>Registro</th><th>Cliente</th><th>Qué debes completar</th><th>Acción</th></tr></thead><tbody><tr v-for="item in visibleDownloadItems" :key="item.key"><td data-label="Registro"><strong>NUMERO {{ item.number }}</strong><small>{{ item.description }}</small></td><td data-label="Cliente">{{ item.client }}</td><td data-label="Qué debes completar"><p v-for="issue in item.issues" :key="issue" class="reports-pending-reason">{{ issue }}</p></td><td data-label="Acción"><button class="button small" :disabled="busy" @click="reviewDownloadPending(item)">{{ item.group ? (canEdit ? 'Revisar registro' : 'Ver registro') : (canEdit ? 'Revisar importe' : 'Ver importe') }}<ArrowRight :size="15" aria-hidden="true" /></button></td></tr><tr v-if="!visibleDownloadItems.length"><td colspan="4">{{ downloadItems.length ? 'No hay pendientes para esta búsqueda.' : 'No quedan pendientes en este informe.' }}</td></tr></tbody></table></div>
          <ReportPagination :page="downloadPage" :total="filteredDownloadItems.length" :page-size="pageSize" label="Páginas de pendientes" @change="downloadPage = $event" />
        </section>
      </template>
    </template>
  </AppLayout>
</template>
