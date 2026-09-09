<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { rolesService } from '../services/rolesService'
import { permissionsService } from '../services/permissionsService'
import type { Permission, Role } from '../types/models'

const route = useRoute()
const router = useRouter()
const role = ref<Role>()
const permissions = ref<Permission[]>([])
const selected = ref<string[]>([])
onMounted(async () => { ;[role.value, permissions.value] = await Promise.all([rolesService.get(Number(route.params.id)), permissionsService.list()]); selected.value = [...(role.value?.permissionCodes ?? [])] })
const grouped = computed(() => permissions.value.reduce<Record<string, Permission[]>>((result, permission) => {
  ;(result[permission.module] ??= []).push(permission)
  return result
}, {}))
async function save() { if (role.value) await rolesService.setPermissions(role.value.id, selected.value); await router.push('/roles') }
</script>

<template>
  <AppLayout><RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink><PageHeader eyebrow="Portal · Matriz de permisos" title="Matriz de permisos" :description="`Selecciona las acciones autorizadas para ${role?.name ?? 'el rol'}.`" /><section v-if="role" class="panel permission-matrix"><div class="matrix-head"><p class="eyebrow">ROL SELECCIONADO</p><h2>{{ role.name }}</h2><p>{{ role.description }}</p><strong>{{ selected.length }} de {{ permissions.length }} seleccionados</strong></div><section v-for="(items, module) in grouped" :key="module" class="permission-group"><h3>{{ module }} <small>{{ items?.filter((item) => selected.includes(item.code)).length }}/{{ items?.length }}</small></h3><div class="choice-grid"><label v-for="permission in items" :key="permission.code" class="choice-card"><input v-model="selected" type="checkbox" :value="permission.code" /><span><strong>{{ permission.name }}</strong><small>{{ permission.code }}</small></span></label></div></section><div class="form-actions"><RouterLink class="button secondary" to="/roles">Cancelar</RouterLink><button class="button primary" @click="save">Guardar cambios</button></div></section></AppLayout>
</template>
