<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ArrowRight, ClipboardList, Factory, FilePenLine, Plus, RefreshCw } from '@lucide/vue'
import AppLayout from '../../../layouts/AppLayout.vue'
import PageHeader from '../../../components/PageHeader.vue'
import { ApiError } from '../../../api/httpClient'
import { useAuthStore } from '../../../stores/auth'
import ProductionOrderStatusBadge from './ProductionOrderStatusBadge.vue'
import { productionOrdersService } from './productionOrdersService'
import type { ProductionOrderSummary } from './types'

const auth = useAuthStore()
const orders = ref<ProductionOrderSummary[]>([])
const loading = ref(true)
const errorMessage = ref('')

const drafts = computed(() => orders.value.filter((order) => order.status === 'draft').length)
const ready = computed(() => orders.value.filter((order) => order.status === 'readyForProduction').length)
const active = computed(() => orders.value.filter((order) => order.status === 'inProduction').length)
const completed = computed(() => orders.value.filter((order) => order.status === 'completed').length)
const recent = computed(() => orders.value.slice(0, 5))

async function load() {
  loading.value = true
  errorMessage.value = ''
  try { orders.value = await productionOrdersService.list() }
  catch (error) { errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el módulo Comercial.' }
  finally { loading.value = false }
}

onMounted(load)
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="FASE 2 · COMERCIAL" title="Gestión comercial" description="Crea órdenes claras, entrégalas a Producción y conserva su trazabilidad en un solo lugar.">
      <RouterLink v-if="auth.hasPermission('commercial.production_orders.create')" class="button primary" to="/commercial/production-orders/new"><Plus :size="18" /> Nueva orden</RouterLink>
    </PageHeader>

    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section class="commercial-welcome">
      <div>
        <span class="commercial-kicker">ORDEN DE PRODUCCIÓN DIGITAL</span>
        <h2>De Comercial a Producción, sin pasos innecesarios.</h2>
        <p>La sección comercial se completa primero. Producción recibe una orden validada y trabaja únicamente sobre sus propios campos.</p>
        <div class="commercial-welcome-actions">
          <RouterLink class="button primary" to="/commercial/production-orders">Ver órdenes <ArrowRight :size="17" /></RouterLink>
          <button class="button commercial-quiet" type="button" :disabled="loading" @click="load"><RefreshCw :size="17" /> Actualizar</button>
        </div>
      </div>
      <div class="commercial-flow" aria-label="Flujo del módulo">
        <span><FilePenLine :size="21" /><b>1</b><small>Comercial prepara</small></span>
        <i aria-hidden="true" />
        <span><ClipboardList :size="21" /><b>2</b><small>El sistema valida</small></span>
        <i aria-hidden="true" />
        <span><Factory :size="21" /><b>3</b><small>Producción ejecuta</small></span>
      </div>
    </section>

    <section class="commercial-metrics" aria-label="Resumen de órdenes">
      <RouterLink to="/commercial/production-orders?group=active"><span class="metric-symbol is-draft">B</span><strong>{{ drafts }}</strong><small>Borradores comerciales</small></RouterLink>
      <RouterLink to="/commercial/production-orders?group=active"><span class="metric-symbol is-ready">L</span><strong>{{ ready }}</strong><small>Listas para Producción</small></RouterLink>
      <RouterLink to="/commercial/production-orders?group=active"><span class="metric-symbol is-progress">P</span><strong>{{ active }}</strong><small>En producción</small></RouterLink>
      <RouterLink to="/commercial/production-orders?group=closed"><span class="metric-symbol is-done">✓</span><strong>{{ completed }}</strong><small>Finalizadas</small></RouterLink>
    </section>

    <section class="panel commercial-recent">
      <div class="commercial-section-heading"><div><p class="eyebrow">ACTIVIDAD RECIENTE</p><h2>Últimas órdenes</h2></div><RouterLink to="/commercial/production-orders">Ver todas <ArrowRight :size="16" /></RouterLink></div>
      <div v-if="loading" class="commercial-empty">Cargando órdenes…</div>
      <div v-else-if="recent.length === 0" class="commercial-empty"><ClipboardList :size="32" /><strong>Todavía no hay órdenes</strong><span>Crea la primera orden comercial para empezar el flujo.</span></div>
      <RouterLink v-for="order in recent" v-else :key="order.id" class="recent-order" :to="`/commercial/production-orders/${order.id}`">
        <span class="recent-code">{{ order.code }}</span>
        <span><strong>{{ order.clientName || 'Cliente pendiente' }}</strong><small>{{ order.productName || 'Producto pendiente' }}</small></span>
        <ProductionOrderStatusBadge :status="order.status" />
        <ArrowRight :size="17" />
      </RouterLink>
    </section>
  </AppLayout>
</template>
