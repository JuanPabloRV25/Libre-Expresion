<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { KeyRound } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { ApiError } from '../api/httpClient'
import { permissionsService, type ApiPermission } from '../services/permissionsService'

const permissions = ref<ApiPermission[]>([])
const errorMessage = ref('')
const moduleNames: Record<string, string> = {
  areas: 'Áreas', users: 'Usuarios', roles: 'Roles', permissions: 'Permisos', audit: 'Auditoría', 'commercial.production_orders': 'Comercial · Órdenes de producción',
}

onMounted(async () => {
  try {
    permissions.value = (await permissionsService.list())
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el catálogo de permisos.'
  }
})

const grouped = computed(() => permissions.value.reduce<Record<string, ApiPermission[]>>((result, permission) => {
  ;(result[moduleNames[permission.module] ?? permission.module] ??= []).push(permission)
  return result
}, {}))
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Permisos" title="Permisos" description="Catálogo de acciones disponibles." />
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section class="permission-summary"><KeyRound :size="28" /><strong>{{ permissions.length }}</strong><span>Permisos disponibles</span><p>Si un rol no tiene un permiso, el acceso se considera denegado. Ocultar opciones es una representación visual temporal.</p></section>
    <div class="permission-catalog assigned-permission-groups"><details v-for="(items, module) in grouped" :key="module"><summary><span><strong>{{ module }}</strong><small>{{ items.length }} {{ items.length === 1 ? 'permiso disponible' : 'permisos disponibles' }}</small></span><b aria-hidden="true">+</b></summary><div class="assigned-permission-items permission-catalog-items"><span v-for="permission in items" :key="permission.code"><strong>{{ permission.displayName }}</strong><small>{{ permission.description || 'Acción disponible en este módulo.' }}</small></span></div></details></div>
  </AppLayout>
</template>
