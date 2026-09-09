<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { usersService } from '../services/usersService'
import { areasService } from '../services/areasService'
import { rolesService } from '../services/rolesService'
import type { Area, Role, UserStatus } from '../types/models'

const route = useRoute()
const router = useRouter()
const id = computed(() => route.params.id ? Number(route.params.id) : undefined)
const areas = ref<Area[]>([])
const roles = ref<Role[]>([])
const form = reactive({ documentType: 'CC' as 'CC' | 'CE' | 'TI' | 'PPT', document: '', firstName: '', lastName: '', email: '', areaId: 1, roleIds: [] as number[], status: 'active' as UserStatus })

onMounted(async () => {
  ;[areas.value, roles.value] = await Promise.all([areasService.list(), rolesService.list()])
  if (id.value) {
    const user = await usersService.get(id.value)
    if (user) Object.assign(form, { ...user, roleIds: [...user.roleIds] })
  }
})

async function submit() {
  const saved = await usersService.save({ ...form, id: id.value, demoProfile: undefined })
  if (saved) await router.push(`/users/${saved.id}`)
}
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/users">← Volver a usuarios</RouterLink>
    <PageHeader :eyebrow="`Portal · ${id ? 'Editar usuario' : 'Crear usuario'}`" :title="id ? 'Editar usuario' : 'Crear usuario'" :description="id ? 'Actualiza la información general y los roles asignados.' : 'Registra un usuario. Su contraseña temporal será su número de documento.'" />
    <form class="panel form-panel" @submit.prevent="submit">
      <div class="form-section-title"><p class="eyebrow">INFORMACIÓN GENERAL</p><h2>Datos del usuario</h2><span>Campos con * obligatorios</span></div>
      <div class="form-grid">
        <label>Tipo de documento <em>*</em><select v-model="form.documentType" required><option value="CC">Cédula de ciudadanía</option><option value="CE">Cédula de extranjería</option><option value="TI">Tarjeta de identidad</option><option value="PPT">Permiso por Protección Temporal</option></select></label>
        <label>Número de documento <em>*</em><input v-model="form.document" required /><small v-if="!id">También será la contraseña temporal para el primer ingreso.</small></label>
        <label>Nombre <em>*</em><input v-model="form.firstName" required /></label>
        <label>Apellidos <em>*</em><input v-model="form.lastName" required /></label>
        <label>Correo electrónico <em>*</em><input v-model="form.email" type="email" required /></label>
        <label>Área o dependencia <em>*</em><select v-model="form.areaId" required><option v-for="area in areas" :key="area.id" :value="area.id">{{ area.name }}</option></select><small>El área es informativa; no otorga permisos.</small></label>
        <label>Estado<select v-model="form.status"><option value="active">Activo</option><option value="inactive">Inactivo</option></select></label>
      </div>
      <fieldset><legend>Roles asignados <small>Puedes seleccionar uno o varios roles parametrizables.</small></legend><div class="choice-grid"><label v-for="role in roles" :key="role.id" class="choice-card"><input v-model="form.roleIds" type="checkbox" :value="role.id" /><span><strong>{{ role.name }}</strong><small>{{ role.description }}</small></span></label></div></fieldset>
      <div class="form-actions"><RouterLink class="button secondary" to="/users">Cancelar</RouterLink><button class="button primary" type="submit">Guardar usuario</button></div>
    </form>
  </AppLayout>
</template>

