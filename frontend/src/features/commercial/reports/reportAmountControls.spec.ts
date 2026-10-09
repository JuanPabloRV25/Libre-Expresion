import { describe, expect, it } from 'vitest'
import { createSSRApp } from 'vue'
import { renderToString, type SSRContext } from 'vue/server-renderer'
import ReportExceptionCase from './ReportExceptionCase.vue'
import ReportRowEditor from './ReportRowEditor.vue'
import type { ReviewCase, SalesGroup, SalesReport } from './types'

const row = (amountMode?: SalesGroup['amountMode'], amount: number | null = 123.45): SalesGroup => ({
  key: 'sale-row', documentId: 'sale', number: '00042', date: '2026-10-08', op: amountMode === 'manual' ? '123/456' : '123',
  product: 'Producto', client: 'Cliente', term: '30', amount, factura: 'F01', line: 'Línea', seller: 'Asesor',
  details: ['Primer detalle', 'Segundo detalle'], detailIds: ['a', 'b'], issues: [], modified: false,
  ...(amountMode ? { amountMode, opCount: amountMode === 'manual' ? 2 : 1 } : {}),
})
const item = (code = 'amount_undetermined'): ReviewCase => ({
  id: 'case', rowKey: 'sale-row', scope: 'row', classification: 'validation', findings: [{
    id: 'amount-finding', code, field: 'VALOR_BRUT', rule: 'confirmed_amount_rule', initialClassification: 'validation',
    resolution: 'pending', provenance: 'automatic', proposal: '', reason: 'Importe pendiente', detailIds: ['a', 'b'], evidence: [], allowedActions: [],
  }],
})
function report(saved: SalesGroup, reviewCase: ReviewCase): SalesReport {
  return {
    id: 'report', name: 'Informe de ventas mensual', version: 1, updatedAt: '', lastExportedVersion: null,
    groups: [saved], controls: [], canExport: false,
    data: {
      currentSourceId: 'source', sourceFile: 'Manager.xlsx', sha256: '', details: [], documents: [], edits: [], unappliedEdits: [], warnings: [],
      preparation: {
        ruleVersion: saved.amountMode ? 4 : 3, status: 'generated_with_observations', historyFingerprint: '', history: [],
        automaticRows: [saved], rows: [saved], changes: [], rowEdits: [], review: {
          schemaVersion: 1, policyVersion: 1, cases: [reviewCase], decisions: [], approvals: [], summary: {
            sourceRows: 2, finalRows: 1, automaticRows: 0, humanResolvedRows: 0, validationRows: 1,
            conflictRows: 0, filePendingCases: 0, pendingCases: 1, pendingFindings: 1,
          },
        },
      },
    },
  }
}
function input(html: string, label: string): string {
  const match = html.match(new RegExp(`<input\\b[^>]*aria-label="${label}"[^>]*>`))
  expect(match, `Input ${label} must be present`).not.toBeNull()
  return match![0]
}
async function renderEditor(saved: SalesGroup): Promise<string> {
  const context: SSRContext = {}
  await renderToString(createSSRApp(ReportRowEditor, { report: report(saved, item()), row: saved }), context)
  return context.teleports?.body ?? ''
}
async function renderIncidence(saved: SalesGroup, code?: string): Promise<string> {
  const reviewCase = item(code)
  return renderToString(createSSRApp(ReportExceptionCase, { report: report(saved, reviewCase), item: reviewCase, index: 0, total: 1, editable: true }))
}

describe('saved amount controls', () => {
  it('renders a single OP Manager amount as read-only while FACTURA remains available to edit', async () => {
    for (const html of [await renderEditor(row('automatic')), await renderIncidence(row('automatic'))]) {
      const label = html.includes('aria-label="VALOR_BRUT"') ? 'VALOR_BRUT' : 'Consultar VALOR_BRUT'
      expect(input(html, label)).toContain('readonly')
      expect(input(html, label)).toContain('value="123.45"')
      if (label === 'VALOR_BRUT') {
        const factura = input(html, 'FACTURA')
        expect(factura).toContain('value="F01"')
        expect(factura).not.toContain('readonly')
      } else {
        expect(html).toContain('F01')
        expect(html).toContain('aria-label="Editar FACTURA del caso actual"')
        expect(html).not.toContain('aria-label="FACTURA del caso actual"')
      }
      expect(html).toContain('Automático · 1 OP')
    }
  })
  it('renders the several-OP total empty and editable in both exact incidence and row editing', async () => {
    for (const html of [await renderEditor(row('manual', null)), await renderIncidence(row('manual', null))]) {
      const label = html.includes('aria-label="VALOR_BRUT"') ? 'VALOR_BRUT' : 'Corregir VALOR_BRUT'
      expect(input(html, label)).not.toContain('readonly')
      expect(input(html, label)).toContain('value=""')
      expect(html).toContain('Manual · varias OP')
      expect(html).toContain('Escribe el total correspondiente')
    }
  })
  it('explains differing one-OP Manager values without offering a replacement or confirmation', async () => {
    for (const html of [await renderEditor(row('validation', null)), await renderIncidence(row('validation', null))]) {
      const label = html.includes('aria-label="VALOR_BRUT"') ? 'VALOR_BRUT' : 'Consultar VALOR_BRUT'
      expect(input(html, label)).toContain('readonly')
      expect(input(html, label)).toContain('value=""')
      expect(html).toContain('Manager contiene importes diferentes para una sola OP')
      expect(html).not.toContain('Mantener este importe')
    }
  })
  it('only offers unchanged automatic amount confirmation for its pending shared amount observation', async () => {
    expect(await renderIncidence(row('automatic'), 'shared_amount_observation')).toContain('Mantener este importe')
    expect(await renderIncidence(row('automatic'), 'amount_undetermined')).not.toContain('Mantener este importe')
  })
  it('preserves editable amount controls for older saved rows without amount metadata', async () => {
    const html = await renderEditor(row())
    expect(input(html, 'VALOR_BRUT')).not.toContain('readonly')
    expect(html).not.toContain('Automático · 1 OP')
    expect(input(await renderIncidence(row()), 'Corregir VALOR_BRUT')).not.toContain('readonly')
  })
})
