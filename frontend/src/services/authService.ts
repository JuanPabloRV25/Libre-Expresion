import { ApiError, clearAntiforgeryToken, getJson, postJson } from '../api/httpClient'
import type { AuthenticatedUser, LoginResult } from '../types/models'
import type { NotificationStatus } from './notificationMessages'

interface LoginResponse {
  authenticated: boolean
  mustChangePassword: boolean
}

export const authService = {
  async login(documentNumber: string, password: string): Promise<LoginResult> {
    try {
      const login = await postJson<LoginResponse>('/auth/login', { documentNumber, password })
      clearAntiforgeryToken()
      const user = await getJson<AuthenticatedUser>('/auth/me')
      return { status: login.mustChangePassword ? 'first-login' : 'success', user }
    } catch (error) {
      if (error instanceof ApiError) {
        if (error.code === 'inactive_account') return { status: 'inactive' }
        if (error.code === 'account_locked') return { status: 'locked' }
        if (error.code === 'invalid_credentials') return { status: 'invalid' }
      }
      throw error
    }
  },
  me: () => getJson<AuthenticatedUser>('/auth/me'),
  async logout() {
    await postJson<{ succeeded: boolean }>('/auth/logout')
    clearAntiforgeryToken()
  },
  async changeRequiredPassword(newPassword: string, confirmPassword: string) {
    const result = await postJson<{ succeeded: boolean; notificationStatus: NotificationStatus }>('/auth/change-required-password', { newPassword, confirmPassword })
    clearAntiforgeryToken()
    return result.notificationStatus
  },
  async resetPassword(userId: string, token: string, newPassword: string, confirmPassword: string) {
    const result = await postJson<{ passwordReset: boolean; notificationStatus: NotificationStatus }>('/auth/reset-password', {
      userId, token, newPassword, confirmPassword,
    })
    clearAntiforgeryToken()
    return result.notificationStatus
  },

  async changePassword(currentPassword: string, newPassword: string, confirmPassword: string) {
    const result = await postJson<{ succeeded: boolean; notificationStatus: NotificationStatus }>('/auth/change-password', { currentPassword, newPassword, confirmPassword })
    clearAntiforgeryToken()
    return result.notificationStatus
  },
}
