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
  const activeRoles = computed(() => currentUser.value?.availableRoles.filter((role) => currentUser.value?.activeRoleIds.includes(role.id)) ?? [])
  const roleNames = computed(() => activeRoles.value.map((role) => role.name).join(', ') || 'Usuario interno')
  const roleSummary = computed(() => activeRoles.value.length === 1 ? activeRoles.value[0]?.name : activeRoles.value.length + ' roles activos')

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
    const notificationStatus = await authService.changeRequiredPassword(newPassword, confirmation)
    currentUser.value = null
    return notificationStatus
  }

  async function changeOwnPassword(currentPassword: string, newPassword: string, confirmation: string) {
    if (!currentUser.value) return false
    const notificationStatus = await authService.changePassword(currentPassword, newPassword, confirmation)
    currentUser.value = await authService.me()
    return notificationStatus
  }

  function hasPermission(code?: string) {
    return !code || permissionCodes.value.includes(code)
  }

  async function selectActiveRoles(roleIds: string[]) {
    currentUser.value = await authService.selectActiveRoles(roleIds)
  }

  async function logout() {
    try {
      if (currentUser.value) await authService.logout()
    } finally {
      currentUser.value = null
      initialized.value = true
    }
  }

  return { currentUser, initialized, permissionCodes, activeRoles, roleNames, roleSummary, isAuthenticated, fullName, initialize, login, completeFirstLogin, changeOwnPassword, selectActiveRoles, hasPermission, logout }
})
