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
  areas: 'Áreas', users: 'Usuarios', roles: 'Roles', permissions: 'Permisos', audit: 'Auditoría',
}

onMounted(async () => {
  try {
    permissions.value = await permissionsService.list()
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
    <PageHeader eyebrow="Portal · Permisos" title="Permisos" description="Catálogo de acciones disponibles para la Fase 1." />
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section class="permission-summary"><KeyRound :size="28" /><strong>{{ permissions.length }}</strong><span>Permisos disponibles</span><p>Si un rol no tiene un permiso, el acceso se considera denegado. Ocultar opciones es una representación visual temporal.</p></section>
    <div class="permission-catalog"><section v-for="(items, module) in grouped" :key="module" class="panel"><div class="catalog-head"><div><h3>{{ module }}</h3><p>Permisos administrativos oficiales de {{ module }}.</p></div><strong>{{ items.length }}</strong></div><div class="permission-lines"><div v-for="permission in items" :key="permission.code"><span><strong>{{ permission.displayName }}</strong><small>{{ permission.description || permission.displayName }}</small></span><code>{{ permission.code }}</code><em>Fase 1</em></div></div></section></div>
  </AppLayout>
</template>
