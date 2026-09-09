<script setup lang="ts">
import { ShieldX } from '@lucide/vue'
import AppLayout from '../layouts/AppLayout.vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
</script>

<template>
  <AppLayout><section class="state-card"><ShieldX :size="54" /><span class="soft-pill">Acceso restringido</span><h1>Acceso no autorizado</h1><p>Tu perfil no tiene permisos para realizar esta operación. Si consideras que necesitas acceso, solicita la asignación correspondiente.</p><RouterLink class="button primary" to="/home">Volver al inicio</RouterLink><small>Perfil actual: {{ auth.currentUser?.demoProfile === 'limited' ? 'Administrador limitado' : 'Usuario interno' }}</small></section></AppLayout>
</template>

