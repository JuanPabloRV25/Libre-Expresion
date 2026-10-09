<script setup lang="ts">
import { computed } from 'vue'
import { ArrowUpRight, Check, Download, ListChecks, ShieldCheck } from '@lucide/vue'
import type { SalesReport } from './types'
import { reviewDate } from './reportReviewPresentation'
import ReportSourceIdentity from './ReportSourceIdentity.vue'
const props = defineProps<{ report: SalesReport; editable: boolean; exportable: boolean; busy: boolean; dirty: boolean }>()
const emit = defineEmits<{ resolve: []; complete: []; draft: []; approve: []; final: [] }>()
const review = computed(() => props.report.data.preparation!.review!)
const summary = computed(() => review.value.summary)
const approval = computed(() => [...review.value.approvals].reverse().find(item => item.valid))
</script>
<template>
  <section class="rw-review-summary" aria-labelledby="rw-review-title">
    <header>
      <div><h2 id="rw-review-title">INFORME DE VENTAS MENSUAL</h2><ReportSourceIdentity :source-file="report.data.sourceFile" :version="report.version" /></div>
      <span class="rw-review-state"><ShieldCheck v-if="report.canExportFinal" :size="18" /><ListChecks v-else :size="18" />{{ report.canExportFinal ? 'Aprobado' : 'Borrador' }}</span>
    </header>
    <dl class="rw-review-counts" aria-label="Resultado de la preparación">
      <div><dt>Registros del informe</dt><dd>{{ summary.finalRows }}</dd></div>
      <div><dt>Resueltos automáticamente</dt><dd>{{ summary.automaticRows }}</dd></div>
      <div><dt>Requieren validación</dt><dd>{{ summary.validationRows }}</dd></div>
      <div class="rw-incidence-count"><dt>Incidencias</dt><dd><button type="button" :aria-label="`Ver las ${summary.pendingFindings} incidencias pendientes`" :disabled="busy || !summary.pendingFindings" @click="emit('resolve')"><strong>{{ summary.pendingFindings }}</strong><span v-if="summary.pendingFindings">Ver incidencias <ArrowUpRight :size="16" aria-hidden="true" /></span></button></dd></div>
    </dl>
    <p v-if="report.canExportFinal && approval" class="rw-review-approval"><Check :size="16" aria-hidden="true" /> Aprobado por {{ approval.actor }} · {{ reviewDate(approval.occurredAt) }}</p>
    <div class="rw-review-actions">
      <button v-if="summary.pendingFindings" aria-label="Ver incidencias" type="button" class="rw-button rw-button-primary" :disabled="busy" @click="emit('resolve')"><ListChecks :size="18" />Ver incidencias <span aria-hidden="true">{{ summary.pendingFindings }}</span></button>
      <button v-else-if="editable && report.canApprove && !report.canExportFinal" type="button" class="rw-button rw-button-primary" :disabled="busy || dirty" @click="emit('approve')"><ShieldCheck :size="18" />Aprobar informe</button>
      <button v-if="exportable && report.canExportFinal" type="button" class="rw-button rw-button-primary" :disabled="busy || dirty" @click="emit('final')"><Download :size="18" />Descargar informe</button>
      <button type="button" class="rw-button rw-button-outlined" :disabled="busy" @click="emit('complete')">Ver informe completo</button>
      <button v-if="exportable" type="button" class="rw-button rw-button-subtle" :disabled="busy || !report.canExportDraft" @click="emit('draft')"><Download :size="17" />Descargar borrador</button>
    </div>
    <p v-if="dirty" class="rw-small-note">Hay cambios sin guardar. Guarda tus cambios para incluirlos en la aprobación y el archivo final.</p>
  </section>
</template>
