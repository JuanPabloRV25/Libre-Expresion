<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '../layouts/AppLayout.vue'
import BrandMark from '../components/BrandMark.vue'
import { emailsService } from '../services/emailsService'
import type { DemoEmail } from '../types/models'

const route = useRoute()
const email = ref<DemoEmail>()
onMounted(async () => { email.value = await emailsService.get(String(route.params.id)) })
</script>

<template>
  <AppLayout><RouterLink class="back-link" to="/demo/emails">← Volver a correos simulados</RouterLink><section v-if="email" class="email-preview"><div class="email-meta"><span class="demo-pill">DEMO</span><dl><div><dt>Para</dt><dd>{{ email.recipientEmail }}</dd></div><div><dt>Asunto</dt><dd>{{ email.subject }}</dd></div><div><dt>Estado</dt><dd>Enviado</dd></div></dl></div><article><BrandMark compact /><p>Hola, {{ email.recipientName.split(' ')[0] }}.</p><h1>{{ email.type === 'Restablecimiento' ? 'Se restableció tu contraseña de acceso' : email.type === 'Creación de usuario' ? 'Bienvenido(a) al Portal Libre Expresión' : 'Tu contraseña fue actualizada' }}</h1><template v-if="email.document"><p>Para ingresar temporalmente utiliza tu número de documento:</p><div class="credential-preview"><span>USUARIO Y CONTRASEÑA TEMPORAL</span><strong>{{ email.document }}</strong></div><p>Al ingresar deberás establecer una nueva contraseña.</p></template><p v-else>La operación fue realizada correctamente. Si no la reconoces, comunícate con el administrador del sistema.</p><span class="button primary">INGRESAR AL PORTAL</span><small>Este es un mensaje automático. Por favor no responder.</small></article></section></AppLayout>
</template>

