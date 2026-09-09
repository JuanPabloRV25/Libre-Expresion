<script setup lang="ts">
import type { PortalUser } from '../types/models'

defineProps<{ open: boolean; users: PortalUser[] }>()
const emit = defineEmits<{ close: []; select: [user: PortalUser] }>()

const profileLabel = (user: PortalUser) => ({
  superadmin: 'Superadmin', limited: 'Administrador limitado', standard: 'Usuario interno',
  'first-login': 'Usuario de primer ingreso', inactive: 'Usuario inactivo',
}[user.demoProfile ?? 'standard'])

const stateLabel = (user: PortalUser) => user.status === 'inactive' ? 'Inactivo' : user.mustChangePassword ? 'Primer ingreso' : 'Activo'
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="dialog-backdrop" @click.self="emit('close')">
      <section class="dialog-card demo-dialog" role="dialog" aria-modal="true" aria-labelledby="demo-title">
        <span class="demo-pill">DEMO · SOLO PROTOTIPO</span>
        <h2 id="demo-title">Modo demostración</h2>
        <p>Selecciona un usuario de prueba. La contraseña se carga en el formulario, pero permanece enmascarada.</p>
        <div class="demo-list">
          <button v-for="user in users" :key="user.id" type="button" @click="emit('select', user)">
            <span class="avatar">{{ user.firstName[0] }}{{ user.lastName[0] }}</span>
            <span><strong>{{ profileLabel(user) }}</strong><small>{{ user.document }} · {{ stateLabel(user) }}</small></span>
          </button>
        </div>
        <button class="button secondary full" type="button" @click="emit('close')">Cerrar</button>
      </section>
    </div>
  </Teleport>
</template>

