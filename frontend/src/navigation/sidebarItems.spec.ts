import { describe, expect, it } from 'vitest'
import { visibleSidebarItems } from './sidebarItems'

describe('sidebar permission navigation', () => {
  it('shows Auditoría only with audit.view', () => {
    const withoutAudit = visibleSidebarItems((permission) => !permission || permission === 'areas.view')
    expect(withoutAudit.map((item) => item.to)).not.toContain('/audit')

    const withAudit = visibleSidebarItems((permission) => !permission || permission === 'audit.view')
    expect(withAudit.map((item) => item.to)).toContain('/audit')
    expect(withAudit.find((item) => item.to === '/audit')?.label).toBe('Auditoría')
  })
})
