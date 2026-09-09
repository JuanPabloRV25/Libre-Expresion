<script setup lang="ts">
defineProps<{ open: boolean; title: string; description: string; confirmLabel?: string; tone?: 'primary' | 'danger' }>()
const emit = defineEmits<{ close: []; confirm: [] }>()
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="dialog-backdrop" @click.self="emit('close')">
      <section class="dialog-card" role="dialog" aria-modal="true" :aria-label="title">
        <div class="dialog-icon" :class="tone === 'danger' ? 'danger' : ''">!</div>
        <h2>{{ title }}</h2>
        <p>{{ description }}</p>
        <div class="dialog-actions">
          <button class="button secondary" type="button" @click="emit('close')">Cancelar</button>
          <button class="button" :class="tone === 'danger' ? 'danger' : 'primary'" type="button" @click="emit('confirm')">
            {{ confirmLabel ?? 'Confirmar' }}
          </button>
        </div>
      </section>
    </div>
  </Teleport>
</template>

