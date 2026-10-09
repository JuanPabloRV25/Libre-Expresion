<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../../../layouts/AppLayout.vue'
import { reportService } from './reportService'
import ReportWorkspace from './ReportWorkspace.vue'
import SalesReportView from './SalesReportView.vue'
import type { SalesReport } from './types'
const route = useRoute(), report = ref<SalesReport | null>(null), error = ref(''), loading = ref(true)
let loadEpoch = 0
watch(() => String(route.params.id), async id => {
  const epoch = ++loadEpoch
  loading.value = true; error.value = ''; report.value = null
  try { const loaded = await reportService.get(id); if (epoch === loadEpoch) report.value = loaded }
  catch (e) { if (epoch === loadEpoch) error.value = e instanceof Error ? e.message : 'No fue posible abrir el reporte.' }
  finally { if (epoch === loadEpoch) loading.value = false }
}, { immediate: true })
</script>
<template>
  <AppLayout v-if="loading || error"><p v-if="loading" role="status">Abriendo reporte…</p><div v-if="error"><p class="form-error" role="alert">{{ error }}</p><RouterLink class="button secondary" to="/commercial/reports/monthly">Volver a Informe Mensual</RouterLink></div></AppLayout>
  <ReportWorkspace v-else-if="report?.data.preparation" :key="report.id" :initial-report="report" />
  <SalesReportView v-else-if="report" :key="report.id" />
</template>
