import { mockAuthProvider } from '../mocks/providers'

export const authService = {
  login: (document: string, password: string) => mockAuthProvider.login(document, password),
  changePassword: (userId: number, currentPassword: string, newPassword: string) => mockAuthProvider.changePassword(userId, currentPassword, newPassword),
}

