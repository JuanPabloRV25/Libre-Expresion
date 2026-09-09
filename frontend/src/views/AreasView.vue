<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { Building2, Plus, Search } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import { areasService } from '../services/areasService'
import type { Area, UserStatus } from '../types/models'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const areas = ref<Area[]>([])
const search = ref('')
const editorOpen = ref(false)
const toggleTarget = ref<Area>()
const form = reactive({ id: undefined as number | undefined, name: '', description: '', status: 'active' as UserStatus })
onMounted(async () => { areas.value = await areasService.list() })
const filtered = computed(() => areas.value.filter((area) => `${area.name} ${area.description}`.toLowerCase().includes(search.value.toLowerCase())))
function openEditor(area?: Area) { Object.assign(form, area ? { ...area } : { id: undefined, name: '', description: '', status: 'active' }); editorOpen.value = true }
async function save() { await areasService.save({ ...form }); areas.value = await areasService.list(); editorOpen.value = false }
async function toggle() { if (toggleTarget.value) { await areasService.toggleStatus(toggleTarget.value.id); areas.value = await areasService.list() } toggleTarget.value = undefined }
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Áreas" title="Áreas" description="Administra las áreas o dependencias informativas de la organización."><button v-if="auth.hasPermission('areas.create')" class="button primary" @click="openEditor()"><Plus :size="18" /> Crear área</button></PageHeader>
    <section class="info-banner"><Building2 :size="20" /><div><strong>Las áreas no otorgan permisos</strong><p>El acceso depende exclusivamente de los roles y permisos asignados a cada usuario.</p></div></section>
    <section class="panel table-panel"><div class="table-toolbar"><label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar áreas" placeholder="Buscar áreas" /></label><span><strong>{{ filtered.length }}</strong> áreas</span></div><div class="table-scroll"><table><thead><tr><th>Área</th><th>Descripción</th><th>Estado</th><th>Acciones</th></tr></thead><tbody><tr v-for="area in filtered" :key="area.id"><td><strong>{{ area.name }}</strong></td><td>{{ area.description }}</td><td><StatusBadge :active="area.status === 'active'" /></td><td><div class="row-actions"><button v-if="auth.hasPermission('areas.edit')" @click="openEditor(area)">Editar</button><button v-if="auth.hasPermission('areas.activate')" @click="toggleTarget = area">{{ area.status === 'active' ? 'Inactivar' : 'Activar' }}</button></div></td></tr></tbody></table></div></section>
    <Teleport to="body"><div v-if="editorOpen" class="dialog-backdrop" @click.self="editorOpen = false"><form class="dialog-card" @submit.prevent="save"><p class="eyebrow">{{ form.id ? 'EDITAR ÁREA' : 'NUEVA ÁREA' }}</p><h2>{{ form.id ? 'Editar área' : 'Crear área' }}</h2><label>Nombre <em>*</em><input v-model="form.name" required /></label><label>Descripción <em>*</em><textarea v-model="form.description" required rows="3" /></label><label>Estado<select v-model="form.status"><option value="active">Activo</option><option value="inactive">Inactivo</option></select></label><div class="dialog-actions"><button class="button secondary" type="button" @click="editorOpen = false">Cancelar</button><button class="button primary" type="submit">Guardar área</button></div></form></div></Teleport>
    <ConfirmDialog :open="Boolean(toggleTarget)" :title="`${toggleTarget?.status === 'active' ? 'Inactivar' : 'Activar'} área`" :description="`Confirma el cambio de estado para ${toggleTarget?.name ?? ''}.`" confirm-label="Confirmar" @close="toggleTarget = undefined" @confirm="toggle" />
  </AppLayout>
</template>
