import { describe, expect, it } from 'vitest'
import { visibleSidebarGroups, visibleSidebarItems } from './sidebarItems'

describe('sidebar permission navigation', () => {
  it('keeps every Phase 1 module inside Administration', () => {
    const administration = visibleSidebarGroups(() => true).find((group) => group.id === 'administration')
    expect(administration?.items.map((item) => 'to' in item ? item.to : undefined)).toEqual([
      '/users', '/areas', '/roles', '/permissions', '/audit',
    ])
  })

  it('shows only production orders while the commercial summary is temporarily hidden', () => {
    const groups = visibleSidebarGroups((permission) => !permission || permission === 'areas.view')
    expect(groups.map((group) => group.id)).toEqual(['administration'])

    const commercial = visibleSidebarGroups((permission) => permission === 'commercial.production_orders.view')
    expect(commercial.map((group) => group.id)).toEqual(['commercial'])
    expect(commercial[0].items.map((item) => 'to' in item ? item.to : undefined)).toEqual(['/commercial/production-orders'])
  })

  it('does not expose the removed simulated email module', () => {
    const items = visibleSidebarItems(() => true)
    expect(items.map((item) => item.to)).not.toContain('/demo/emails')
  })

  it('shows the Reports folder and its two submodules only with its own permission', () => {
    const commercial = visibleSidebarGroups(p => p === 'commercial.reports.view')
    expect(commercial.map(g => g.id)).toEqual(['commercial'])
    expect(commercial[0].items).toHaveLength(1)
    const reports = commercial[0].items[0]
    expect(reports.label).toBe('Reportes')
    expect('to' in reports).toBe(false)
    expect('children' in reports && reports.children.map(item => [item.label, item.to, item.permission])).toEqual([
      ['Informe Mensual', '/commercial/reports/monthly', 'commercial.reports.view'],
      ['Informe OPs', '/commercial/reports/ops', 'commercial.reports.view'],
    ])
    expect(visibleSidebarItems(p => p === 'commercial.reports.view').map(item => item.to)).toEqual([
      '/commercial/reports/monthly', '/commercial/reports/ops',
    ])
    expect(visibleSidebarItems(p => p === 'commercial.production_orders.view').map(item => item.to)).not.toContain('/commercial/reports/ops')
  })
})
