<script setup lang="ts">
import { computed } from 'vue'
import type { ProductionOrderStatus } from './types'

const props = defineProps<{ status: ProductionOrderStatus }>()
const labels: Record<ProductionOrderStatus, string> = {
  draft: 'Borrador comercial',
  pendingCommercialReview: 'Pendiente de revisión',
  correctionRequired: 'Corrección requerida',
  readyForProduction: 'Lista para Producción',
  inProduction: 'En producción',
  completed: 'Finalizada',
  cancelled: 'Anulada',
}
const label = computed(() => labels[props.status])
</script>

<template><span class="order-status" :class="`is-${status}`"><span aria-hidden="true" />{{ label }}</span></template>
