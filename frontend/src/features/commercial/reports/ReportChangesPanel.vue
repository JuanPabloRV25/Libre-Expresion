<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ArrowRight, ArrowUpRight, Check, Search } from '@lucide/vue'
import type { OpRecord, ReportChange, ReportReview, SalesDetail, SalesGroup } from './types'
import { changeComparison, changeFieldLabel, changeValue, presentReportChanges, searchChangeEntry } from './reportChangePresentation'
import type { ChangeEntry } from './reportChangePresentation'
import WorkspacePagination from './WorkspacePagination.vue'
import { reviewDate, reviewDecisionLabel } from './reportReviewPresentation'

const props = defineProps<{ changes: ReportChange[]; details: SalesDetail[]; history: OpRecord[]; rows?: SalesGroup[]; review?: ReportReview | null }>()
const emit = defineEmits<{ locate: [key: string] }>()
const search = ref(''), filter = ref<'automatic' | 'manual'>('automatic'), page = ref(1), retainedPage = ref(1)
const presentation = computed(() => presentReportChanges(props.changes, props.details, props.rows, props.review))
const decisionPage = ref(1)
const decisions = computed(() => [...(props.review?.decisions ?? [])].filter(decision => decisionCase(decision.caseId)?.rowKey).reverse())
const visibleDecisions = computed(() => decisions.value.slice((decisionPage.value - 1) * 5, decisionPage.value * 5))
function decisionCase(id: string) { return props.review?.cases.find(item => item.id === id) }
function decisionRow(id: string) { return props.rows?.find(row => row.key === decisionCase(id)?.rowKey) }
const filtered = computed(() => presentation.value[filter.value].filter(entry => searchChangeEntry(entry, search.value)))
const retained = computed(() => presentation.value.retained.filter(entry => searchChangeEntry(entry, search.value)))
const visible = computed(() => filtered.value.slice((page.value - 1) * 5, page.value * 5))
const retainedVisible = computed(() => retained.value.slice((retainedPage.value - 1) * 5, retainedPage.value * 5))
const detailById = computed(() => new Map(props.details.map(detail => [detail.id, detail])))
const historyById = computed(() => new Map(props.history.map(record => [record.id, record])))
watch([search, filter], () => { page.value = 1; retainedPage.value = 1 })
watch(filtered, () => { page.value = Math.min(page.value, Math.max(1, Math.ceil(filtered.value.length / 5))) })
watch(retained, () => { retainedPage.value = Math.min(retainedPage.value, Math.max(1, Math.ceil(retained.value.length / 5))) })
function sources(entry: ChangeEntry) { return [...new Set(entry.changes.flatMap(change => change.detailIds))].map(id => detailById.value.get(id)).filter((detail): detail is SalesDetail => !!detail) }
function records(entry: ChangeEntry) { return [...new Set(entry.changes.flatMap(change => change.historyIds))].map(id => historyById.value.get(id)).filter((record): record is OpRecord => !!record) }
function unavailableReferences(entry: ChangeEntry) {
  const detailIds = new Set(entry.changes.flatMap(change => change.detailIds)), historyIds = new Set(entry.changes.flatMap(change => change.historyIds))
  return [...detailIds].filter(id => !detailById.value.has(id)).length + [...historyIds].filter(id => !historyById.value.has(id)).length
}
</script>

