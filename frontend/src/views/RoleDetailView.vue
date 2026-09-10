<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { ApiError } from '../api/httpClient'
import { rolesService, type ApiRole } from '../services/rolesService'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const auth = useAuthStore()
const role = ref<ApiRole>()
const errorMessage = ref('')

onMounted(async () => {
  try {
    role.value = await rolesService.get(String(route.params.id))
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'No fue posible cargar el rol.'
  }
})
</script>

<template>
  <AppLayout>
    <RouterLink class="back-link" to="/roles">← Volver a roles</RouterLink>
    <p v-if="errorMessage" class="form-error">{{ errorMessage }}</p>
    <section v-if="role" class="detail-hero">
      <div><p class="eyebrow">DETALLE DE ROL</p><h1>{{ role.name }}</h1><p>{{ role.description || 'Sin descripción' }}</p></div>
      <StatusBadge :active="role.isActive" />
      <div class="detail-actions">
        <RouterLink v-if="auth.hasPermission('roles.edit')" class="button secondary" :to="`/roles/${role.id}/edit`">Editar</RouterLink>
        <RouterLink v-if="auth.hasPermission('roles.assign_permissions')" class="button primary" :to="`/roles/${role.id}/permissions`">Gestionar permisos</RouterLink>
      </div>
    </section>
    <div v-if="role" class="detail-grid">
      <section class="panel">
        <p class="eyebrow">PERMISOS ASIGNADOS</p><h3>{{ role.permissionCodes.length }} permisos</h3>
        <div class="stack-list"><div v-for="code in role.permissionCodes" :key="code"><strong>{{ code }}</strong></div><p v-if="!role.permissionCodes.length">Este rol todavía no tiene permisos.</p></div>
      </section>
      <section class="panel"><p class="eyebrow">USUARIOS ASIGNADOS</p><h3>{{ role.userCount }} usuarios</h3><p>Las asignaciones se administran desde el módulo de Usuarios.</p></section>
    </div>
  </AppLayout>
</template>
