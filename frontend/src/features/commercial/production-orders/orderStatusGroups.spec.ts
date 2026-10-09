import { describe, expect, it } from 'vitest'
import { matchesOrderStatusGroup, normalizeOrderStatusGroup, orderStatusGroupOptions } from './orderStatusGroups'

describe('commercial order status groups', () => {
  it('exposes exactly three understandable filters', () => {
    expect(orderStatusGroupOptions.map((option) => option.label)).toEqual([
      'Todas las OP',
      'En proceso',
      'Finalizadas o anuladas',
    ])
  })

  it('groups every operational state without hiding orders', () => {
    expect(matchesOrderStatusGroup('draft', 'active')).toBe(true)
    expect(matchesOrderStatusGroup('pendingCommercialReview', 'active')).toBe(true)
    expect(matchesOrderStatusGroup('correctionRequired', 'active')).toBe(true)
    expect(matchesOrderStatusGroup('readyForProduction', 'active')).toBe(true)
    expect(matchesOrderStatusGroup('inProduction', 'active')).toBe(true)
    expect(matchesOrderStatusGroup('completed', 'closed')).toBe(true)
    expect(matchesOrderStatusGroup('cancelled', 'closed')).toBe(true)
  })

  it('keeps old status links compatible with the simplified filter', () => {
    expect(normalizeOrderStatusGroup('draft')).toBe('active')
    expect(normalizeOrderStatusGroup('completed')).toBe('closed')
    expect(normalizeOrderStatusGroup(undefined)).toBe('all')
  })
})
