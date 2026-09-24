import { describe, expect, it } from 'vitest'
import { visibleSidebarItems } from './sidebarItems'

describe('sidebar permission navigation', () => {
  it('keeps Auditoría hidden even with audit.view', () => {
    const withoutAudit = visibleSidebarItems((permission) => !permission || permission === 'areas.view')
    expect(withoutAudit.map((item) => item.to)).not.toContain('/audit')

    const withAudit = visibleSidebarItems((permission) => !permission || permission === 'audit.view')
    expect(withAudit.map((item) => item.to)).not.toContain('/audit')
  })

  it('does not expose the removed simulated email module', () => {
    const items = visibleSidebarItems(() => true)
    expect(items.map((item) => item.to)).not.toContain('/demo/emails')
    expect(items.map((item) => item.label)).not.toContain('Correos simulados')
  })
})
