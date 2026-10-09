<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { CheckCircle2, ChevronDown, Download, Eye, Factory, FilePenLine, Plus, Send, Trash2, Upload, XCircle } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import ConfirmDialog from '../../../components/ConfirmDialog.vue'
import { ApiError } from '../../../api/httpClient'
import ReviewAssignmentDialog from './ReviewAssignmentDialog.vue'
import { initialReviewerSelection } from './reviewAssignment'
import ProductionOrderStatusBadge from './ProductionOrderStatusBadge.vue'
import { documentStatusLabel, isProductionOrderVersionConflict } from './documentPackage'
import { productionOrdersService } from './productionOrdersService'
import type { ProductionOrderDetail, ProductionOrderReviewer, ProductionOrderInput, RelatedProductionOrder } from './types'

const route = useRoute()
const router = useRouter()
const order = ref<ProductionOrderDetail>()
const loading = ref(true)
const saving = ref(false)
const navigating = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const activeTab = ref<'commercial' | 'production'>('commercial')
const confirmation = ref<'submitReview' | 'approveReview' | 'receive' | 'complete' | null>(null)
const reviewers = ref<ProductionOrderReviewer[]>([])
const reviewerUserId = ref('')
const reviewersLoading = ref(false)
const reviewersLoadError = ref('')
const reviewSubmitError = ref('')
let reviewersSequence = 0
const discardOpen = ref(false)
const cancellationOpen = ref(false)
const cancellationReason = ref('')
const returnOpen = ref(false)
const returnReason = ref('')
const reviewCustomerOrderNumber = ref('')
const editingProduction = ref(false)
const productionForm = reactive<ProductionOrderInput>({
  version: 0, planningDate: null, planningManager: null, materialCutDate: null, cuttingManager: null,
  printStartShift1: null, printingManagerShift1: null, printStartShift2: null, printingManagerShift2: null,
  finishingStart: null, finishingManager: null, dieCutStart: null, dieCutManager: null, dieMachine: null, dieNumber: null,
  dieTotalProcessed: null, dieConforming: null, dieNonConforming: null, gluingStart: null, gluingManager: null, glueType: null,
  glueTotalProcessed: null, glueConforming: null, glueNonConforming: null, qualityReviewDate: null, qualityReviewer: null,
  qualityApproved: null, qualityNotes: null, materials: [], printLines: [], finishes: [],
})

const id = computed(() => route.params.id as string)
const operationOrders = computed<RelatedProductionOrder[]>(() => order.value ? [
  {
    id: order.value.id,
    code: order.value.code,
    status: order.value.status,
    productName: order.value.commercial.productName,
    quantity: order.value.commercial.quantity,
  },
  ...order.value.relatedOrders,
] : [])
const can = (action: string) => order.value?.allowedActions.includes(action as never) ?? false
const totalValue = computed(() => order.value?.commercial.totalValue ?? null)
const purchaseOrderStatus = computed(() => documentStatusLabel('purchaseOrder', order.value?.checklist.purchaseOrder ?? 'pending'))
const designStatus = computed(() => documentStatusLabel('design', order.value?.checklist.design ?? 'pending'))

const labels: Record<string, string> = {
  new: 'Trabajo nuevo', change: 'Cambio', repeat: 'Repetición', replacement: 'Reposición', unspecified: 'Sin definir',
  none: 'No aplica', existing: 'Existente', physical: 'Físico', digital: 'Digital', both: 'Físico y digital',
}

let loadSequence = 0

async function load() {
  const requestedId = id.value
  const sequence = ++loadSequence
  loading.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const loaded = await productionOrdersService.get(requestedId)
    if (sequence !== loadSequence || id.value !== requestedId) return
    order.value = loaded
    reviewCustomerOrderNumber.value = loaded.commercial.customerOrderNumber ?? ''
    fillProduction()
  }
  catch (error) {
    if (sequence === loadSequence && id.value === requestedId) errorMessage.value = describe(error)
  }
  finally {
    if (sequence === loadSequence && id.value === requestedId) loading.value = false
  }
}

function fillProduction() {
  if (!order.value) return
  const p = order.value.production
  Object.assign(productionForm, {
    version: order.value.version,
    ...p,
    printStartShift1: toLocal(p.printStartShift1), printStartShift2: toLocal(p.printStartShift2), finishingStart: toLocal(p.finishingStart),
    dieCutStart: toLocal(p.dieCutStart), gluingStart: toLocal(p.gluingStart), qualityReviewDate: toLocal(p.qualityReviewDate),
    materials: order.value.materials.map((item) => ({ id: item.id, sheetSize: item.sheetSize, sheetQuantity: item.sheetQuantity, cutSize: item.cutSize, fractionPerSheet: item.fractionPerSheet, fitPerFraction: item.fitPerFraction, totalCutQuantity: item.totalCutQuantity, conformingQuantity: item.conformingQuantity, nonConformingQuantity: item.nonConformingQuantity })),
    printLines: order.value.printLines.map((item) => ({ id: item.id, machine: item.machine, mounting: item.mounting, shotsToProcess: item.shotsToProcess, conformingQuantity: item.conformingQuantity, nonConformingQuantity: item.nonConformingQuantity })),
    finishes: order.value.finishes.map((item) => ({ id: item.id, totalProcessed: item.totalProcessed, conformingQuantity: item.conformingQuantity, nonConformingQuantity: item.nonConformingQuantity })),
  })
}

