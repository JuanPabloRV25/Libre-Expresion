<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ChevronLeft, ChevronRight, Eye, Filter, Search, ScrollText } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { ApiError } from '../api/httpClient'
import { auditService, type AuditEvent } from '../services/auditService'
import {
  auditResultPresentation,
  formatAuditDate,
  safeAuditMetadataEntries,
} from '../services/auditPresentation'

const events = ref<AuditEvent[]>([])
const loading = ref(false)
const errorMessage = ref('')
const page = ref(1)
const pageSize = 10
const totalItems = ref(0)
const totalPages = ref(0)
const selectedEvent = ref<AuditEvent>()
const filters = reactive({
  search: '',
  action: '',
  entityType: '',
  result: '',
  dateFrom: '',
  dateTo: '',
})
const detailMetadata = computed(() => safeAuditMetadataEntries(selectedEvent.value?.metadata ?? null))

function describeError(error: unknown) {
  if (error instanceof ApiError && error.status === 401) {
    return 'Tu sesión terminó. Inicia sesión nuevamente para consultar la auditoría.'
  }
  if (error instanceof ApiError && error.status === 403) {
    return 'No tienes autorización para consultar la auditoría.'
  }
  return error instanceof ApiError
    ? error.message
    : 'No fue posible cargar los eventos de auditoría. Intenta nuevamente.'
}

function utcBoundary(value: string, endOfDay = false) {
  if (!value) return undefined
  return `${value}T${endOfDay ? '23:59:59.999' : '00:00:00.000'}Z`
}

