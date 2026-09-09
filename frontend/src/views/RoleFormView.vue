<script setup lang="ts">
import { computed, onMounted, reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { rolesService } from '../services/rolesService'
import type { UserStatus } from '../types/models'

const route = useRoute()
const router = useRouter()
const id = computed(() => route.params.id ? Number(route.params.id) : undefined)
const form = reactive({ name: '', description: '', status: 'active' as UserStatus, permissionCodes: [] as string[] })
onMounted(async () => { if (id.value) { const role = await rolesService.get(id.value); if (role) Object.assign(form, { ...role, permissionCodes: [...role.permissionCodes] }) } })
async function submit() { const saved = await rolesService.save({ ...form, id: id.value }); if (saved) await router.push(`/roles/${saved.id}`) }
</script>

<template>
  <AppLayout><RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink><PageHeader :eyebrow="`Portal · ${id ? 'Editar rol' : 'Crear rol'}`" :title="id ? 'Editar rol' : 'Crear rol'" :description="id ? `Actualiza la configuración de ${form.name}.` : 'Define un rol configurable para cualquier necesidad futura.'" /><form class="panel form-panel compact-form" @submit.prevent="submit"><div class="info-banner"><strong>Rol completamente parametrizable</strong><p>El nombre no activa reglas automáticas. Sus capacidades dependen únicamente de los permisos asociados.</p></div><label>Nombre del rol <em>*</em><input v-model="form.name" required /></label><label>Descripción <em>*</em><textarea v-model="form.description" required rows="4" /></label><label>Estado<select v-model="form.status"><option value="active">Activo</option><option value="inactive">Inactivo</option></select></label><div class="form-actions"><RouterLink class="button secondary" to="/roles">Cancelar</RouterLink><button class="button primary" type="submit">Guardar rol</button></div></form></AppLayout>
</template>