<template>
  <div class="rw-changes-panel rc-panel">
    <p class="rc-intro">Cambios guardados en el informe de ventas mensual.</p>
    <details v-if="decisions.length" class="rw-case-evidence"><summary>Decisiones guardadas de la auxiliar · {{ decisions.length }}</summary><ul class="rw-case-decisions"><li v-for="decision in visibleDecisions" :key="decision.id"><strong>{{ reviewDecisionLabel(decision.action) }} · {{ decisionRow(decision.caseId) ? `Venta ${decisionRow(decision.caseId)!.number}` : 'Archivo' }}</strong><p>{{ decision.before === decision.after ? `Valor confirmado: ${decision.after || 'Celda vacía'}` : `Antes: ${decision.before || 'Celda vacía'} → Ahora: ${decision.after || 'Celda vacía'}` }}</p><small>{{ decision.actor }} · {{ reviewDate(decision.occurredAt) }} · Versión {{ decision.reportVersion }}</small><button v-if="decisionCase(decision.caseId)?.rowKey" type="button" class="rw-button rw-button-subtle" @click="emit('locate', decisionCase(decision.caseId)!.rowKey!)">Ver fila</button></li></ul><WorkspacePagination v-model="decisionPage" :total="decisions.length" label="Páginas de decisiones guardadas" /></details>
    <div class="rc-tools">
      <label class="rc-search"><Search :size="17" aria-hidden="true" /><input v-model="search" aria-label="Buscar OP, cliente o cambio" placeholder="Buscar OP, cliente o cambio" /></label>
      <label class="rc-filter"><span>Mostrar</span><select v-model="filter" aria-label="Tipo de cambios"><option value="automatic">Preparación automática</option><option value="manual">Mis ediciones guardadas</option></select></label>
    </div>
    <p v-if="!visible.length" class="rc-empty">{{ search ? 'No encontramos cambios para esta búsqueda.' : filter === 'manual' ? 'Todavía no has guardado ediciones en este reporte.' : 'No hubo ajustes en las filas del reporte.' }}</p>
    <article v-for="entry in visible" :key="entry.id" class="rw-change-item rc-entry">
      <header class="rc-entry-heading">
        <div><h3>{{ entry.title }}</h3><p>{{ entry.subtitle }}</p></div>
        <button v-if="entry.rowKey" type="button" class="rw-button rw-button-subtle rc-locate" @click="emit('locate', entry.rowKey)">Ver fila <ArrowUpRight :size="15" aria-hidden="true" /></button>
      </header>
      <div v-for="event in entry.events" :key="event.id" class="rc-event">
        <p v-if="filter === 'manual'" class="rc-event-time">{{ event.label }}</p>
        <ul class="rc-actions">
          <li v-for="action in event.actions" :key="action.id" :class="['rc-action', `rc-action-${action.status}`]">
            <span class="rc-action-marker" aria-hidden="true"><Check v-if="action.status === 'changed' || action.status === 'confirmation' || action.status === 'resolved'" :size="13" /><span v-else>!</span></span>
            <div class="rc-action-content"><p class="rc-action-title">{{ action.text }}<small v-if="action.status === 'resolved'" class="rc-resolved">Resuelto</small></p><p v-if="action.explanation" class="rc-explanation">{{ action.explanation }}</p>
              <div v-if="action.comparison" class="rc-comparison"><div><span>Antes</span><p v-for="(value, index) in action.comparison.before" :key="index">{{ value }}</p></div><ArrowRight :size="16" aria-hidden="true" /><div><span>Ahora</span><p>{{ action.comparison.after }}</p></div></div>
            </div>
          </li>
        </ul>
      </div>
      <details class="rc-evidence"><summary>Consultar datos de origen</summary>
        <div v-if="sources(entry).length" class="rc-evidence-section"><h4>Informe de ventas</h4><ul><li v-for="detail in sources(entry)" :key="detail.id"><strong>Fila {{ detail.sourceRow }} · {{ detail.sheet }}</strong><small>{{ detail.sourceFile }}</small><dl><dt>Número OP</dt><dd>{{ detail.managerOp || 'Celda vacía' }}</dd><dt>Cliente</dt><dd>{{ detail.client || 'Celda vacía' }}</dd><dt>Línea</dt><dd>{{ detail.line || 'Celda vacía' }}</dd><dt>Importe</dt><dd>{{ changeValue('VALOR_BRUT', String(detail.rawAmount)) }}</dd><dt>Detalle</dt><dd>{{ detail.detail || 'Celda vacía' }}</dd></dl></li></ul></div>
        <div v-if="records(entry).length" class="rc-evidence-section"><h4>Informe de OPs</h4><ul><li v-for="record in records(entry)" :key="record.id"><strong>OP {{ record.number }}</strong><dl><dt>Cliente · columna F</dt><dd>{{ record.data.cells[5] || 'Celda vacía' }}</dd><dt>Referencia · columna G</dt><dd>{{ record.data.cells[6] || 'Celda vacía' }}</dd></dl></li></ul></div>
        <p v-if="unavailableReferences(entry)" class="rc-explanation">Algunas referencias de origen ya no están disponibles en este reporte.</p>
        <details class="rc-full-explanation"><summary>Ver explicación completa de los ajustes</summary><ul><li v-for="change in entry.changes" :key="change.id"><strong>{{ changeFieldLabel(change.field) }}</strong><p>{{ change.reason }}</p><template v-if="changeComparison(change)"><dl><dt>Antes</dt><dd><p v-for="(value, index) in change.before" :key="index">{{ changeValue(change.field, value) }}</p></dd><dt>Ahora</dt><dd>{{ changeValue(change.field, change.after) }}</dd></dl></template><p v-else-if="change.after && change.kind !== 'columns_removed'" class="rc-explanation">Valor conservado: {{ changeValue(change.field, change.after) }}</p><p v-else-if="change.kind === 'columns_removed'" class="rc-explanation">Columnas: {{ change.before.join(', ') }}</p></li></ul></details>
      </details>
    </article>
    <WorkspacePagination v-if="filtered.length" v-model="page" :total="filtered.length" label="Páginas de cambios" />
    <details v-if="filter === 'automatic' && retained.length" class="rc-retained">
      <summary>Filas que conservaron los datos originales <span>{{ retained.length }}</span></summary>
      <p>Estas filas pasaron directamente desde el Informe de ventas, sin cambiar su número OP.</p>
      <article v-for="entry in retainedVisible" :key="entry.id" class="rc-retained-row"><div><strong>{{ entry.title }}</strong><p>{{ entry.subtitle }}</p></div><button v-if="entry.rowKey" type="button" class="rw-button rw-button-subtle rc-locate" @click="emit('locate', entry.rowKey)">Ver fila <ArrowUpRight :size="15" aria-hidden="true" /></button><details class="rc-retained-reason"><summary>Ver origen y explicación</summary><ul><li v-for="source in sources(entry)" :key="source.id"><strong>Informe de ventas · fila {{ source.sourceRow }}</strong><p>{{ source.sourceFile }} · {{ source.sheet }}</p></li><li v-for="change in entry.changes" :key="change.id"><p>{{ change.reason }}</p><p>Número OP conservado: {{ changeValue(change.field, change.after) }}</p></li></ul></details></article>
      <WorkspacePagination v-model="retainedPage" :total="retained.length" label="Páginas de filas sin cambios" />
    </details>
  </div>
