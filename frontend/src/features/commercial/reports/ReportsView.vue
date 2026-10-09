<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, FileSpreadsheet, FolderOpen, Upload, X } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { useAuthStore } from '../../../stores/auth'
import { reportService } from './reportService'
import WorkspacePagination from './WorkspacePagination.vue'
import type { ReportSummary } from './types'
import './workspace.css'
const auth = useAuthStore(), router = useRouter(), input = ref<HTMLInputElement | null>(null)
const reports = ref<ReportSummary[]>([]), file = ref<File | null>(null), busy = ref(false), loading = ref(true)
const error = ref(''), fileError = ref(''), search = ref(''), page = ref(1), dragging = ref(false)
const filtered = computed(() => reports.value.filter(report => report.name.toLowerCase().includes(search.value.toLowerCase())))
const visible = computed(() => filtered.value.slice((page.value - 1) * 5, page.value * 5))
watch(search, () => { page.value = 1 })
function choose(selected: File | null) {
  if (busy.value) return
  file.value = selected; error.value = ''
  fileError.value = selected && (!selected.name.toLowerCase().endsWith('.xlsx') || selected.size > 10 * 1024 * 1024)
    ? 'Selecciona un archivo Excel .xlsx de hasta 10 MB.' : ''
}
function drop(event: DragEvent) { dragging.value = false; choose(event.dataTransfer?.files?.[0] ?? null) }
async function prepare() {
  if (!file.value || busy.value || fileError.value) return
  busy.value = true; error.value = ''
  try { const report = await reportService.create(file.value); await router.push(`/commercial/reports/ventas/${report.id}`) }
  catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible preparar el reporte. Tu archivo sigue seleccionado.' }
  finally { busy.value = false }
}
onMounted(async () => { try { reports.value = await reportService.list() } catch (e) { error.value = e instanceof Error ? e.message : 'No fue posible cargar los reportes guardados.' } finally { loading.value = false } })
</script>
<template>
  <AppLayout>
    <div class="rw-workspace">
      <PageHeader eyebrow="COMERCIAL · REPORTES" title="Informe Mensual" description="Adjunta el informe de ventas y obtén tu Excel de VENTAS MES." />
      <p v-if="error" class="rw-error" role="alert">{{ error }}</p>
      <section v-if="auth.hasPermission('commercial.reports.edit')" class="rw-upload-surface" aria-labelledby="rw-upload-title" :aria-busy="busy" :class="{ dragging }" @dragover.prevent="dragging = true" @dragleave.prevent="dragging = false" @drop.prevent="drop">
        <div class="rw-upload-heading"><span class="rw-upload-symbol"><Upload :size="27" /></span><div><h2 id="rw-upload-title">Informe de ventas</h2><p>Excel de Manager · .xlsx · Hasta 10 MB</p></div></div>
        <div class="rw-upload-selection"><input ref="input" id="rw-manager-file" type="file" accept=".xlsx" hidden :disabled="busy" @change="choose(($event.target as HTMLInputElement).files?.[0] ?? null)" /><button type="button" class="rw-button rw-button-outlined" :disabled="busy" @click="input?.click()"><FolderOpen :size="18" />{{ file ? 'Cambiar archivo' : 'Seleccionar Excel' }}</button><div v-if="file" class="rw-selected-file"><FileSpreadsheet :size="20" /><span :title="file.name">{{ file.name }}</span><button type="button" class="rw-icon-button" :disabled="busy" aria-label="Quitar archivo seleccionado" @click="choose(null); if (input) input.value = ''"><X :size="17" /></button></div><span v-else class="rw-upload-placeholder">También puedes arrastrarlo aquí</span></div>
        <p v-if="fileError" class="rw-error" role="alert">{{ fileError }}</p>
        <div class="rw-prepare-action"><button type="button" class="rw-button rw-button-primary" :disabled="!file || busy || !!fileError" @click="prepare">{{ busy ? 'Preparando reporte…' : 'Preparar reporte' }}<ArrowRight v-if="!busy" :size="17" /></button><div v-if="busy" class="rw-preparing" role="status"><progress aria-label="Preparando el reporte" />Estamos preparando la tabla final. Espera un momento.</div></div>
      </section>
      <section class="rw-saved-surface" aria-labelledby="rw-saved-title"><header><h2 id="rw-saved-title">Reportes guardados</h2><label class="rw-saved-search"><span class="sr-only">Buscar reporte guardado</span><input v-model="search" placeholder="Buscar reporte" /></label></header><p v-if="loading" role="status" class="rw-empty">Cargando reportes…</p><p v-else-if="!visible.length" class="rw-empty">{{ search ? 'No hay reportes con ese nombre.' : 'Aquí aparecerán los reportes que prepares.' }}</p><RouterLink v-for="report in visible" :key="report.id" class="rw-saved-report" :to="`/commercial/reports/ventas/${report.id}`"><span class="rw-file-icon"><FileSpreadsheet :size="20" /></span><span><strong>{{ report.name }}</strong><small>Guardado {{ new Date(report.updatedAt).toLocaleString('es-CO') }}</small></span><ArrowRight :size="18" /></RouterLink><WorkspacePagination v-if="!loading" v-model="page" :total="filtered.length" label="Páginas de reportes guardados" /></section>
    </div>
  </AppLayout>
</template>
