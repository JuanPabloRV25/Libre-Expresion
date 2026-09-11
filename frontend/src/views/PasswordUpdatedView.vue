<script setup lang="ts">
import { CircleCheckBig } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { isNotificationStatus, passwordChangedNotificationMessage } from '../services/notificationMessages'

const route = useRoute()
const notificationMessage = computed(() => isNotificationStatus(route.query.notification)
  ? passwordChangedNotificationMessage(route.query.notification)
  : '')
</script>

<template>
  <main class="centered-page">
    <section class="success-card">
      <CircleCheckBig :size="54" />
      <span class="soft-pill">Primer ingreso completado</span>
      <h1>Contraseña actualizada exitosamente</h1>
      <p>La contraseña fue actualizada correctamente. Por seguridad, la sesión se cerró y deberá iniciar sesión nuevamente utilizando su nueva contraseña.</p>
      <p v-if="notificationMessage" class="notice success">{{ notificationMessage }}</p>
      <RouterLink class="button primary" to="/login">Volver al inicio de sesión</RouterLink>
      <small>Portal Libre Expresión · Sesión protegida</small>
    </section>
  </main>
</template>