async function load() {
  loading.value = true
  errorMessage.value = ''
  try {
    const response = await auditService.list({
      page: page.value,
      pageSize,
      search: filters.search,
      action: filters.action,
      entityType: filters.entityType,
      result: filters.result,
      dateFrom: utcBoundary(filters.dateFrom),
      dateTo: utcBoundary(filters.dateTo, true),
    })
    events.value = response.items
    page.value = response.page
    totalItems.value = response.totalItems
    totalPages.value = response.totalPages
  } catch (error) {
    events.value = []
    totalItems.value = 0
    totalPages.value = 0
    errorMessage.value = describeError(error)
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  page.value = 1
  void load()
}

function clearFilters() {
  Object.assign(filters, {
    search: '',
    action: '',
    entityType: '',
    result: '',
    dateFrom: '',
    dateTo: '',
  })
  page.value = 1
  void load()
}

function goToPage(target: number) {
  if (target < 1 || target > totalPages.value || target === page.value) return
  page.value = target
  void load()
}

const auditActionNames: Record<string, string> = {
  created: 'Creación', updated: 'Actualización', activated: 'Activación', deactivated: 'Desactivación',
  login: 'Inicio de sesión', logout: 'Cierre de sesión', sent: 'Notificación enviada', failed: 'Operación fallida',
  completed: 'Proceso completado', permissions: 'Permisos', password: 'Contraseña', reset: 'Restablecimiento',
}
const entityNames: Record<string, string> = { User: 'Usuario', Role: 'Rol', Area: 'Área', Permission: 'Permiso' }
function readableAction(value: string) {
  return value.split(/[._]/).map((part) => auditActionNames[part] ?? (part.charAt(0).toUpperCase() + part.slice(1))).join(' · ')
}

function hasDetail(event: AuditEvent) {
  return Boolean(
    event.correlationId
    || event.userAgent
    || safeAuditMetadataEntries(event.metadata).length,
  )
}

onMounted(load)
</script>

<template>
  <AppLayout>
    <PageHeader
      eyebrow="Portal · Seguridad"
      title="Auditoría"
      description="Consulta los eventos generados por las operaciones administrativas."
    />

    <section class="info-banner">
      <ScrollText :size="20" />
      <div>
        <strong>Registro de solo lectura</strong>
        <p>Los eventos no pueden editarse ni eliminarse desde el Portal.</p>
      </div>
    </section>

    <form class="panel audit-filters" @submit.prevent="applyFilters">
      <div class="audit-filter-heading">
        <Filter :size="18" />
        <strong>Filtros</strong>
      </div>
      <label class="search-box audit-search">
        <Search :size="18" />
        <input v-model="filters.search" aria-label="Buscar en auditoría" placeholder="Acción o identificador" />
      </label>
      <label><span class="field-label">Acción</span><input v-model="filters.action" placeholder="area.created" /></label>
      <label><span class="field-label">Entidad</span><input v-model="filters.entityType" placeholder="Area, Role o User" /></label>
      <label><span class="field-label">Resultado</span><select v-model="filters.result">
          <option value="">Todos</option>
          <option value="SUCCESS">Exitoso</option>
          <option value="FAILED">Fallido</option>
          <option value="DENIED">Denegado</option>
        </select>
      </label>
      <label><span class="field-label">Desde</span><input v-model="filters.dateFrom" type="date" /></label>
      <label><span class="field-label">Hasta</span><input v-model="filters.dateTo" type="date" /></label>
      <div class="audit-filter-actions">
        <button class="button secondary" type="button" @click="clearFilters">Limpiar</button>
        <button class="button primary" type="submit">Aplicar filtros</button>
      </div>
    </form>

    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>

    <section class="panel table-panel">
      <div class="table-toolbar">
        <div><strong>{{ totalItems }}</strong> eventos encontrados</div>
        <span>Ordenados del más reciente al más antiguo</span>
      </div>
      <div class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Fecha y hora</th>
              <th>Usuario / actor</th>
              <th>Acción</th>
              <th>Entidad</th>
              <th>Identificador</th>
              <th>Resultado</th>
              <th>Detalle</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td class="empty-row" colspan="7">Cargando auditoría…</td></tr>
            <tr v-else-if="events.length === 0"><td class="empty-row" colspan="7">No se encontraron eventos.</td></tr>
            <tr v-for="event in events" v-else :key="event.id">
              <td class="audit-date">{{ formatAuditDate(event.occurredAt) }}</td>
              <td><strong>{{ event.actor?.name ?? 'Sistema / no disponible' }}</strong></td>
              <td><span>{{ readableAction(event.action) }}</span></td>
              <td>{{ event.entityType ? (entityNames[event.entityType] ?? event.entityType) : '—' }}</td>
              <td><span class="audit-entity-id">{{ event.entityId ?? '—' }}</span></td>
              <td>
                <span class="audit-result" :class="auditResultPresentation(event.result).className">
                  {{ auditResultPresentation(event.result).label }}
                </span>
              </td>
              <td>
                <button v-if="hasDetail(event)" class="audit-detail-button" @click="selectedEvent = event">
                  <Eye :size="15" /> Ver detalle
                </button>
                <span v-else>—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <footer class="audit-pagination">
        <span>Página {{ page }} de {{ Math.max(totalPages, 1) }}</span>
        <div>
          <button class="button secondary" :disabled="page <= 1 || loading" @click="goToPage(page - 1)">
            <ChevronLeft :size="17" /> Anterior
          </button>
          <button class="button secondary" :disabled="page >= totalPages || loading" @click="goToPage(page + 1)">
            Siguiente <ChevronRight :size="17" />
          </button>
        </div>
      </footer>
    </section>

    <Teleport to="body">
      <div v-if="selectedEvent" class="dialog-backdrop" @click.self="selectedEvent = undefined">
        <section class="dialog-card audit-detail-dialog" role="dialog" aria-modal="true" aria-labelledby="audit-detail-title">
          <p class="eyebrow">EVENTO DE AUDITORÍA</p>
          <h2 id="audit-detail-title">{{ selectedEvent.action }}</h2>
          <dl class="data-list audit-detail-list">
            <div><dt>Fecha y hora</dt><dd>{{ formatAuditDate(selectedEvent.occurredAt) }}</dd></div>
            <div><dt>Actor</dt><dd>{{ selectedEvent.actor?.name ?? 'Sistema / no disponible' }}</dd></div>
            <div><dt>Entidad</dt><dd>{{ selectedEvent.entityType ?? '—' }}</dd></div>
            <div><dt>Identificador</dt><dd>{{ selectedEvent.entityId ?? '—' }}</dd></div>
            <div><dt>Resultado</dt><dd>{{ auditResultPresentation(selectedEvent.result).label }}</dd></div>
            <div v-if="selectedEvent.correlationId"><dt>Correlation ID</dt><dd>{{ selectedEvent.correlationId }}</dd></div>
          </dl>
          <section v-if="detailMetadata.length" class="audit-metadata">
            <h3>Metadata segura</h3>
            <dl>
              <div v-for="entry in detailMetadata" :key="entry.key">
                <dt>{{ entry.label }}</dt>
                <dd>{{ entry.value }}</dd>
              </div>
            </dl>
          </section>
          <section v-if="selectedEvent.userAgent" class="audit-user-agent">
            <h3>User agent</h3>
            <p>{{ selectedEvent.userAgent }}</p>
          </section>
          <div class="dialog-actions">
            <button class="button primary" @click="selectedEvent = undefined">Cerrar</button>
          </div>
        </section>
      </div>
    </Teleport>
  </AppLayout>
</template>
