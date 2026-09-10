import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '../api/httpClient'
import { authService } from '../services/authService'
import type { AuthenticatedUser } from '../types/models'

export const useAuthStore = defineStore('auth', () => {
  const currentUser = ref<AuthenticatedUser | null>(null)
  const initialized = ref(false)

  const isAuthenticated = computed(() => currentUser.value !== null)
  const fullName = computed(() => currentUser.value ? `${currentUser.value.firstName} ${currentUser.value.lastName}` : '')
  const permissionCodes = computed(() => currentUser.value?.permissions ?? [])
  const roleNames = computed(() => currentUser.value?.roles.join(', ') || 'Usuario interno')

  async function initialize() {
    if (initialized.value) return
    try {
      currentUser.value = await authService.me()
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) throw error
      currentUser.value = null
    } finally {
      initialized.value = true
    }
  }

  async function login(document: string, password: string) {
    const result = await authService.login(document, password)
    if (result.status === 'success' || result.status === 'first-login') {
      currentUser.value = result.user
    }
    initialized.value = true
    return result
  }

  async function completeFirstLogin(newPassword: string, confirmation: string) {
    if (!currentUser.value?.mustChangePassword) return false
    await authService.changeRequiredPassword(newPassword, confirmation)
    currentUser.value = null
    return true
  }

  async function changeOwnPassword(currentPassword: string, newPassword: string, confirmation: string) {
    if (!currentUser.value) return false
    await authService.changePassword(currentPassword, newPassword, confirmation)
    currentUser.value = await authService.me()
    return true
  }

  function hasPermission(code?: string) {
    return !code || permissionCodes.value.includes(code)
  }

  async function logout() {
    try {
      if (currentUser.value) await authService.logout()
    } finally {
      currentUser.value = null
      initialized.value = true
    }
  }

  return { currentUser, initialized, permissionCodes, roleNames, isAuthenticated, fullName, initialize, login, completeFirstLogin, changeOwnPassword, hasPermission, logout }
})
