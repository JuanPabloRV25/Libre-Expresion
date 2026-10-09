<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { Download, Pencil, Plus, Save, Search, Upload, X } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { useAuthStore } from '../../../stores/auth'
import { reportService } from './reportService'
import WorkspacePagination from './WorkspacePagination.vue'
import type { OpRecord } from './types'
import './workspace.css'
const auth = useAuthStore(), records = ref<OpRecord[]>([]), search = ref(''), page = ref(1)
const loading = ref(true), error = ref(''), message = ref(''), importing = ref(false), saving = ref(false)
const fileInput = ref<HTMLInputElement | null>(null), dialog = ref<HTMLDialogElement | null>(null)
const selected = ref<OpRecord | null>(null), editing = ref(false), cells = ref<string[]>(Array(21).fill('')), editorError = ref('')
const canEdit = computed(() => auth.hasPermission('commercial.reports.edit'))
const labels = ['OP', 'F. INGRESO', 'COTIZACION', 'OC', 'NIT', 'F · CLIENTE', 'G · REFERENCIA', 'LINEA DE PRODUCCION', 'CODIGO PRODUCTO', 'PRODUCTO', 'MATERIAL', 'TINTAS', 'TAMAÑO', 'TERMINADOS', 'TIPO', 'F. ENTREGA', 'CÓD VENDEDOR', 'VENDEDOR', 'CANTIDAD', 'VALOR UNITARIO', 'VALOR TOTAL']
const filtered = computed(() => records.value.filter(record => `${record.number} ${record.code} ${record.data.cells.join(' ')}`.toLowerCase().includes(search.value.toLowerCase())))
const visible = computed(() => filtered.value.slice((page.value - 1) * 5, page.value * 5))
watch(search, () => { page.value = 1 })
watch(filtered, () => { page.value = Math.min(page.value, Math.max(1, Math.ceil(filtered.value.length / 5))) })
async function load() { loading.value = true; try { records.value = await reportService.ops() } catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible consultar el histórico.' } finally { loading.value = false } }
async function importHistory(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file || importing.value) return
  importing.value = true; error.value = ''; message.value = ''
  try { const result = await reportService.importOps(file); message.value = result.message; await load() }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible incorporar el histórico.' }
  finally { importing.value = false; (event.target as HTMLInputElement).value = '' }
}
async function open(record: OpRecord | null) {
  selected.value = record; editing.value = !record; editorError.value = ''
  cells.value = Array.from({ length: 21 }, (_, index) => record?.data.cells[index] ?? '')
  await nextTick(); dialog.value?.showModal()
}
function close() { if (!saving.value) dialog.value?.close() }
async function save() {
  if (saving.value) return
  saving.value = true; editorError.value = ''
  try {
    const result = selected.value ? await reportService.saveOp(selected.value, cells.value) : await reportService.createOp(cells.value)
    selected.value = result; message.value = `OP ${result.number} guardada en el histórico.`; editing.value = false
    dialog.value?.close(); await load()
  } catch (e) { editorError.value = e instanceof Error ? e.message : 'No fue posible guardar. Los datos que escribiste siguen aquí.' }
  finally { saving.value = false }
}
onMounted(load)
</script>
<template>
  <AppLayout>
    <div class="rw-workspace">
      <PageHeader eyebrow="COMERCIAL · REPORTES" title="Informe OPs" description="Histórico de las órdenes de producción y sus datos registrados."><a v-if="auth.hasPermission('commercial.reports.export')" class="rw-button rw-button-primary" :href="reportService.opsUrl"><Download :size="17" /> Descargar histórico</a></PageHeader>
      <p v-if="error" class="rw-error" role="alert">{{ error }}</p><p v-if="message" class="rw-success" role="status">{{ message }}</p>
      <section class="rw-result-surface" aria-label="Registro histórico de OPs"><div class="rw-registry-tools"><label class="rw-search"><Search :size="18" /><input v-model="search" placeholder="Buscar OP, cliente o referencia" aria-label="Buscar en el histórico de OPs" /></label><button v-if="canEdit" type="button" class="rw-button rw-button-outlined" @click="open(null)"><Plus :size="17" /> Registrar OP</button></div><p v-if="loading" role="status" class="rw-empty">Cargando histórico…</p><p v-else-if="!visible.length" class="rw-empty">{{ search ? 'No hay OPs que coincidan con tu búsqueda.' : 'Todavía no hay OPs registradas. Puedes registrar una OP o incorporar tu histórico.' }}</p><div v-else class="rw-table-scroll rw-registry-adaptive" role="region" aria-label="Tabla del Informe de OPs"><table class="rw-registry-table"><thead><tr><th>OP</th><th>F · CLIENTE</th><th>G · REFERENCIA</th><th>Procedencia</th><th><span class="sr-only">Acciones</span></th></tr></thead><tbody><tr v-for="record in visible" :key="record.id"><td data-label="OP"><strong>{{ record.number || 'Por completar' }}</strong><small v-if="record.code">{{ record.code }}</small></td><td data-label="F · CLIENTE">{{ record.data.cells[5] || '—' }}</td><td data-label="G · REFERENCIA">{{ record.data.cells[6] || '—' }}</td><td data-label="Procedencia">{{ record.origin }}<small>{{ new Date(record.capturedAt).toLocaleDateString('es-CO') }}</small></td><td><button type="button" class="rw-button rw-button-subtle" @click="open(record)">Ver datos</button></td></tr></tbody></table></div><WorkspacePagination v-if="!loading" v-model="page" :total="filtered.length" label="Páginas del Informe de OPs" /></section>
      <details v-if="canEdit" class="rw-registry-import"><summary>Incorporar Informe de OPs desde Excel</summary><div><p class="rw-small-note">Selecciona el histórico con sus columnas originales. Los registros ya incorporados se reconocerán al volver a cargarlo.</p><input ref="fileInput" type="file" accept=".xlsx" hidden :disabled="importing" @change="importHistory" /><button type="button" class="rw-button rw-button-outlined" :disabled="importing" @click="fileInput?.click()"><Upload :size="17" />{{ importing ? 'Incorporando histórico…' : 'Seleccionar Excel de OPs' }}</button></div></details>
    </div>
    <Teleport to="body"><dialog ref="dialog" class="rw-op-dialog" aria-labelledby="rw-op-title" @cancel.prevent="close"><header class="rw-editor-heading"><div><p>INFORME DE OPS</p><h2 id="rw-op-title">{{ selected ? `OP ${selected.number || 'por completar'}` : 'Registrar OP' }}</h2><span v-if="selected">{{ selected.origin }} · {{ new Date(selected.capturedAt).toLocaleString('es-CO') }}</span></div><button type="button" class="rw-icon-button" aria-label="Cerrar datos de OP" :disabled="saving" @click="close"><X :size="21" /></button></header><div class="rw-editor-body"><p v-if="editorError" class="rw-error" role="alert">{{ editorError }}</p><form v-if="editing" id="rw-op-form" @submit.prevent="save"><div class="rw-edit-fields"><label v-for="(label, index) in labels" :key="label"><span>{{ label }}</span><input v-model="cells[index]" :disabled="saving" :required="index === 0" /></label></div></form><dl v-else class="rw-op-data"><div v-for="(label, index) in labels" :key="label"><dt>{{ label }}</dt><dd>{{ cells[index] || '—' }}</dd></div></dl></div><footer class="rw-editor-footer"><button type="button" class="rw-button rw-button-subtle" :disabled="saving" @click="close">{{ editing ? 'Cancelar' : 'Cerrar' }}</button><button v-if="!editing && canEdit" type="button" class="rw-button rw-button-outlined" @click="editing = true"><Pencil :size="17" /> Editar registro</button><button v-if="editing && canEdit" type="submit" form="rw-op-form" class="rw-button rw-button-primary" :disabled="saving"><Save :size="17" />{{ saving ? 'Guardando…' : 'Guardar registro' }}</button></footer></dialog></Teleport>
  </AppLayout>
</template>
