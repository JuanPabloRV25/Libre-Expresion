<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppLayout from '../layouts/AppLayout.vue'
import PageHeader from '../components/PageHeader.vue'
import { emailsService } from '../services/emailsService'
import type { DemoEmail } from '../types/models'

const emails = ref<DemoEmail[]>([])
const filter = ref('all')
onMounted(async () => { emails.value = await emailsService.list() })
const filtered = computed(() => filter.value === 'all' ? emails.value : emails.value.filter((email) => email.type === filter.value))
</script>

<template>
  <AppLayout><PageHeader eyebrow="Portal · Correos simulados" title="Correos simulados" description="Bandeja ficticia para demostrar notificaciones sin enviar correos reales." /><section class="info-banner demo-info"><span class="demo-pill">DEMO · SOLO PROTOTIPO</span><div><strong>Bandeja de correos demo</strong><p>Todo permanece dentro del frontend. No se usa Outlook, SMTP ni servicios externos.</p></div></section><section class="panel table-panel"><div class="table-toolbar"><span><strong>{{ filtered.length }}</strong> correos enviados</span><select v-model="filter" aria-label="Filtrar correos"><option value="all">Todos</option><option>Creación de usuario</option><option>Restablecimiento</option><option>Cambio de contraseña</option></select></div><div class="table-scroll"><table><thead><tr><th>Destinatario</th><th>Asunto</th><th>Tipo</th><th>Fecha / hora</th><th>Estado</th><th>Acción</th></tr></thead><tbody><tr v-for="email in filtered" :key="email.id"><td><strong>{{ email.recipientName }}</strong><small class="cell-description">{{ email.recipientEmail }}</small></td><td>{{ email.subject }}</td><td>{{ email.type }}</td><td>{{ email.sentAt }}</td><td><span class="status-badge is-active">Enviado</span></td><td><RouterLink :to="`/demo/emails/${email.id}`">Ver correo</RouterLink></td></tr></tbody></table></div></section></AppLayout>
</template>