function describe(error: unknown) { return error instanceof ApiError ? error.message : 'No fue posible completar la operación.' }
async function runDocumentMutation(
  orderId: string,
  version: number,
  operation: (orderId: string, version: number) => Promise<ProductionOrderDetail>,
) {
  try {
    return await operation(orderId, version)
  } catch (error) {
    if (!isProductionOrderVersionConflict(error)) throw error
    const latest = await productionOrdersService.get(orderId)
    if (id.value !== orderId) throw new Error('La navegación cambió antes de completar el guardado.', { cause: error })
    return operation(orderId, latest.version)
  }
}

async function confirmPersisted(orderId: string, message: string) {
  const persisted = await productionOrdersService.get(orderId)
  if (id.value !== orderId) return
  order.value = persisted
  fillProduction()
  successMessage.value = message
}

async function navigateToOrder(targetId: string) {
  if (saving.value || navigating.value || targetId === id.value) return
  navigating.value = true
  try {
    await router.push(`/commercial/production-orders/${targetId}`)
  } finally {
    navigating.value = false
  }
}
function toLocal(value: string | null) { return value ? new Date(value).toISOString().slice(0, 16) : null }
function toIso(value: string | null) { return value ? new Date(value).toISOString() : null }
function formatDate(value: string | null, includeTime = false) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('es-CO', includeTime ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(includeTime ? value : `${value}T00:00:00Z`))
}
function formatNumber(value: number | null) { return value == null ? '—' : new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(value) }
function formatMoney(value: number | null) { return value == null ? '—' : new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value) }
function text(value: string | null) { return value || '—' }

async function runAction(action: 'approveReview' | 'receive' | 'complete') {
  if (!order.value) return
  saving.value = true; errorMessage.value = ''
  try {
    order.value = action === 'approveReview' ? await productionOrdersService.approveReview(order.value.id, order.value.version, reviewCustomerOrderNumber.value.trim())
      : action === 'receive' ? await productionOrdersService.receive(order.value.id, order.value.version)
        : await productionOrdersService.complete(order.value.id, order.value.version)
    fillProduction()
    if (action === 'receive') activeTab.value = 'production'
  } catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false; confirmation.value = null }
}

async function fetchReviewers() {
  const requestedId = id.value
  const sequence = ++reviewersSequence
  reviewersLoading.value = true
  reviewersLoadError.value = ''
  reviewers.value = []
  reviewerUserId.value = ''
  try {
    const candidates = await productionOrdersService.reviewers(requestedId)
    if (sequence !== reviewersSequence || requestedId !== id.value || confirmation.value !== 'submitReview') return
    reviewers.value = candidates
    reviewerUserId.value = initialReviewerSelection(candidates)
  } catch {
    if (sequence === reviewersSequence && requestedId === id.value) reviewersLoadError.value = 'No fue posible consultar las auxiliares. Reintentar.'
  } finally {
    if (sequence === reviewersSequence && requestedId === id.value) reviewersLoading.value = false
  }
}

function openReviewDialog() {
  if (saving.value || confirmation.value === 'submitReview') return
  confirmation.value = 'submitReview'
  reviewSubmitError.value = ''
  void fetchReviewers()
}

function closeReviewDialog() {
  if (saving.value) return
  ++reviewersSequence
  confirmation.value = null
  reviewerUserId.value = ''
}

async function submitReview() {
  if (!order.value || saving.value || reviewersLoading.value || reviewersLoadError.value || !reviewerUserId.value) return
  const requestedId = id.value
  saving.value = true
  reviewSubmitError.value = ''
  errorMessage.value = ''
  try {
    const updated = await productionOrdersService.submitForReview(requestedId, order.value.version, reviewerUserId.value)
    if (id.value !== requestedId) return
    order.value = updated
    fillProduction()
    confirmation.value = null
    successMessage.value = `Asignada a: ${updated.reviewOwner?.name ?? 'Auxiliar Comercial'}.`
  } catch (error) {
    if (id.value !== requestedId) return
    if (isProductionOrderVersionConflict(error) || (error instanceof ApiError && error.status === 403)) {
      const latest = await productionOrdersService.get(requestedId).catch(() => undefined)
      if (id.value !== requestedId) return
      if (latest) { order.value = latest; fillProduction() }
      confirmation.value = null
      errorMessage.value = 'La orden cambió. Consulta el estado y responsable actuales antes de continuar.'
    } else {
      reviewSubmitError.value = describe(error)
      if (error instanceof ApiError && ['reviewer_unavailable', 'reviewer_required', 'commercial_assistant_configuration'].includes(error.code ?? '')) await fetchReviewers()
    }
  } finally { saving.value = false }
}

