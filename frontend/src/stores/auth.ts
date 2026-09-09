import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { authService } from '../services/authService'
import { rolesService } from '../services/rolesService'
import type { PortalUser } from '../types/models'

type StoredSession = { currentUser: PortalUser | null; pendingUser: PortalUser | null; permissionCodes: string[] }
const emptySession = (): StoredSession => ({ currentUser: null, pendingUser: null, permissionCodes: [] })

export const useAuthStore = defineStore('auth', () => {
  const storageKey = 'portal-demo-session'
  const initialSession = (() => {
    try {
      const savedSession = sessionStorage.getItem(storageKey)
      return savedSession ? JSON.parse(savedSession) as StoredSession : emptySession()
    } catch {
      sessionStorage.removeItem(storageKey)
      return emptySession()
    }
  })()
  const currentUser = ref<PortalUser | null>(initialSession.currentUser)
  const pendingUser = ref<PortalUser | null>(initialSession.pendingUser)
  const permissionCodes = ref<string[]>(initialSession.permissionCodes)

  const isAuthenticated = computed(() => currentUser.value !== null)
  const fullName = computed(() => currentUser.value ? `${currentUser.value.firstName} ${currentUser.value.lastName}` : '')

  function persistSession() {
    const withoutPassword = (user: PortalUser | null) => user ? { ...user, password: '' } : null
    sessionStorage.setItem(storageKey, JSON.stringify({
      currentUser: withoutPassword(currentUser.value),
      pendingUser: withoutPassword(pendingUser.value),
      permissionCodes: permissionCodes.value,
    }))
  }

  async function hydratePermissions(user: PortalUser) {
    const roles = await rolesService.list()
    permissionCodes.value = [...new Set(roles.filter((role) => user.roleIds.includes(role.id)).flatMap((role) => role.permissionCodes))]
  }

  async function login(document: string, password: string) {
    const result = await authService.login(document, password)
    if (result.status === 'success') {
      currentUser.value = result.user
      pendingUser.value = null
      await hydratePermissions(result.user)
      persistSession()
    } else if (result.status === 'first-login') {
      pendingUser.value = result.user
      currentUser.value = null
      permissionCodes.value = []
      persistSession()
    }
    return result
  }

  async function completeFirstLogin(currentPassword: string, newPassword: string) {
    if (!pendingUser.value) return false
    const success = await authService.changePassword(pendingUser.value.id, currentPassword, newPassword)
    if (success) {
      pendingUser.value = null
      persistSession()
    }
    return success
  }

  async function changeOwnPassword(currentPassword: string, newPassword: string) {
    if (!currentUser.value) return false
    return authService.changePassword(currentUser.value.id, currentPassword, newPassword)
  }

  function hasPermission(code?: string) {
    return !code || permissionCodes.value.includes(code)
  }

  function logout() {
    currentUser.value = null
    pendingUser.value = null
    permissionCodes.value = []
    sessionStorage.removeItem(storageKey)
  }

  return { currentUser, pendingUser, permissionCodes, isAuthenticated, fullName, login, completeFirstLogin, changeOwnPassword, hasPermission, logout }
})
