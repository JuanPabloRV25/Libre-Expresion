export type NotificationStatus = 'sent' | 'failed'

export const userCreatedNotificationMessage = (status: NotificationStatus) => status === 'sent'
  ? 'El usuario fue creado exitosamente. Se envió la información de acceso al correo registrado.'
  : 'El usuario fue creado exitosamente, pero no fue posible enviar la notificación.'

export const passwordResetNotificationMessage = (status: NotificationStatus) => status === 'sent'
  ? 'La contraseña fue restablecida exitosamente. Se envió la información de acceso al correo registrado.'
  : 'La contraseña fue restablecida, pero no fue posible enviar la notificación.'

export const passwordChangedNotificationMessage = (status: NotificationStatus) => status === 'sent'
  ? 'Su contraseña fue actualizada correctamente. Se envió una notificación a su correo electrónico.'
  : 'Su contraseña fue actualizada correctamente, pero no fue posible enviar la notificación.'

export function isNotificationStatus(value: unknown): value is NotificationStatus {
  return value === 'sent' || value === 'failed'
}
