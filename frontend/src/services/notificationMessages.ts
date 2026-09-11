export type NotificationStatus = 'sent' | 'failed'

export const userCreatedNotificationMessage = (status: NotificationStatus) => status === 'sent'
  ? 'El usuario fue creado exitosamente. Se envió al correo registrado el enlace para establecer su contraseña.'
  : 'El usuario fue creado, pero no fue posible enviar el enlace. Puedes reenviarlo desde Restablecer contraseña.'

export const passwordResetNotificationMessage = (status: NotificationStatus) => status === 'sent'
  ? 'Se envió al correo registrado el enlace para establecer una nueva contraseña.'
  : 'El acceso quedó pendiente, pero no fue posible enviar el enlace. Intenta enviarlo nuevamente.'

export const passwordChangedNotificationMessage = (status: NotificationStatus) => status === 'sent'
  ? 'Su contraseña fue actualizada correctamente. Se envió una notificación a su correo electrónico.'
  : 'Su contraseña fue actualizada correctamente, pero no fue posible enviar la notificación.'

export function isNotificationStatus(value: unknown): value is NotificationStatus {
  return value === 'sent' || value === 'failed'
}
