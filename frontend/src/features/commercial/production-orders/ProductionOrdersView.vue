<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ClipboardList, Plus, Search } from '@lucide/vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { ApiError } from '../../../api/httpClient'
import { useAuthStore } from '../../../stores/auth'
import ProductionOrderStatusBadge from './ProductionOrderStatusBadge.vue'
import { matchesOrderStatusGroup, normalizeOrderStatusGroup, orderStatusGroupOptions } from './orderStatusGroups'
import { productionOrdersService } from './productionOrdersService'
import type { ProductionOrderSummary } from './types'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const orders = ref<ProductionOrderSummary[]>([])
const search = ref('')
const statusGroup = ref(normalizeOrderStatusGroup(route.query.group ?? route.query.status))
const loading = ref(false)
const assignedToMe = ref(route.query.assignedToMe === 'true')
const errorMessage = ref('')
let timer: ReturnType<typeof setTimeout> | undefined

const visibleOrders = computed(() => orders.value.filter((order) => matchesOrderStatusGroup(order.status, statusGroup.value)))

async function load() {
  loading.value = true
  errorMessage.value = ''
  try {
    orders.value = await productionOrdersService.list({ search: search.value, assignedToMe: assignedToMe.value })
    await router.replace({ query: { ...(statusGroup.value !== 'all' ? { group: statusGroup.value } : {}), ...(assignedToMe.value ? { assignedToMe: 'true' } : {}) } })
  } catch (error) { errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar las órdenes.' }
  finally { loading.value = false }
}

function formatDate(value: string | null) {
  return value ? new Intl.DateTimeFormat('es-CO', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${value}T00:00:00Z`)) : 'Por definir'
}

function formatQuantity(value: number | null) { return value == null ? '—' : new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(value) }

onMounted(load)
onUnmounted(() => clearTimeout(timer))
watch([search, statusGroup, assignedToMe], () => { clearTimeout(timer); timer = setTimeout(load, 250) })
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="COMERCIAL · ÓRDENES" title="Órdenes de producción" description="Consulta el estado, responsable y próxima acción de cada orden.">
      <RouterLink v-if="auth.hasPermission('commercial.production_orders.create')" class="button primary" to="/commercial/production-orders/new"><Plus :size="18" /> Nueva orden</RouterLink>
    </PageHeader>
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section class="panel table-panel commercial-table-panel">
      <div class="table-toolbar commercial-order-toolbar">
        <label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar órdenes" placeholder="Buscar cliente, pedido, producto u OP" /></label>
        <select v-model="statusGroup" aria-label="Filtrar por estado"><option v-for="option in orderStatusGroupOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select>
        <label class="assigned-filter"><input v-model="assignedToMe" type="checkbox" /> Asignadas a mí</label>
        <span><strong>{{ visibleOrders.length }}</strong> órdenes</span>
      </div>
      <div class="table-scroll">
        <table>
          <thead><tr><th>Orden</th><th>Cliente y producto</th><th>Entrega</th><th>Cantidad</th><th>Responsable</th><th>Estado</th><th /></tr></thead>
          <tbody>
            <tr v-if="loading"><td class="empty-row" colspan="7">Cargando órdenes…</td></tr>
            <tr v-else-if="visibleOrders.length === 0"><td class="empty-row commercial-empty-row" colspan="7"><ClipboardList :size="25" /> No hay órdenes que coincidan con la búsqueda.</td></tr>
            <tr v-for="order in visibleOrders" v-else :key="order.id">
              <td><RouterLink class="order-code-link" :to="`/commercial/production-orders/${order.id}`">{{ order.code }}</RouterLink><small class="cell-description">Pedido {{ order.customerOrderNumber || 'pendiente' }}</small></td>
              <td><strong>{{ order.clientName || 'Cliente pendiente' }}</strong><small class="cell-description">{{ order.productName || 'Producto pendiente' }}</small></td>
              <td>{{ formatDate(order.deliveryDate) }}</td>
              <td class="number-column">{{ formatQuantity(order.quantity) }}</td>
              <td>{{ order.commercialOwner.name }}<small v-if="order.productionOwner" class="cell-description">Producción: {{ order.productionOwner.name }}</small></td>
              <td><ProductionOrderStatusBadge :status="order.status" /></td>
              <td><RouterLink class="table-arrow" :to="`/commercial/production-orders/${order.id}`" aria-label="Ver orden">→</RouterLink></td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </AppLayout>
</template>
