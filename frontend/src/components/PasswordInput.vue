<script setup lang="ts">
import { ref } from 'vue'
import { Eye, EyeOff, LockKeyhole } from '@lucide/vue'

withDefaults(defineProps<{
  autocomplete?: string
  placeholder?: string
  required?: boolean
}>(), {
  autocomplete: 'new-password',
  placeholder: 'Escribe tu contraseña',
  required: true,
})

const model = defineModel<string>({ required: true })
const visible = ref(false)
</script>

<template>
  <span class="password-input">
    <LockKeyhole :size="18" aria-hidden="true" />
    <input
      v-model="model"
      :type="visible ? 'text' : 'password'"
      :required="required"
      :autocomplete="autocomplete"
      :placeholder="placeholder"
    />
    <button type="button" :aria-label="visible ? 'Ocultar contraseña' : 'Mostrar contraseña'" @click="visible = !visible">
      <component :is="visible ? EyeOff : Eye" :size="18" />
    </button>
  </span>
</template>
