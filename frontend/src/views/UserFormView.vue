<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { ApiError } from '../api/httpClient'
import { areasService, type ApiArea } from '../services/areasService'
import { rolesService, type ApiRole } from '../services/rolesService'
import { usersService } from '../services/usersService'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const id = computed(() => typeof route.params.id === 'string' ? route.params.id : undefined)
const areas = ref<ApiArea[]>([])
const roles = ref<ApiRole[]>([])
const saving = ref(false)
const errorMessage = ref('')
const form = reactive({
  documentNumber: '', firstName: '', lastName: '', email: '', areaId: '' as string,
  advisorCode: '', roleIds: [] as string[], isActive: true,
})

const availableAreas = computed(() => areas.value.filter((area) => area.isActive || area.id === form.areaId))
const availableRoles = computed(() => roles.value.filter((role) => role.isActive || form.roleIds.includes(role.id)))
const canAssignRoles = computed(() => auth.hasPermission('users.assign_roles'))

onMounted(async () => {
  try {
    areas.value = await areasService.list()
    if (canAssignRoles.value) roles.value = await rolesService.list()
    if (!id.value) return
    const user = await usersService.get(id.value)
    Object.assign(form, {
      documentNumber: user.documentNumber,
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      areaId: user.area?.id ?? '',
      advisorCode: user.advisorCode ?? '',
      roleIds: user.roles.map((role) => role.id),
      isActive: user.isActive,
    })
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el formulario.'
  }
})

async function submit() {
  saving.value = true
  errorMessage.value = ''
  try {
    if (id.value) {
      const updated = await usersService.update(id.value, {
        firstName: form.firstName,
        lastName: form.lastName,
        email: form.email,
        areaId: form.areaId || null,
        advisorCode: form.advisorCode || null,
      })
      if (auth.hasPermission('users.assign_roles')) {
        await usersService.setRoles(updated.id, form.roleIds)
      }
      await router.push(`/users/${updated.id}`)
      return
    }

    const created = await usersService.create({
      documentNumber: form.documentNumber,
      firstName: form.firstName,
      lastName: form.lastName,
      email: form.email,
      areaId: form.areaId || null,
      advisorCode: form.advisorCode || null,
      roleIds: canAssignRoles.value ? form.roleIds : [],
      isActive: form.isActive,
    })
    await router.push({
      path: `/users/${created.user.id}`,
      query: { operation: 'created', notification: created.notificationStatus },
    })
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible guardar el usuario.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/users">← Volver a usuarios</RouterLink>
    <PageHeader :eyebrow="`Portal · ${id ? 'Editar usuario' : 'Crear usuario'}`" :title="id ? 'Editar usuario' : 'Crear usuario'" :description="id ? 'Actualiza la información general y los roles asignados.' : 'Registra un usuario. Su contraseña temporal será su número de documento.'" />
    <form class="panel form-panel" @submit.prevent="submit">
      <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
      <div class="form-section-title"><p class="eyebrow">INFORMACIÓN GENERAL</p><h2>Datos del usuario</h2><span>Campos con * obligatorios</span></div>
      <div class="form-grid">
        <label>Número de documento <em>*</em><input v-model="form.documentNumber" required maxlength="256" :disabled="Boolean(id)" /><small>{{ id ? 'El documento es el identificador de acceso y no puede modificarse.' : 'También será la contraseña temporal para el primer ingreso.' }}</small></label>
        <label>Nombre <em>*</em><input v-model="form.firstName" required maxlength="120" /></label>
        <label>Apellidos <em>*</em><input v-model="form.lastName" required maxlength="120" /></label>
        <label>Correo electrónico <em>*</em><input v-model="form.email" type="email" required maxlength="256" /></label>
        <label>Área o dependencia<select v-model="form.areaId"><option value="">Sin área</option><option v-for="area in availableAreas" :key="area.id" :value="area.id">{{ area.name }}</option></select><small>El área es informativa; no otorga permisos.</small></label>
        <label>Código de asesor<input v-model="form.advisorCode" maxlength="100" /></label>
        <label v-if="!id">Estado<select v-model="form.isActive"><option :value="true">Activo</option><option :value="false">Inactivo</option></select></label>
      </div>
      <fieldset v-if="canAssignRoles"><legend>Roles asignados <small>Puedes seleccionar uno o varios roles parametrizables.</small></legend><div class="choice-grid"><label v-for="role in availableRoles" :key="role.id" class="choice-card"><input v-model="form.roleIds" type="checkbox" :value="role.id" /><span><strong>{{ role.name }}</strong><small>{{ role.description }}</small></span></label></div></fieldset>
      <div class="form-actions"><RouterLink class="button secondary" to="/users">Cancelar</RouterLink><button class="button primary" type="submit" :disabled="saving">{{ saving ? 'Guardando…' : 'Guardar usuario' }}</button></div>
    </form>
  </AppLayout>
</template>
