<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { ApiError } from '../api/httpClient'
import { rolesService, type ApiRole } from '../services/rolesService'
import { permissionsService, type ApiPermission } from '../services/permissionsService'

const route = useRoute()
const router = useRouter()
const role = ref<ApiRole>()
const permissions = ref<ApiPermission[]>([])
const selected = ref<string[]>([])
const saving = ref(false)
const errorMessage = ref('')
const moduleNames: Record<string, string> = {
  areas: 'Áreas', users: 'Usuarios', roles: 'Roles', permissions: 'Permisos', audit: 'Auditoría', 'commercial.production_orders': 'Comercial · Órdenes de producción',
}

onMounted(async () => {
  try {
    ;[role.value, permissions.value] = await Promise.all([
      rolesService.get(String(route.params.id)),
      permissionsService.list(),
    ])
    selected.value = [...(role.value?.permissionCodes ?? [])]
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar la matriz de permisos.'
  }
})

const visiblePermissions = computed(() => permissions.value)
const grouped = computed(() => visiblePermissions.value.reduce<Record<string, ApiPermission[]>>((result, permission) => {
  ;(result[moduleNames[permission.module] ?? permission.module] ??= []).push(permission)
  return result
}, {}))

async function save() {
  if (!role.value || role.value.isSystem) return
  saving.value = true
  errorMessage.value = ''
  try {
    await rolesService.setPermissions(role.value.id, selected.value)
    await router.push('/roles')
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible actualizar los permisos.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink>
    <PageHeader eyebrow="Portal · Matriz de permisos" title="Matriz de permisos" :description="`Selecciona las acciones autorizadas para ${role?.name ?? 'el rol'}.`" />
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section v-if="role" class="panel permission-matrix">
      <div class="matrix-head"><p class="eyebrow">ROL SELECCIONADO</p><h2>{{ role.name }}</h2><p>{{ role.description }}</p><strong>{{ selected.filter((code) => !code.startsWith('audit.')).length }} de {{ visiblePermissions.length }} seleccionados</strong></div>
      <p v-if="role.isSystem" class="info-banner">El rol Superadmin está protegido y debe conservar todos los permisos oficiales.</p>
      <section v-for="(items, module) in grouped" :key="module" class="permission-group">
        <h3>{{ module }} <small>{{ items.filter((item) => selected.includes(item.code)).length }}/{{ items.length }}</small></h3>
        <div class="choice-grid"><label v-for="permission in items" :key="permission.code" class="choice-card"><input v-model="selected" type="checkbox" :value="permission.code" :disabled="role.isSystem" /><span><strong>{{ permission.displayName }}</strong><small>Permiso del módulo {{ module }}</small></span></label></div>
      </section>
      <div class="form-actions"><RouterLink class="button secondary" to="/roles">Cancelar</RouterLink><button v-if="!role.isSystem" class="button primary" :disabled="saving" @click="save">{{ saving ? 'Guardando…' : 'Guardar cambios' }}</button></div>
    </section>
  </AppLayout>
</template>
