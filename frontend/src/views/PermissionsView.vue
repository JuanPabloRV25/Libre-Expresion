<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { KeyRound } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { permissionsService } from '../services/permissionsService'
import type { Permission } from '../types/models'

const permissions = ref<Permission[]>([])
onMounted(async () => { permissions.value = await permissionsService.list() })
const grouped = computed(() => permissions.value.reduce<Record<string, Permission[]>>((result, permission) => { ;(result[permission.module] ??= []).push(permission); return result }, {}))
</script>

<template>
  <AppLayout><PageHeader eyebrow="Portal · Permisos" title="Permisos" description="Catálogo de acciones disponibles para la Fase 1." /><section class="permission-summary"><KeyRound :size="28" /><strong>{{ permissions.length }}</strong><span>Permisos disponibles</span><p>Si un rol no tiene un permiso, el acceso se considera denegado. Ocultar opciones es una representación visual temporal.</p></section><div class="permission-catalog"><section v-for="(items, module) in grouped" :key="module" class="panel"><div class="catalog-head"><div><h3>{{ module }}</h3><p>{{ items[0]?.description }}</p></div><strong>{{ items.length }}</strong></div><div class="permission-lines"><div v-for="permission in items" :key="permission.code"><span><strong>{{ permission.name }}</strong><small>{{ permission.description }}</small></span><code>{{ permission.code }}</code><em>Fase 1</em></div></div></section></div></AppLayout>
</template>

