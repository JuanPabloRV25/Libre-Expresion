import { describe, expect, it } from 'vitest'
import {
  passwordChangedNotificationMessage,
  passwordResetNotificationMessage,
  userCreatedNotificationMessage,
} from './notificationMessages'

describe('notification result messages', () => {
  it('maps sent and failed user creation results', () => {
    expect(userCreatedNotificationMessage('sent')).toContain('enlace para establecer su contraseña')
    expect(userCreatedNotificationMessage('failed')).toContain('Puedes reenviarlo')
  })

  it('maps sent and failed password reset results', () => {
    expect(passwordResetNotificationMessage('sent')).toContain('enlace para establecer una nueva contraseña')
    expect(passwordResetNotificationMessage('failed')).toContain('Intenta enviarlo nuevamente')
  })

  it('maps sent and failed password change results', () => {
    expect(passwordChangedNotificationMessage('sent')).toBe('Su contraseña fue actualizada correctamente. Se envió una notificación a su correo electrónico.')
    expect(passwordChangedNotificationMessage('failed')).toBe('Su contraseña fue actualizada correctamente, pero no fue posible enviar la notificación.')
  })
})