</template>

<style scoped>
.rc-panel { display: grid; gap: 16px; min-width: 0; color: var(--charcoal); }
.rc-intro { margin: 0; font-size: .9rem; line-height: 1.55; color: var(--muted); }
.rc-tools { display: flex; align-items: end; flex-wrap: wrap; gap: 12px; }
.rc-search { position: relative; flex: 1 1 240px; max-width: 440px; }
.rc-search > svg { position: absolute; left: 13px; top: 50%; transform: translateY(-50%); color: var(--muted); }
.rc-search input { padding-left: 39px; min-height: 44px !important; }
.rc-filter { flex: 0 1 250px; font-size: .78rem; gap: 4px; color: var(--muted); }
.rc-filter select { min-height: 44px !important; padding-block: .65rem; font-size: .88rem; }
.rc-empty { margin: 0; padding: 16px 0; color: var(--muted); font-size: .9rem; }
.rc-entry { padding: 20px; margin: 0; border: 1px solid var(--line); border-radius: 12px; background: var(--surface); min-width: 0; }
.rc-entry-heading { display: flex; justify-content: space-between; align-items: start; gap: 16px; margin-bottom: 18px; }
.rc-entry-heading > div { min-width: 0; }
.rc-entry-heading h3 { margin: 0 0 5px; font-size: 1rem; font-weight: 650; letter-spacing: -.015em; overflow-wrap: anywhere; }
.rc-entry-heading p { margin: 0; font-size: .83rem; line-height: 1.5; color: var(--muted); overflow-wrap: anywhere; }
.rc-locate { flex: 0 0 auto; padding: 7px 10px; min-height: 36px; font-size: .8rem; }
.rc-file-icon { color: var(--orange-dark); flex-shrink: 0; }
.rc-event + .rc-event { margin-top: 18px; padding-top: 16px; border-top: 1px solid var(--line); }
.rc-event-time { color: var(--muted); font-size: .76rem; margin: 0 0 12px; }
.rc-actions { list-style: none; padding: 0; margin: 0; display: grid; gap: 16px; }
.rc-action { display: flex; gap: 10px; align-items: start; min-width: 0; }
.rc-action-marker { display: inline-flex; align-items: center; justify-content: center; width: 20px; height: 20px; flex: 0 0 auto; margin-top: 1px; background: var(--orange-soft); color: var(--orange-dark); border-radius: 50%; font-size: .74rem; font-weight: 700; }
.rc-action-pending .rc-action-marker { border: 1px solid var(--orange); }
.rc-action-content { flex: 1; min-width: 0; }
.rc-action-title { font-size: .9rem; font-weight: 600; line-height: 1.5; margin: 0; overflow-wrap: anywhere; }
.rc-explanation { font-size: .83rem; line-height: 1.6; color: var(--muted); margin: 3px 0 0; overflow-wrap: anywhere; }
.rc-resolved { margin-left: 8px; border: 1px solid var(--line); border-radius: 5px; padding: 2px 5px; font-size: .7rem; font-weight: 400; white-space: nowrap; }
.rc-comparison { display: grid; grid-template-columns: minmax(0, 1fr) 16px minmax(0, 1fr); align-items: center; gap: 12px; max-width: 650px; margin-top: 10px; padding: 10px 12px; border: 1px solid var(--line); border-radius: 8px; }
.rc-comparison > div { min-width: 0; }
.rc-comparison span { display: block; color: var(--muted); font-size: .72rem; margin-bottom: 5px; }
.rc-comparison p { margin: 0; font-size: .86rem; white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.5; }
.rc-comparison svg { color: var(--orange-dark); }
.rc-evidence { margin-top: 18px; padding-top: 12px; border-top: 1px solid var(--line); }
.rc-evidence summary, .rc-retained-reason summary { width: fit-content; cursor: pointer; color: var(--muted); font-size: .8rem; line-height: 1.5; }
.rc-evidence summary:hover, .rc-retained-reason summary:hover { color: var(--orange-dark); }
.rc-evidence-section { margin-top: 18px; }
.rc-evidence-section h4 { font-size: .85rem; margin: 0 0 10px; }
.rc-evidence ul, .rc-retained-reason ul { list-style: none; padding: 0; margin: 0; display: grid; gap: 10px; }
.rc-evidence li { padding: 12px; border: 1px solid var(--line); border-radius: 8px; min-width: 0; }
.rc-evidence strong { font-size: .82rem; display: block; }
.rc-evidence small { display: block; font-size: .76rem; margin-top: 4px; overflow-wrap: anywhere; }
.rc-evidence dl { display: grid; grid-template-columns: minmax(90px, 130px) minmax(0, 1fr); gap: 6px 12px; font-size: .8rem; line-height: 1.6; margin: 10px 0 0; }
.rc-evidence dt { color: var(--muted); }
.rc-evidence dd { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
.rc-evidence dd p { margin: 0; }
.rc-full-explanation { margin-top: 16px; }
.rc-full-explanation ul { margin-top: 10px; }
.rc-full-explanation li > p { margin: 5px 0 0; font-size: .8rem; line-height: 1.6; overflow-wrap: anywhere; }
.rc-retained { border-top: 1px solid var(--line); padding-top: 16px; min-width: 0; }
.rc-retained > summary { color: var(--muted); cursor: pointer; font-size: .85rem; line-height: 1.6; }
.rc-retained > summary span { font-size: .75rem; margin-left: 6px; border: 1px solid var(--line); border-radius: 5px; padding: 2px 6px; }
.rc-retained > p { margin: 12px 0 4px; font-size: .83rem; color: var(--muted); line-height: 1.6; }
.rc-retained-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: start; gap: 8px; padding: 14px 0; border-bottom: 1px solid var(--line); }
.rc-retained-row strong { font-size: .86rem; overflow-wrap: anywhere; }
.rc-retained-row p { font-size: .8rem; line-height: 1.5; color: var(--muted); margin: 4px 0 0; overflow-wrap: anywhere; }
.rc-retained-reason { grid-column: 1 / -1; }
.rc-retained-reason ul { margin-top: 10px; }
@media (max-width: 600px) {
  .rc-entry { padding: 16px 13px; }
  .rc-entry-heading { gap: 8px; }
  .rc-tools { display: grid; grid-template-columns: minmax(0, 1fr); }
  .rc-search, .rc-filter { max-width: none; }
  .rc-comparison { grid-template-columns: minmax(0, 1fr); gap: 6px; }
  .rc-comparison svg { transform: rotate(90deg); }
  .rc-evidence dl { grid-template-columns: minmax(0, 1fr); gap: 2px; }
  .rc-evidence dd + dt { margin-top: 8px; }
  .rc-action-title { font-size: .86rem; }
}
</style>
