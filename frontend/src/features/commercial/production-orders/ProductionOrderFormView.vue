<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { CirclePlus, FileSpreadsheet, PackageCheck, Save, Trash2, UploadCloud, UserRound, WandSparkles } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { ApiError } from '../../../api/httpClient'
import { productionOrdersService } from './productionOrdersService'
import { commercialDraftFromQuotation } from './quotationDraft'
import type { CommercialFinishInput, CommercialMaterialInput, CommercialOrderInput, CommercialPrintLineInput, ProductionOrderDetail, QuotationPreview } from './types'

const route = useRoute()
const router = useRouter()
const id = computed(() => typeof route.params.id === 'string' ? route.params.id : undefined)
const relatedOrderId = computed(() => !id.value && typeof route.query.relatedTo === 'string' ? route.query.relatedTo : undefined)
const relatedOrder = ref<ProductionOrderDetail>()
const cancelTarget = computed(() => relatedOrder.value ? `/commercial/production-orders/${relatedOrder.value.id}` : '/commercial/production-orders')
const loading = ref(Boolean(id.value))
const saving = ref(false)
const errorMessage = ref('')
const quotationFile = ref<File>()
const preview = ref<QuotationPreview>()
const selectedItem = ref(0)
const selectedOption = ref(0)
const reviewingQuotation = ref(false)
const quotationReady = ref(false)

const emptyMaterial = (): CommercialMaterialInput => ({ material: '', weight: '', caliber: '', optionalSpecifications: '' })
const emptyPrintLine = (): CommercialPrintLineInput => ({ product: '', inks: '', process: '', specials: '' })
const emptyFinish = (): CommercialFinishInput => ({ specification: '', front: false, back: false, reserve: false })

const form = reactive<CommercialOrderInput>({
  quotationNumber: '', deliveryDate: null, clientName: '', productName: '', referenceNumber: '', clientPurchaseOrder: '',
  quantity: null, unitValue: null, cityCountry: '', address: '', workType: 'unspecified', printColorProof: false, dieType: 'none',
  openSize: '', closedSize: '', observations: '', additionalSpecifications: '', receptionContact: '', deliveryAddress: '',
  receptionSchedule: '', partialDelivery: false, partialDeliveryQuantity: null, legalContractRequirements: '', dispatchDay: null,
  qualityCertificateMode: 'none', technicalSheetMode: 'none', materials: [emptyMaterial()], printLines: [emptyPrintLine()], finishes: [emptyFinish()],
})

const totalValue = computed(() => form.quantity != null && form.unitValue != null ? form.quantity * form.unitValue : null)

function loadDetail(order: ProductionOrderDetail) {
  quotationReady.value = order.checklist.quotationReady
  const c = order.commercial
  Object.assign(form, {
    version: order.version,
    quotationNumber: c.quotationNumber ?? '', deliveryDate: c.deliveryDate, clientName: c.clientName ?? '', productName: c.productName ?? '',
    referenceNumber: c.referenceNumber ?? '', clientPurchaseOrder: c.clientPurchaseOrder ?? '', quantity: c.quantity, unitValue: c.unitValue,
    cityCountry: c.cityCountry ?? '', address: c.address ?? '', workType: c.workType, printColorProof: c.printColorProof, dieType: c.dieType,
    openSize: c.openSize ?? '', closedSize: c.closedSize ?? '', observations: c.observations ?? '', additionalSpecifications: c.additionalSpecifications ?? '',
    receptionContact: c.receptionContact ?? '', deliveryAddress: c.deliveryAddress ?? '', receptionSchedule: c.receptionSchedule ?? '',
    partialDelivery: c.partialDelivery, partialDeliveryQuantity: c.partialDeliveryQuantity, legalContractRequirements: c.legalContractRequirements ?? '',
    dispatchDay: c.dispatchDay, qualityCertificateMode: c.qualityCertificateMode, technicalSheetMode: c.technicalSheetMode,
    materials: order.materials.length ? order.materials.map((item) => ({ material: item.material ?? '', weight: item.weight ?? '', caliber: item.caliber ?? '', optionalSpecifications: item.optionalSpecifications ?? '' })) : [emptyMaterial()],
    printLines: order.printLines.length ? order.printLines.map((item) => ({ product: item.product ?? '', inks: item.inks ?? '', process: item.process ?? '', specials: item.specials ?? '' })) : [emptyPrintLine()],
    finishes: order.finishes.length ? order.finishes.map((item) => ({ specification: item.specification ?? '', front: item.front, back: item.back, reserve: item.reserve })) : [emptyFinish()],
  })
}

