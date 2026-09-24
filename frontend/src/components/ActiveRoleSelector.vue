<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ChevronDown } from '@lucide/vue'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const router = useRouter()
const open = ref(false)
const saving = ref(false)
const error = ref('')
const selected = ref<string[]>([])

watch(() => auth.currentUser?.activeRoleIds, (ids) => {
  selected.value = [...(ids ?? [])]
}, { immediate: true })

const summary = computed(() => {
  const count = auth.activeRoles.length
  if (count === 1) return auth.activeRoles[0]?.name ?? 'Seleccionar rol'
  return `${count} roles activos`
})

async function apply() {
  if (selected.value.length === 0) {
    error.value = 'Selecciona al menos un rol.'
    return
  }
  saving.value = true
  error.value = ''
  try {
    await auth.selectActiveRoles(selected.value)
    open.value = false
    await router.push('/home')
  } catch {
    error.value = 'No fue posible cambiar los roles activos.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div v-if="auth.currentUser?.availableRoles.length" class="role-selector">
    <div v-if="auth.currentUser.availableRoles.length === 1" class="role-selector-single">
      <small>Rol activo</small><strong>{{ auth.currentUser.availableRoles[0]?.name }}</strong>
    </div>
    <button v-else class="role-selector-trigger" type="button" :aria-expanded="open" @click="open = !open">
      <span><small>Roles activos</small><strong>{{ summary }}</strong></span>
      <ChevronDown :size="16" />
    </button>
    <div v-if="open" class="role-selector-panel">
      <p>Selecciona los roles cuyos permisos deseas utilizar.</p>
      <label v-for="role in auth.currentUser.availableRoles" :key="role.id" class="role-selector-option">
        <input v-model="selected" type="checkbox" :value="role.id" />
        <span>{{ role.name }}</span>
      </label>
      <small v-if="error" class="role-selector-error">{{ error }}</small>
      <button class="button primary full" type="button" :disabled="saving" @click="apply">
        {{ saving ? 'Aplicando…' : 'Aplicar roles' }}
      </button>
    </div>
  </div>
</template>
