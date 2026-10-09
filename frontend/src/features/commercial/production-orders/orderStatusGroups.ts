import type { ProductionOrderStatus } from './types'

export type OrderStatusGroup = 'all' | 'active' | 'closed'

export const orderStatusGroupOptions: Array<{ value: OrderStatusGroup; label: string }> = [
  { value: 'all', label: 'Todas las OP' },
  { value: 'active', label: 'En proceso' },
  { value: 'closed', label: 'Finalizadas o anuladas' },
]

export function normalizeOrderStatusGroup(value: unknown): OrderStatusGroup {
  if (value === 'active' || value === 'closed') return value
  if (value === 'completed' || value === 'cancelled') return 'closed'
  if (typeof value === 'string' && value.length > 0) return 'active'
  return 'all'
}

export function matchesOrderStatusGroup(status: ProductionOrderStatus, group: OrderStatusGroup) {
  if (group === 'all') return true
  const closed = status === 'completed' || status === 'cancelled'
  return group === 'closed' ? closed : !closed
}