async function uploadDocument(type: 'purchaseOrder' | 'design', event: Event) {
  if (!order.value || saving.value) return
  const orderId = order.value.id
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  if (!files.length) return
  saving.value = true; errorMessage.value = ''
  successMessage.value = ''
  try {
    let version = order.value.version
    for (const file of type === 'purchaseOrder' ? files.slice(0, 1) : files) {
      const updated = await runDocumentMutation(orderId, version, (targetId, currentVersion) => productionOrdersService.addDocument(targetId, currentVersion, type, file))
      version = updated.version
    }
    await confirmPersisted(orderId, type === 'purchaseOrder' ? 'Orden de compra guardada.' : 'Documento de Diseño guardado.')
  }
  catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false; input.value = '' }
}

async function setNotApplicable(type: 'purchaseOrder' | 'design', notApplicable: boolean) {
  if (!order.value || saving.value) return
  const orderId = order.value.id
  const version = order.value.version
  saving.value = true; errorMessage.value = ''
  successMessage.value = ''
  try {
    await runDocumentMutation(orderId, version, (targetId, currentVersion) => productionOrdersService.setDocumentApplicability(targetId, currentVersion, type, notApplicable))
    await confirmPersisted(orderId, notApplicable
      ? (type === 'purchaseOrder' ? 'Orden de compra marcada como No aplica.' : 'Diseño marcado como No requerido.')
      : (type === 'purchaseOrder' ? 'La orden de compra vuelve a ser requerida.' : 'El documento de Diseño vuelve a ser requerido.'))
  }
  catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false }
}

async function removeDocument(documentId: string) {
  if (!order.value || saving.value) return
  const orderId = order.value.id
  const version = order.value.version
  saving.value = true; errorMessage.value = ''
  successMessage.value = ''
  try {
    await runDocumentMutation(orderId, version, (targetId, currentVersion) => productionOrdersService.deleteDocument(targetId, documentId, currentVersion))
    await confirmPersisted(orderId, 'Documento eliminado y estado actualizado.')
  }
  catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false }
}

async function returnForCorrection() {
  if (!order.value) return
  saving.value = true; errorMessage.value = ''
  try {
    order.value = await productionOrdersService.returnForCorrection(order.value.id, order.value.version, returnReason.value)
    returnOpen.value = false; returnReason.value = ''
  } catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false }
}

function documentLabel(type: string) { return type === 'quotation' ? 'Cotización' : type === 'purchaseOrder' ? 'Orden de compra' : 'Diseño / arte' }
function fileSize(value: number) { return value < 1024 * 1024 ? `${Math.ceil(value / 1024)} KB` : `${(value / 1024 / 1024).toFixed(1)} MB` }

async function saveProduction() {
  if (!order.value) return
  saving.value = true; errorMessage.value = ''
  try {
    const payload = { ...productionForm, printStartShift1: toIso(productionForm.printStartShift1), printStartShift2: toIso(productionForm.printStartShift2), finishingStart: toIso(productionForm.finishingStart), dieCutStart: toIso(productionForm.dieCutStart), gluingStart: toIso(productionForm.gluingStart), qualityReviewDate: toIso(productionForm.qualityReviewDate) }
    order.value = await productionOrdersService.updateProduction(order.value.id, payload)
    fillProduction(); editingProduction.value = false
  } catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false }
}

async function cancelOrder() {
  if (!order.value) return
  saving.value = true; errorMessage.value = ''
  try { order.value = await productionOrdersService.cancel(order.value.id, order.value.version, cancellationReason.value); cancellationOpen.value = false; cancellationReason.value = '' }
  catch (error) { errorMessage.value = describe(error) }
  finally { saving.value = false }
}

async function discardDraft() {
  if (!order.value || !can('discard')) return
  saving.value = true; errorMessage.value = ''
  try {
    await productionOrdersService.discardDraft(order.value.id, order.value.version)
    discardOpen.value = false
    await router.replace('/commercial/production-orders')
  } catch (error) { errorMessage.value = describe(error); discardOpen.value = false }
  finally { saving.value = false }
}

onMounted(load)
watch(id, () => {
  ++reviewersSequence
  confirmation.value = null
  reviewerUserId.value = ''
  ++loadSequence
  activeTab.value = 'commercial'
  order.value = undefined
  void load()
})
</script>

