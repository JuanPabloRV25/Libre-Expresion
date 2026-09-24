<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { Building2, Plus, Search } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ConfirmDialog from '../components/ConfirmDialog.vue'
import { ApiError } from '../api/httpClient'
import { areasService, type ApiArea } from '../services/areasService'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const areas = ref<ApiArea[]>([])
const search = ref('')
const loading = ref(false)
const saving = ref(false)
const errorMessage = ref('')
const editorOpen = ref(false)
const toggleTarget = ref<ApiArea>()
const form = reactive({ id: undefined as string | undefined, name: '', description: '' })
let searchTimer: ReturnType<typeof setTimeout> | undefined

function describeError(error: unknown) {
  return error instanceof ApiError
    ? error.message
    : 'No fue posible completar la operación. Intenta nuevamente.'
}

async function load() {
  loading.value = true
  errorMessage.value = ''
  try {
    areas.value = await areasService.list({ search: search.value })
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    loading.value = false
  }
}

onMounted(load)
onUnmounted(() => clearTimeout(searchTimer))
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(load, 250)
})

function openEditor(area?: ApiArea) {
  Object.assign(form, area
    ? { id: area.id, name: area.name, description: area.description ?? '' }
    : { id: undefined, name: '', description: '' })
  errorMessage.value = ''
  editorOpen.value = true
}

async function save() {
  saving.value = true
  errorMessage.value = ''
  try {
    const input = { name: form.name, description: form.description || null }
    if (form.id) await areasService.update(form.id, input)
    else await areasService.create(input)
    await load()
    editorOpen.value = false
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    saving.value = false
  }
}

async function toggle() {
  if (!toggleTarget.value) return
  errorMessage.value = ''
  try {
    await areasService.setStatus(toggleTarget.value.id, !toggleTarget.value.isActive)
    await load()
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    toggleTarget.value = undefined
  }
}
</script>

<template>
  <AppLayout>
    <PageHeader eyebrow="Portal · Áreas" title="Áreas" description="Administra las áreas o dependencias informativas de la organización.">
      <button v-if="auth.hasPermission('areas.create')" class="button primary" @click="openEditor()">
        <Plus :size="18" /> Crear área
      </button>
    </PageHeader>
    <section class="info-banner">
      <Building2 :size="20" />
      <div><strong>Las áreas no otorgan permisos</strong><p>El acceso depende exclusivamente de los roles y permisos asignados a cada usuario.</p></div>
    </section>
    <p v-if="errorMessage && !editorOpen" class="form-error">{{ errorMessage }}</p>
    <section class="panel table-panel">
      <div class="table-toolbar">
        <label class="search-box"><Search :size="18" /><input v-model="search" aria-label="Buscar áreas" placeholder="Buscar áreas" /></label>
        <span><strong>{{ areas.length }}</strong> áreas</span>
      </div>
      <div class="table-scroll">
        <table>
          <thead><tr><th>Área</th><th>Descripción</th><th>Estado</th><th>Acciones</th></tr></thead>
          <tbody>
            <tr v-if="loading"><td class="empty-row" colspan="4">Cargando áreas…</td></tr>
            <tr v-else-if="areas.length === 0"><td class="empty-row" colspan="4">No se encontraron áreas.</td></tr>
            <tr v-for="area in areas" v-else :key="area.id">
              <td><strong>{{ area.name }}</strong></td>
              <td>{{ area.description || 'Sin descripción' }}</td>
              <td><StatusBadge :active="area.isActive" /></td>
              <td><div class="row-actions">
                <button v-if="auth.hasPermission('areas.edit')" @click="openEditor(area)">Editar</button>
                <button v-if="auth.hasPermission('areas.activate')" @click="toggleTarget = area">{{ area.isActive ? 'Inactivar' : 'Activar' }}</button>
              </div></td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <Teleport to="body">
      <div v-if="editorOpen" class="dialog-backdrop" @click.self="editorOpen = false">
        <form class="dialog-card" @submit.prevent="save">
          <p class="eyebrow">{{ form.id ? 'EDITAR ÁREA' : 'NUEVA ÁREA' }}</p>
          <h2>{{ form.id ? 'Editar área' : 'Crear área' }}</h2>
          <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
          <label><span class="field-label">Nombre <em>*</em></span><input v-model="form.name" required maxlength="120" /></label>
          <label><span class="field-label">Descripción</span><textarea v-model="form.description" maxlength="500" rows="3" /></label>
          <div class="dialog-actions">
            <button class="button secondary" type="button" @click="editorOpen = false">Cancelar</button>
            <button class="button primary" type="submit" :disabled="saving">{{ saving ? 'Guardando…' : 'Guardar área' }}</button>
          </div>
        </form>
      </div>
    </Teleport>
    <ConfirmDialog
      :open="Boolean(toggleTarget)"
      :title="`${toggleTarget?.isActive ? 'Inactivar' : 'Activar'} área`"
      :description="`Confirma el cambio de estado para ${toggleTarget?.name ?? ''}.`"
      confirm-label="Confirmar"
      @close="toggleTarget = undefined"
      @confirm="toggle"
    />
  </AppLayout>
</template>