onMounted(async () => {
  if (!id.value && !relatedOrderId.value) return
  try {
    if (relatedOrderId.value) {
      relatedOrder.value = await productionOrdersService.get(relatedOrderId.value)
      return
    }
    const order = await productionOrdersService.get(id.value!)
    if (!order.allowedActions.includes('editCommercial')) {
      await router.replace(`/commercial/production-orders/${order.id}`)
      return
    }
    loadDetail(order)
  } catch (error) { errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar la orden.' }
  finally { loading.value = false }
})

async function submit() {
  saving.value = true
  errorMessage.value = ''
  try {
    const saved = id.value
      ? await productionOrdersService.updateCommercial(id.value, form)
      : quotationFile.value && preview.value
        ? await productionOrdersService.importQuotation(quotationFile.value, selectedItem.value, selectedOption.value, form, relatedOrderId.value)
        : await productionOrdersService.create(form)
    await router.push(`/commercial/production-orders/${saved.id}`)
  } catch (error) { errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible guardar la orden.' }
  finally { saving.value = false }
}

async function selectQuotation(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  quotationFile.value = file
  preview.value = undefined
  selectedItem.value = 0
  selectedOption.value = 0
  saving.value = true; errorMessage.value = ''
  try { preview.value = await productionOrdersService.previewQuotation(file) }
  catch (error) { errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible leer la cotización.' }
  finally { saving.value = false }
}

function continueFromQuotation() {
  if (!quotationFile.value || !preview.value) return
  try {
    Object.assign(form, commercialDraftFromQuotation(preview.value, selectedItem.value, selectedOption.value))
    reviewingQuotation.value = true
    errorMessage.value = ''
  } catch (error) { errorMessage.value = error instanceof Error ? error.message : 'No fue posible preparar la orden desde la cotización.' }
}

function returnToQuotation() {
  reviewingQuotation.value = false
  errorMessage.value = ''
}

async function replaceQuotation() {
  if (!id.value || !quotationFile.value || !preview.value || form.version == null) return
  saving.value = true; errorMessage.value = ''
  try {
    const saved = await productionOrdersService.replaceQuotation(id.value, form.version, quotationFile.value, selectedItem.value, selectedOption.value)
    loadDetail(saved); quotationFile.value = undefined; preview.value = undefined
  } catch (error) { errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible reemplazar la cotización.' }
  finally { saving.value = false }
}

function removeRow<T>(rows: T[], index: number, empty: () => T) {
  rows.splice(index, 1)
  if (!rows.length) rows.push(empty())
}

function money(value: number | null) {
  return value == null ? '—' : new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value)
}
function quantity(value: number) { return new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(value) }
</script>

<template>
  <AppLayout>
    <button v-if="!id && reviewingQuotation" class="back-link commercial-back-button" type="button" @click="returnToQuotation">← Volver a la selección</button>
    <RouterLink v-else class="back-link" :to="id ? `/commercial/production-orders/${id}` : cancelTarget">← Volver</RouterLink>
    <PageHeader eyebrow="COMERCIAL · ORDEN DE PRODUCCIÓN" :title="id ? 'Editar borrador' : reviewingQuotation ? 'Revisar orden antes de crear' : 'Nueva orden'" description="Diligencia la información comercial. Producción tendrá su propia sección después del envío." />
    <section v-if="relatedOrder" class="related-order-context">
      <div><p class="eyebrow">OPERACIÓN EN CURSO</p><strong>{{ relatedOrder.code }} ya está guardada</strong><span>{{ relatedOrder.commercial.productName || 'Producto pendiente' }} · {{ quantity(relatedOrder.commercial.quantity || 0) }} unidades</span></div>
      <RouterLink class="button secondary compact" :to="`/commercial/production-orders/${relatedOrder.id}`">Ver OP anterior</RouterLink>
    </section>
    <div v-if="loading" class="panel commercial-empty">Cargando borrador…</div>
    <section v-else-if="!id && !reviewingQuotation" class="quotation-import-layout">
      <div class="panel quotation-import-card">
        <div class="commercial-form-heading"><span><UploadCloud :size="21" /></span><div><p class="eyebrow">PASO 1</p><h2>Cargar cotización aprobada</h2><p>Admite PDF, XLSX y XLS. El archivo original se conservará junto con su huella de integridad.</p></div></div>
        <label class="quotation-drop"><FileSpreadsheet :size="32" /><strong>{{ quotationFile?.name || 'Selecciona la cotización' }}</strong><small>Máximo 25 MB · sin macros</small><input type="file" accept=".pdf,.xlsx,.xls" @change="selectQuotation" /></label>
        <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
      </div>
      <div v-if="preview" class="panel quotation-preview-card">
        <p class="eyebrow">PASO 2 · PREVISUALIZACIÓN</p><h2>Selecciona la referencia y cantidad</h2>
        <p>Selecciona una referencia para preparar la OP. En este paso todavía no se creará ningún registro.</p>
        <div class="quotation-meta"><span><small>Cotización</small><strong>{{ preview.quotationNumber || 'No identificada' }}</strong></span><span><small>Cliente</small><strong>{{ preview.clientName || 'Por completar' }}</strong></span><span><small>Formato</small><strong>{{ preview.format.toUpperCase() }}</strong></span></div>
        <label>Producto<select v-model.number="selectedItem" @change="selectedOption = 0"><option v-for="item in preview.items" :key="item.index" :value="item.index">{{ item.productName || item.description }}</option></select></label>
        <label>Opción de cantidad<select v-model.number="selectedOption"><option v-for="(option, index) in preview.items[selectedItem]?.options || []" :key="index" :value="index">{{ quantity(option.quantity) }} unidades · {{ money(option.unitValue) }} c/u</option></select></label>
        <ul v-if="preview.warnings.length" class="quotation-warnings"><li v-for="warning in preview.warnings" :key="warning">{{ warning }}</li></ul>
        <div class="commercial-form-actions"><RouterLink class="button secondary" :to="cancelTarget">Cancelar</RouterLink><button class="button primary" type="button" :disabled="saving" @click="continueFromQuotation"><WandSparkles :size="18" /> Continuar y revisar datos</button></div>
      </div>
    </section>
    <form v-else class="commercial-form-layout" @submit.prevent="submit">
      <aside class="commercial-form-guide">
        <span class="guide-number">01</span><strong>Información comercial</strong><p>Los campos marcados con * serán necesarios para enviar la orden.</p>
        <nav><a href="#client">Cliente y pedido</a><a href="#characteristics">Características</a><a href="#production-specs">Especificaciones</a><a href="#dispatch">Despacho</a></nav>
        <div class="commercial-form-total"><small>Valor estimado</small><strong>{{ money(totalValue) }}</strong></div>
      </aside>
      <div class="commercial-form-content">
        <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>

        <section v-if="id" class="panel quotation-replace-card">
          <div><p class="eyebrow">{{ quotationReady ? 'COTIZACIÓN DE ORIGEN' : 'COTIZACIÓN PENDIENTE' }}</p><h2>{{ form.quotationNumber || 'Adjunta la cotización de esta OP' }}</h2><p>{{ quotationReady ? 'Si reemplazas el archivo se repetirá la importación y se conservará el historial del documento anterior.' : 'Las órdenes duplicadas no reutilizan la cotización anterior. Selecciona el PDF o Excel correspondiente a esta nueva venta.' }}</p></div>
          <label class="button secondary"><UploadCloud :size="17" /> {{ quotationReady ? 'Seleccionar reemplazo PDF/Excel' : 'Seleccionar cotización PDF/Excel' }}<input type="file" hidden accept=".pdf,.xlsx,.xls" @change="selectQuotation" /></label>
          <div v-if="preview" class="quotation-replace-selection"><label>Producto<select v-model.number="selectedItem" @change="selectedOption = 0"><option v-for="item in preview.items" :key="item.index" :value="item.index">{{ item.productName || item.description }}</option></select></label><label>Cantidad<select v-model.number="selectedOption"><option v-for="(option, index) in preview.items[selectedItem]?.options || []" :key="index" :value="index">{{ quantity(option.quantity) }} · {{ money(option.unitValue) }} c/u</option></select></label><button class="button primary" type="button" :disabled="saving" @click="replaceQuotation">{{ quotationReady ? 'Reimportar cotización' : 'Adjuntar e importar cotización' }}</button></div>
        </section>
        <section v-else class="panel quotation-replace-card quotation-draft-notice">
          <div><p class="eyebrow">VISTA PREVIA SIN GUARDAR</p><h2>{{ form.quotationNumber || 'Cotización importada' }}</h2><p>Revisa y completa la información. La OP solo se creará cuando pulses <strong>Guardar borrador</strong>. Volver o cancelar no creará ningún registro.</p></div>
        </section>

        <section id="client" class="panel commercial-form-section">
          <div class="commercial-form-heading"><span><UserRound :size="20" /></span><div><p class="eyebrow">DATOS PRINCIPALES</p><h2>Cliente y pedido</h2><p>Identifica la operación que será entregada a Producción.</p></div></div>
          <div class="form-grid commercial-form-grid">
            <label><span class="field-label">Número de cotización</span><input v-model="form.quotationNumber" maxlength="100" /></label>
            <label><span class="field-label">Fecha de entrega <em>*</em></span><input v-model="form.deliveryDate" type="date" /></label>
            <label><span class="field-label">Nombre del cliente <em>*</em></span><input v-model="form.clientName" maxlength="180" /></label>
            <label><span class="field-label">Producto <em>*</em></span><input v-model="form.productName" maxlength="180" /></label>
            <label><span class="field-label">Orden de compra del cliente</span><input v-model="form.clientPurchaseOrder" maxlength="100" /></label>
            <label><span class="field-label">Cantidad ordenada <em>*</em></span><input v-model.number="form.quantity" min="0" step="0.01" type="number" /></label>
            <label><span class="field-label">Valor unitario</span><input v-model.number="form.unitValue" min="0" step="0.01" type="number" /></label>
            <label><span class="field-label">Ciudad / País</span><input v-model="form.cityCountry" maxlength="180" placeholder="Opcional" /></label>
            <label><span class="field-label">Dirección</span><input v-model="form.address" maxlength="500" placeholder="Opcional" /></label>
          </div>
        </section>

        <section id="characteristics" class="panel commercial-form-section">
          <div class="commercial-form-heading"><span><WandSparkles :size="20" /></span><div><p class="eyebrow">CARACTERÍSTICAS</p><h2>Configuración del trabajo</h2><p>Define lo que Producción debe conocer desde el inicio.</p></div></div>
          <div class="form-grid commercial-form-grid">
            <label><span class="field-label">Tipo de trabajo <em>*</em></span><select v-model="form.workType"><option value="unspecified">Seleccionar</option><option value="new">Trabajo nuevo</option><option value="change">Cambio</option><option value="repeat">Repetición</option><option value="replacement">Reposición</option></select></label>
            <label><span class="field-label">Troquel</span><select v-model="form.dieType"><option value="none">No aplica</option><option value="existing">Existente</option><option value="new">Nuevo</option></select></label>
            <label><span class="field-label">Tamaño abierto</span><input v-model="form.openSize" maxlength="100" placeholder="Largo × ancho" /></label>
            <label><span class="field-label">Tamaño cerrado</span><input v-model="form.closedSize" maxlength="100" placeholder="Largo × ancho × alto" /></label>
          </div>
          <label class="commercial-check"><input v-model="form.printColorProof" type="checkbox" /><span><strong>Imprimir carta de color</strong><small>Producción verá esta instrucción al recibir la orden.</small></span></label>
          <div class="form-grid commercial-form-grid">
            <label><span class="field-label">Observaciones</span><textarea v-model="form.observations" maxlength="2000" rows="4" /></label>
            <label><span class="field-label">Especificaciones adicionales</span><textarea v-model="form.additionalSpecifications" maxlength="2000" rows="4" placeholder="Opcional" /></label>
          </div>
        </section>

        <section id="production-specs" class="panel commercial-form-section">
          <div class="commercial-form-heading"><span><PackageCheck :size="20" /></span><div><p class="eyebrow">ESPECIFICACIONES PARA PRODUCCIÓN</p><h2>Materiales, impresión y acabados</h2><p>Registra únicamente las definiciones comerciales. Los resultados productivos se completarán después.</p></div></div>

          <div class="repeatable-heading"><h3>Materiales</h3><button class="button commercial-quiet" type="button" @click="form.materials.push(emptyMaterial())"><CirclePlus :size="17" /> Agregar material</button></div>
          <div class="repeatable-list">
            <div v-for="(item, index) in form.materials" :key="index" class="repeatable-row four-columns">
              <label>Material<input v-model="item.material" maxlength="180" /></label><label>Gramaje<input v-model="item.weight" maxlength="80" /></label><label>Calibre<input v-model="item.caliber" maxlength="80" /></label><label>Especificación opcional<input v-model="item.optionalSpecifications" maxlength="500" /></label>
              <button class="icon-danger" type="button" aria-label="Eliminar material" @click="removeRow(form.materials, index, emptyMaterial)"><Trash2 :size="17" /></button>
            </div>
          </div>

          <div class="repeatable-heading"><h3>Impresión</h3><button class="button commercial-quiet" type="button" @click="form.printLines.push(emptyPrintLine())"><CirclePlus :size="17" /> Agregar línea</button></div>
          <div class="repeatable-list">
            <div v-for="(item, index) in form.printLines" :key="index" class="repeatable-row four-columns">
              <label>Producto<input v-model="item.product" maxlength="180" /></label><label>Tintas<input v-model="item.inks" maxlength="300" /></label><label>Proceso<input v-model="item.process" maxlength="300" /></label><label>Especiales<input v-model="item.specials" maxlength="300" /></label>
              <button class="icon-danger" type="button" aria-label="Eliminar línea de impresión" @click="removeRow(form.printLines, index, emptyPrintLine)"><Trash2 :size="17" /></button>
            </div>
          </div>

          <div class="repeatable-heading"><h3>Acabados</h3><button class="button commercial-quiet" type="button" @click="form.finishes.push(emptyFinish())"><CirclePlus :size="17" /> Agregar acabado</button></div>
          <div class="repeatable-list">
            <div v-for="(item, index) in form.finishes" :key="index" class="repeatable-row finish-row">
              <label>Especificación<input v-model="item.specification" maxlength="300" placeholder="Ej. Troquelado" /></label>
              <label class="mini-check"><input v-model="item.front" type="checkbox" /> Frente</label><label class="mini-check"><input v-model="item.back" type="checkbox" /> Respaldo</label><label class="mini-check"><input v-model="item.reserve" type="checkbox" /> Reserva</label>
              <button class="icon-danger" type="button" aria-label="Eliminar acabado" @click="removeRow(form.finishes, index, emptyFinish)"><Trash2 :size="17" /></button>
            </div>
          </div>
        </section>

        <section id="dispatch" class="panel commercial-form-section">
          <div class="commercial-form-heading"><span><PackageCheck :size="20" /></span><div><p class="eyebrow">DESPACHO</p><h2>Entrega y requisitos</h2><p>Dirección y especificaciones pueden dejarse vacías cuando no apliquen.</p></div></div>
          <div class="form-grid commercial-form-grid">
            <label><span class="field-label">Encargado de recibir</span><input v-model="form.receptionContact" maxlength="180" /></label>
            <label><span class="field-label">Dirección de entrega</span><input v-model="form.deliveryAddress" maxlength="500" placeholder="Opcional" /></label>
            <label><span class="field-label">Horario de recepción</span><input v-model="form.receptionSchedule" maxlength="180" /></label>
            <label><span class="field-label">Día de despacho</span><input v-model="form.dispatchDay" type="date" /></label>
            <label><span class="field-label">Certificado de calidad</span><select v-model="form.qualityCertificateMode"><option value="none">No aplica</option><option value="physical">Físico</option><option value="digital">Digital</option><option value="both">Físico y digital</option></select></label>
            <label><span class="field-label">Ficha técnica</span><select v-model="form.technicalSheetMode"><option value="none">No aplica</option><option value="physical">Física</option><option value="digital">Digital</option><option value="both">Física y digital</option></select></label>
          </div>
          <label class="commercial-check"><input v-model="form.partialDelivery" type="checkbox" /><span><strong>Permitir entrega parcial</strong><small>Activa este campo solo si la orden admite despachos parciales.</small></span></label>
          <label v-if="form.partialDelivery" class="compact-number"><span class="field-label">Cantidad de entrega parcial</span><input v-model.number="form.partialDeliveryQuantity" min="0" step="0.01" type="number" /></label>
          <label><span class="field-label">Requisitos legales y contractuales</span><textarea v-model="form.legalContractRequirements" maxlength="2000" rows="3" /></label>
        </section>

        <div class="commercial-form-actions"><RouterLink class="button secondary" :to="id ? `/commercial/production-orders/${id}` : cancelTarget">Cancelar</RouterLink><button class="button primary large" type="submit" :disabled="saving"><Save :size="18" /> {{ saving ? 'Guardando…' : 'Guardar borrador' }}</button></div>
      </div>
    </form>
  </AppLayout>
</template>
