<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { ApiError } from '../api/httpClient'
import { rolesService } from '../services/rolesService'

const route = useRoute()
const router = useRouter()
const id = computed(() => typeof route.params.id === 'string' ? route.params.id : undefined)
const form = reactive({ name: '', description: '', isSystem: false })
const saving = ref(false)
const errorMessage = ref('')

onMounted(async () => {
  if (!id.value) return
  try {
    const role = await rolesService.get(id.value)
    Object.assign(form, {
      name: role.name,
      description: role.description ?? '',
      isSystem: role.isSystem,
    })
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el rol.'
  }
})

async function submit() {
  saving.value = true
  errorMessage.value = ''
  try {
    const input = { name: form.name, description: form.description || null }
    const saved = id.value
      ? await rolesService.update(id.value, input)
      : await rolesService.create(input)
    await router.push(`/roles/${saved.id}`)
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible guardar el rol.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink>
    <PageHeader :eyebrow="`Portal · ${id ? 'Editar rol' : 'Crear rol'}`" :title="id ? 'Editar rol' : 'Crear rol'" :description="id ? `Actualiza la configuración de ${form.name}.` : 'Define un rol configurable para cualquier necesidad futura.'" />
    <form class="panel form-panel compact-form" @submit.prevent="submit">
      <div class="info-banner"><strong>Rol completamente parametrizable</strong><p>El nombre no activa reglas automáticas. Sus capacidades dependen únicamente de los permisos asociados.</p></div>
      <p v-if="form.isSystem" class="info-banner">Este rol de sistema está protegido: su nombre no puede modificarse.</p>
      <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
      <label><span class="field-label">Nombre del rol <em>*</em></span><input v-model="form.name" required maxlength="256" :disabled="form.isSystem" /></label>
      <label><span class="field-label">Descripción</span><textarea v-model="form.description" maxlength="500" rows="4" /></label>
      <div class="form-actions">
        <RouterLink class="button secondary" to="/roles">Cancelar</RouterLink>
        <button class="button primary" type="submit" :disabled="saving">{{ saving ? 'Guardando…' : 'Guardar rol' }}</button>
      </div>
    </form>
  </AppLayout>
</template>