<template>
  <AppLayout>
    <button v-if="order && can('discard')" class="back-link commercial-back-button" type="button" :disabled="saving" @click="discardOpen = true">← Volver a órdenes</button>
    <RouterLink v-else class="back-link" to="/commercial/production-orders">← Volver a órdenes</RouterLink>
    <div v-if="loading" class="panel commercial-empty">Cargando orden…</div>
    <template v-else-if="order">
      <section class="order-detail-hero">
        <div><p class="eyebrow">ORDEN DE PRODUCCIÓN</p><div class="order-title-line"><h1>{{ order.code }}</h1></div><p>{{ order.commercial.clientName || 'Cliente pendiente' }} · {{ order.commercial.productName || 'Producto pendiente' }}</p></div>
        <div class="order-actions">
          <RouterLink v-if="can('editCommercial')" class="button secondary" :to="`/commercial/production-orders/${order.id}/edit`"><FilePenLine :size="17" /> Editar</RouterLink>
          <RouterLink v-if="can('duplicate')" class="button secondary" :to="`/commercial/production-orders/new?relatedTo=${order.id}`"><Plus :size="17" /> Agregar otra OP</RouterLink>
          <button v-if="can('submitForReview')" class="button primary" type="button" :disabled="saving" @click="openReviewDialog"><Send :size="17" /> Enviar a revisión</button>
          <button v-if="can('approveReview')" class="button primary" type="button" :disabled="!reviewCustomerOrderNumber.trim()" @click="confirmation = 'approveReview'"><CheckCircle2 :size="17" /> Aprobar y enviar a Producción</button>
          <button v-if="can('returnForCorrection')" class="button secondary" type="button" @click="returnOpen = true">Devolver a Comercial</button>
          <button v-if="can('receive')" class="button primary" type="button" @click="confirmation = 'receive'"><Factory :size="17" /> Recibir orden</button>
          <button v-if="can('complete')" class="button primary" type="button" @click="confirmation = 'complete'"><CheckCircle2 :size="17" /> Finalizar</button>
          <button v-if="can('cancel')" class="button danger ghost" type="button" @click="cancellationOpen = true"><XCircle :size="17" /> Anular</button>
        </div>
      </section>
      <p v-if="order.status === 'pendingCommercialReview' && order.reviewOwner" class="notice">Asignada a: {{ order.reviewOwner.name }}</p>
      <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
      <p v-if="successMessage" class="notice success commercial-save-notice">{{ successMessage }}</p>
      <section v-if="order.status === 'correctionRequired' && order.lastReturnReason" class="correction-banner"><strong>Corrección requerida</strong><p>{{ order.lastReturnReason }}</p></section>
      <section v-if="can('approveReview')" class="panel review-order-number-card">
        <div><p class="eyebrow">ASIGNACIÓN DE LA AUXILIAR COMERCIAL</p><h2>Número de pedido</h2><p>Asigna el número antes de aprobar. Se guardará junto con la OP al enviarla a Producción.</p></div>
        <label><span class="field-label">Número de pedido <em>*</em></span><input v-model="reviewCustomerOrderNumber" maxlength="100" placeholder="Ej. PED-2048" /></label>
      </section>
      <section v-if="order.sourceOrderId" class="duplication-banner"><strong>Orden creada por duplicación</strong><p>Pedido, fecha de entrega, orden de compra, cotización y documentos no se copian para evitar reutilizar información de otra venta. Completa y adjunta los archivos propios de esta nueva OP.</p></section>
      <section v-if="operationOrders.length > 1" class="related-orders-panel">
        <div class="related-orders-heading">
          <div><p class="eyebrow">MISMA OPERACIÓN COMERCIAL</p><h2>OP de esta cotización</h2><p>Cada producto conserva su propia orden, estado y documentos.</p></div>
        </div>
        <div class="related-orders-list">
          <article v-for="related in operationOrders" :key="related.id" class="related-order-card" :class="{ current: related.id === order.id }">
            <div><strong>{{ related.code }}</strong><span>{{ related.productName || 'Producto pendiente' }}</span></div>
            <div class="related-order-meta">
              <span v-if="related.id === order.id" class="current-order-label" aria-current="page">OP actual</span>
              <ProductionOrderStatusBadge v-else :status="related.status" />
              <small>{{ formatNumber(related.quantity) }} unidades</small>
              <button v-if="related.id !== order.id" class="button secondary compact" type="button" :disabled="saving || navigating" @click="navigateToOrder(related.id)">
                <Eye :size="15" /> Ver OP
              </button>
            </div>
          </article>
        </div>
      </section>

      <section class="order-facts">
        <span><small>Pedido</small><strong :class="{ 'pending-value': !order.commercial.customerOrderNumber }">{{ order.commercial.customerOrderNumber || 'Pendiente de diligenciar' }}</strong></span>
        <span><small>Entrega</small><strong :class="{ 'pending-value': !order.commercial.deliveryDate }">{{ order.commercial.deliveryDate ? formatDate(order.commercial.deliveryDate) : 'Pendiente de diligenciar' }}</strong></span>
        <span><small>Cantidad</small><strong>{{ formatNumber(order.commercial.quantity) }}</strong></span>
        <span><small>Valor estimado</small><strong>{{ formatMoney(totalValue) }}</strong></span>
        <span><small>Responsable comercial</small><strong>{{ order.commercialOwner.name }}</strong></span>
      </section>

      <nav v-if="['readyForProduction', 'inProduction', 'completed'].includes(order.status)" class="order-tabs" aria-label="Secciones de la orden">
        <button type="button" :class="{ active: activeTab === 'commercial' }" @click="activeTab = 'commercial'">Información comercial</button>
        <button type="button" :class="{ active: activeTab === 'production' }" @click="activeTab = 'production'">Producción</button>
      </nav>

      <div v-if="activeTab === 'commercial'" class="order-detail-grid">
        <section class="panel order-wide-card document-package">
          <div><p class="eyebrow">PAQUETE DOCUMENTAL</p><h2>Documentos para revisión</h2><p>Cada bloque muestra su estado actual. Los botones indican acciones disponibles; no representan el estado del documento.</p></div>
          <div class="document-status-grid">
            <article>
              <div><strong>Cotización</strong><span :class="{ done: order.checklist.quotationReady, pending: !order.checklist.quotationReady }">{{ order.checklist.quotationReady ? 'Archivo adjunto' : 'Pendiente' }}</span></div>
              <p>Es obligatoria para enviar la OP a revisión.</p>
              <RouterLink v-if="!order.checklist.quotationReady && can('editCommercial')" class="button secondary compact" :to="`/commercial/production-orders/${order.id}/edit`">Adjuntar cotización desde Editar</RouterLink>
            </article>
            <article>
              <div><strong>Orden de compra</strong><span :class="{ done: order.checklist.purchaseOrder !== 'pending', pending: order.checklist.purchaseOrder === 'pending' }">{{ purchaseOrderStatus }}</span></div>
              <p>Adjunta el archivo recibido del cliente o marca expresamente que no aplica.</p>
            </article>
            <article>
              <div><strong>Diseño / arte</strong><span :class="{ done: order.checklist.design !== 'pending', pending: order.checklist.design === 'pending' }">{{ designStatus }}</span></div>
              <p>Adjunta uno o varios archivos de Diseño o marca que no se requieren.</p>
            </article>
          </div>
          <div class="document-list">
            <article v-for="document in order.documents" :key="document.id"><div><strong>{{ documentLabel(document.type) }}</strong><span>{{ document.originalFileName }} · {{ fileSize(document.size) }}</span></div><div><a class="button secondary compact" :href="productionOrdersService.downloadUrl(order.id, document.id)"><Download :size="15" /> Descargar</a><button v-if="can('editCommercial') && document.type !== 'quotation'" class="icon-danger" type="button" aria-label="Eliminar documento" @click="removeDocument(document.id)"><Trash2 :size="16" /></button></div></article>
          </div>
          <div v-if="can('editCommercial')" class="document-actions">
            <label class="button secondary" :class="{ disabled: saving }"><Upload :size="16" /> {{ order.checklist.purchaseOrder === 'attached' ? 'Reemplazar orden de compra' : 'Adjuntar orden de compra' }}<input type="file" hidden :disabled="saving" @change="uploadDocument('purchaseOrder', $event)" /></label>
            <button class="button secondary" type="button" :disabled="saving" @click="setNotApplicable('purchaseOrder', order.checklist.purchaseOrder !== 'notApplicable')">{{ order.checklist.purchaseOrder === 'notApplicable' ? 'Volver a requerir orden de compra' : 'Marcar orden de compra como no aplica' }}</button>
            <label class="button secondary" :class="{ disabled: saving }"><Upload :size="16" /> Adjuntar diseño / arte<input type="file" hidden multiple :disabled="saving" @change="uploadDocument('design', $event)" /></label>
            <button class="button secondary" type="button" :disabled="saving" @click="setNotApplicable('design', order.checklist.design !== 'notApplicable')">{{ order.checklist.design === 'notApplicable' ? 'Volver a requerir Diseño' : 'Marcar que no requiere Diseño' }}</button>
          </div>
        </section>
        <details class="panel order-wide-card operation-summary-disclosure">
          <summary>
            <div><p class="eyebrow">INFORMACIÓN CONSOLIDADA</p><h2>Resumen integral de la operación</h2><p>Consulta los datos comerciales, materiales, impresión, acabados y despacho de esta OP.</p></div>
            <span class="operation-summary-toggle"><span>Ver resumen</span><ChevronDown :size="21" /></span>
          </summary>
          <div class="operation-summary-content order-detail-grid">
        <section class="panel order-info-card"><p class="eyebrow">CLIENTE Y PEDIDO</p><h2>Datos comerciales</h2><dl class="order-data-list"><div><dt>Cliente</dt><dd>{{ text(order.commercial.clientName) }}</dd></div><div><dt>Producto</dt><dd>{{ text(order.commercial.productName) }}</dd></div><div><dt>Orden de compra</dt><dd>{{ text(order.commercial.clientPurchaseOrder) }}</dd></div><div><dt>Ciudad / País</dt><dd>{{ text(order.commercial.cityCountry) }}</dd></div><div><dt>Dirección</dt><dd>{{ text(order.commercial.address) }}</dd></div><div><dt>Valor unitario</dt><dd>{{ formatMoney(order.commercial.unitValue) }}</dd></div><div><dt>Valor total</dt><dd>{{ formatMoney(order.commercial.totalValue) }}</dd></div></dl></section>
        <section class="panel order-info-card"><p class="eyebrow">CARACTERÍSTICAS</p><h2>Trabajo solicitado</h2><dl class="order-data-list"><div><dt>Tipo</dt><dd>{{ labels[order.commercial.workType] || order.commercial.workType }}</dd></div><div><dt>Carta de color</dt><dd>{{ order.commercial.printColorProof ? 'Sí' : 'No' }}</dd></div><div><dt>Troquel</dt><dd>{{ labels[order.commercial.dieType] || order.commercial.dieType }}</dd></div><div><dt>Tamaño abierto</dt><dd>{{ text(order.commercial.openSize) }}</dd></div><div><dt>Tamaño cerrado</dt><dd>{{ text(order.commercial.closedSize) }}</dd></div></dl><div class="order-note"><strong>Observaciones</strong><p>{{ text(order.commercial.observations) }}</p><strong>Especificaciones adicionales</strong><p>{{ text(order.commercial.additionalSpecifications) }}</p></div></section>

        <section class="panel order-wide-card"><p class="eyebrow">DEFINICIONES COMERCIALES</p><h2>Materiales</h2><div class="compact-data-table"><div class="compact-data-head"><span>Material</span><span>Gramaje</span><span>Calibre</span><span>Especificación</span></div><div v-for="item in order.materials" :key="item.id"><span>{{ text(item.material) }}</span><span>{{ text(item.weight) }}</span><span>{{ text(item.caliber) }}</span><span>{{ text(item.optionalSpecifications) }}</span></div><p v-if="!order.materials.length">Sin materiales registrados.</p></div></section>
        <section class="panel order-wide-card"><h2>Impresión</h2><div class="compact-data-table"><div class="compact-data-head"><span>Producto</span><span>Tintas</span><span>Proceso</span><span>Especiales</span></div><div v-for="item in order.printLines" :key="item.id"><span>{{ text(item.product) }}</span><span>{{ text(item.inks) }}</span><span>{{ text(item.process) }}</span><span>{{ text(item.specials) }}</span></div><p v-if="!order.printLines.length">Sin líneas de impresión.</p></div></section>
        <section class="panel order-info-card"><p class="eyebrow">ACABADOS</p><h2>Especificaciones</h2><div class="finish-summary"><span v-for="item in order.finishes" :key="item.id"><strong>{{ text(item.specification) }}</strong><small>{{ [item.front && 'Frente', item.back && 'Respaldo', item.reserve && 'Reserva'].filter(Boolean).join(' · ') || 'Sin posición definida' }}</small></span><p v-if="!order.finishes.length">Sin acabados registrados.</p></div></section>
        <section class="panel order-info-card"><p class="eyebrow">DESPACHO</p><h2>Entrega</h2><dl class="order-data-list"><div><dt>Recibe</dt><dd>{{ text(order.commercial.receptionContact) }}</dd></div><div><dt>Dirección</dt><dd>{{ text(order.commercial.deliveryAddress) }}</dd></div><div><dt>Horario</dt><dd>{{ text(order.commercial.receptionSchedule) }}</dd></div><div><dt>Entrega parcial</dt><dd>{{ order.commercial.partialDelivery ? `Sí · ${formatNumber(order.commercial.partialDeliveryQuantity)}` : 'No' }}</dd></div><div><dt>Certificado</dt><dd>{{ labels[order.commercial.qualityCertificateMode] || order.commercial.qualityCertificateMode }}</dd></div><div><dt>Ficha técnica</dt><dd>{{ labels[order.commercial.technicalSheetMode] || order.commercial.technicalSheetMode }}</dd></div></dl></section>
          </div>
        </details>
      </div>

      <div v-else-if="activeTab === 'production'" class="production-tab">
        <section class="production-access-banner"><Factory :size="23" /><div><strong>Sección exclusiva de Producción</strong><p>La información comercial está protegida. Aquí se registran planeación, ejecución y calidad.</p></div><button v-if="can('editProduction') && !editingProduction" class="button secondary" type="button" @click="editingProduction = true">Editar producción</button></section>
        <form v-if="editingProduction" class="production-form" @submit.prevent="saveProduction">
          <section class="panel commercial-form-section"><p class="eyebrow">PLANEACIÓN Y CORTE</p><h2>Preparación</h2><div class="form-grid commercial-form-grid"><label>Fecha de planeación<input v-model="productionForm.planningDate" type="date" /></label><label>Encargado de planeación<input v-model="productionForm.planningManager" maxlength="180" /></label><label>Fecha de corte<input v-model="productionForm.materialCutDate" type="date" /></label><label>Encargado de corte<input v-model="productionForm.cuttingManager" maxlength="180" /></label></div><div class="production-line-list"><div v-for="(item, index) in productionForm.materials" :key="item.id" class="production-line"><strong>{{ order.materials[index]?.material || `Material ${index + 1}` }}</strong><label>Tamaño pliego<input v-model="item.sheetSize" /></label><label>Cant. pliegos<input v-model.number="item.sheetQuantity" min="0" type="number" /></label><label>Tamaño a cortar<input v-model="item.cutSize" /></label><label>Total cortada<input v-model.number="item.totalCutQuantity" min="0" type="number" /></label><label>Conformes<input v-model.number="item.conformingQuantity" min="0" type="number" /></label><label>No conformes<input v-model.number="item.nonConformingQuantity" min="0" type="number" /></label></div></div></section>
          <section class="panel commercial-form-section"><p class="eyebrow">IMPRESIÓN Y ACABADOS</p><h2>Ejecución</h2><div class="form-grid commercial-form-grid"><label>Inicio impresión · turno 1<input v-model="productionForm.printStartShift1" type="datetime-local" /></label><label>Encargado turno 1<input v-model="productionForm.printingManagerShift1" maxlength="180" /></label><label>Inicio impresión · turno 2<input v-model="productionForm.printStartShift2" type="datetime-local" /></label><label>Encargado turno 2<input v-model="productionForm.printingManagerShift2" maxlength="180" /></label><label>Inicio acabados<input v-model="productionForm.finishingStart" type="datetime-local" /></label><label>Encargado de acabados<input v-model="productionForm.finishingManager" maxlength="180" /></label></div><div class="production-line-list"><div v-for="(item, index) in productionForm.printLines" :key="item.id" class="production-line"><strong>{{ order.printLines[index]?.product || `Línea ${index + 1}` }}</strong><label>Máquina<input v-model="item.machine" /></label><label>Montaje<input v-model="item.mounting" /></label><label>Tiros<input v-model.number="item.shotsToProcess" min="0" type="number" /></label><label>Conformes<input v-model.number="item.conformingQuantity" min="0" type="number" /></label><label>No conformes<input v-model.number="item.nonConformingQuantity" min="0" type="number" /></label></div></div></section>
          <section class="panel commercial-form-section"><p class="eyebrow">TROQUELADO Y PEGADO</p><h2>Procesos finales</h2><div class="form-grid commercial-form-grid"><label>Inicio troquelado<input v-model="productionForm.dieCutStart" type="datetime-local" /></label><label>Encargado de troquelado<input v-model="productionForm.dieCutManager" /></label><label>Máquina<input v-model="productionForm.dieMachine" /></label><label>Número de troquel<input v-model="productionForm.dieNumber" /></label><label>Total procesado<input v-model.number="productionForm.dieTotalProcessed" min="0" type="number" /></label><label>Conformes<input v-model.number="productionForm.dieConforming" min="0" type="number" /></label><label>No conformes<input v-model.number="productionForm.dieNonConforming" min="0" type="number" /></label><label>Inicio pegado<input v-model="productionForm.gluingStart" type="datetime-local" /></label><label>Encargado de pegado<input v-model="productionForm.gluingManager" /></label><label>Tipo de pegado<input v-model="productionForm.glueType" /></label><label>Total pegado<input v-model.number="productionForm.glueTotalProcessed" min="0" type="number" /></label><label>Conformes<input v-model.number="productionForm.glueConforming" min="0" type="number" /></label><label>No conformes<input v-model.number="productionForm.glueNonConforming" min="0" type="number" /></label></div></section>
          <section class="panel commercial-form-section quality-section"><p class="eyebrow">CONTROL DE CALIDAD</p><h2>Revisión final</h2><div class="form-grid commercial-form-grid"><label>Fecha y hora de revisión<input v-model="productionForm.qualityReviewDate" type="datetime-local" /></label><label>Encargado de revisión<input v-model="productionForm.qualityReviewer" maxlength="180" /></label><label>Resultado<select v-model="productionForm.qualityApproved"><option :value="null">Pendiente</option><option :value="true">Aprobado</option><option :value="false">No aprobado</option></select></label><label>Observaciones<textarea v-model="productionForm.qualityNotes" rows="3" maxlength="2000" /></label></div></section>
          <div class="commercial-form-actions"><button class="button secondary" type="button" @click="editingProduction = false; fillProduction()">Cancelar</button><button class="button primary" type="submit" :disabled="saving">{{ saving ? 'Guardando…' : 'Guardar producción' }}</button></div>
        </form>
        <div v-else class="order-detail-grid"><section class="panel order-info-card"><p class="eyebrow">PLANEACIÓN</p><h2>Preparación</h2><dl class="order-data-list"><div><dt>Fecha</dt><dd>{{ formatDate(order.production.planningDate) }}</dd></div><div><dt>Encargado</dt><dd>{{ text(order.production.planningManager) }}</dd></div><div><dt>Corte</dt><dd>{{ formatDate(order.production.materialCutDate) }}</dd></div><div><dt>Encargado de corte</dt><dd>{{ text(order.production.cuttingManager) }}</dd></div></dl></section><section class="panel order-info-card"><p class="eyebrow">CALIDAD</p><h2>Revisión final</h2><dl class="order-data-list"><div><dt>Fecha</dt><dd>{{ formatDate(order.production.qualityReviewDate, true) }}</dd></div><div><dt>Responsable</dt><dd>{{ text(order.production.qualityReviewer) }}</dd></div><div><dt>Resultado</dt><dd>{{ order.production.qualityApproved == null ? 'Pendiente' : order.production.qualityApproved ? 'Aprobado' : 'No aprobado' }}</dd></div></dl><div class="order-note"><p>{{ text(order.production.qualityNotes) }}</p></div></section></div>
      </div>

    </template>
    <p v-else class="form-error">{{ errorMessage }}</p>

    <ReviewAssignmentDialog :open="confirmation === 'submitReview'" :reviewers="reviewers" :selected-id="reviewerUserId" :previous-reviewer="order?.reviewOwner ?? null" :loading="reviewersLoading" :load-error="reviewersLoadError" :submit-error="reviewSubmitError" :busy="saving" @select="reviewerUserId = $event" @retry="fetchReviewers" @close="closeReviewDialog" @confirm="submitReview" />
    <ConfirmDialog :open="confirmation === 'approveReview'" title="Aprobar paquete comercial" :description="`Se asignará el pedido ${reviewCustomerOrderNumber.trim()} y la orden quedará lista para que Producción la reciba.`" confirm-label="Aprobar y enviar" @close="confirmation = null" @confirm="runAction('approveReview')" />
    <ConfirmDialog :open="confirmation === 'receive'" title="Recibir orden" description="La orden cambiará a En producción y quedará asociada a tu usuario." confirm-label="Recibir orden" @close="confirmation = null" @confirm="runAction('receive')" />
    <ConfirmDialog :open="confirmation === 'complete'" title="Finalizar orden" description="La revisión de calidad debe estar completa y aprobada." confirm-label="Finalizar orden" @close="confirmation = null" @confirm="runAction('complete')" />
    <ConfirmDialog :open="discardOpen" title="Descartar borrador" description="Esta OP todavía no fue enviada. Al volver se eliminará el borrador y sus documentos; esta acción no se puede deshacer." confirm-label="Descartar y volver" tone="danger" @close="discardOpen = false" @confirm="discardDraft" />
    <Teleport to="body"><div v-if="cancellationOpen" class="dialog-backdrop" @click.self="cancellationOpen = false"><form class="dialog-card" @submit.prevent="cancelOrder"><p class="eyebrow">ANULAR ORDEN</p><h2>Indica el motivo</h2><p>La orden se conservará para auditoría y no podrá continuar su flujo.</p><label>Motivo<textarea v-model="cancellationReason" required maxlength="500" rows="4" /></label><div class="dialog-actions"><button class="button secondary" type="button" @click="cancellationOpen = false">Volver</button><button class="button danger" type="submit" :disabled="saving">Anular orden</button></div></form></div></Teleport>
    <Teleport to="body"><div v-if="returnOpen" class="dialog-backdrop" @click.self="returnOpen = false"><form class="dialog-card" @submit.prevent="returnForCorrection"><p class="eyebrow">DEVOLVER A COMERCIAL</p><h2>Indica la corrección</h2><p>El agente responsable recibirá la orden y una notificación por correo.</p><label>Motivo<textarea v-model="returnReason" required maxlength="1000" rows="5" /></label><div class="dialog-actions"><button class="button secondary" type="button" @click="returnOpen = false">Cancelar</button><button class="button danger" type="submit" :disabled="saving">Devolver orden</button></div></form></div></Teleport>
  </AppLayout>
</template>
