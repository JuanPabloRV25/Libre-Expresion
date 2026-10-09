import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
const { chromium } = await import(process.env.PORTAL_PLAYWRIGHT_MODULE ?? 'playwright')
const browser = await chromium.launch({ headless: true, channel: process.env.PORTAL_BROWSER_CHANNEL || undefined })
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
const errors = []; page.on('pageerror', error => errors.push(error.message))
const base = process.env.PORTAL_TEST_FRONTEND_URL ?? 'http://127.0.0.1:5178'
const id = '11111111-1111-1111-1111-111111111111'
const userId = '33333333-3333-3333-3333-333333333333'
const details = Array.from({ length: 23 }, (_, index) => {
  const n = index + 1
  return { id: String(n), sourceId: id, sourceFile: 'Manager ficticio.xlsx', sheet: 'Hoja1', sourceRow: n + 1,
    number: String(8000 + n), date: '2026-05-04', client: `Cliente de prueba ${n}`, term: 'CONTADO', rawAmount: 100,
    detail: `Detalle ficticio ${n}`, line: 'EMPAQUE', seller: 'Vendedor de prueba', managerOp: String(25000 + n),
    documentId: `doc-${n}`, matchKey: String(n), selectedOpId: null, selectedOpNumber: String(25000 + n),
    selectedProduct: 'Etiqueta', usePortalCode: false, manualGroup: null, reviewed: false, excluded: false, reason: '' }
})
let report = { id, name: 'Prueba de paginación', version: 1, updatedAt: '2026-10-06T12:00:00Z', lastExportedVersion: null,
  data: { currentSourceId: id, sourceFile: 'Manager ficticio.xlsx', sha256: 'test', details,
    documents: details.map((detail, index) => ({ id: detail.documentId, number: detail.number, client: detail.client,
      sourceAmount: 100, confirmedAmount: index === 1 ? 100 : null, reason: '' })), edits: [], unappliedEdits: [], warnings: [] },
  groups: details.map((detail, index) => ({ key: `g${index + 1}`, documentId: detail.documentId, number: detail.number,
    date: detail.date, op: detail.selectedOpNumber, product: 'Etiqueta', client: detail.client, term: detail.term,
    amount: 100, factura: '', line: 'EMPAQUE', seller: detail.seller, details: [detail.detail], detailIds: [detail.id],
    issues: ['Completa FACTURA y confirma la OP.'], modified: false })),
  controls: details.map((detail, index) => ({ id: detail.documentId, number: detail.number, client: detail.client,
    expected: index === 1 ? 100 : null, distributed: 100, difference: index === 1 ? 0 : null })), canExport: false }
let savedPayload
await page.route('**/api/**', async route => {
  const path = new URL(route.request().url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
  if (path === '/api/auth/me') return json({ id: userId, documentNumber: 'TEST', firstName: 'Auxiliar', lastName: 'Prueba',
    email: 'auxiliar@example.test', area: null, roles: ['Auxiliar Comercial'], availableRoles: [{ id: userId, name: 'Auxiliar Comercial' }],
    activeRoleIds: [userId], permissions: ['commercial.reports.view', 'commercial.reports.edit', 'commercial.reports.export'],
    isActive: true, mustChangePassword: false })
  if (path === '/api/auth/csrf') return json({ token: 'test-csrf' })
  if (path === '/api/commercial/reports/ops') return json([])
  if (path === `/api/commercial/reports/${id}` && route.request().method() === 'PUT') {
    savedPayload = route.request().postDataJSON()
    report = structuredClone(report); report.version++
    report.data.edits = savedPayload.edits
    for (const doc of report.data.documents) Object.assign(doc, savedPayload.documents.find(item => item.id === doc.id))
    for (const control of report.controls) {
      control.expected = report.data.documents.find(doc => doc.id === control.id).confirmedAmount
      control.difference = control.expected === null ? null : control.distributed - control.expected
    }
    return json(report)
  }
  if (path === `/api/commercial/reports/${id}`) return json(report)
  return json([])
})
const records = page.getByRole('region', { name: 'Informe de ventas', exact: true })
const amounts = page.getByRole('region', { name: 'Informe de OPs', exact: true })
const recordPages = records.getByRole('navigation', { name: 'Páginas del informe de ventas', exact: true })
const amountPages = amounts.getByRole('navigation', { name: 'Páginas del informe de OPs', exact: true })
const tabs = page.getByRole('navigation', { name: 'Secciones de revisión', exact: true })
async function range(nav, text) { await nav.getByText(text, { exact: true }).waitFor() }
async function jump(nav, value) {
  await nav.getByRole('combobox').selectOption(String(value))
}
async function screenshot(name) {
  const directory = process.env.PORTAL_SCREENSHOT_DIR
  if (!directory) return
  await fs.mkdir(directory, { recursive: true })
  await tabs.scrollIntoViewIfNeeded()
  await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true, animations: 'disabled' })
}
try {
  await page.goto(`${base}/commercial/reports/ventas/${id}`)
  await range(recordPages, '1–5 de 23 · 5 por página')
  assert.equal(await records.locator('tbody tr').count(), 5)
  assert.equal(await recordPages.locator('.reports-page-number').count(), 5)
  assert.equal(await recordPages.locator('[aria-current=page]').getAttribute('aria-label'), 'Página 1')
  assert.equal(await page.locator('.reports-amount-table').isVisible(), false)
  await recordPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await range(recordPages, '6–10 de 23 · 5 por página')
  await recordPages.getByRole('button', { name: 'Página 5', exact: true }).click(); await range(recordPages, '21–23 de 23 · 5 por página')
  assert.equal(await records.locator('tbody tr').count(), 3)
  assert.equal(await recordPages.getByRole('button', { name: 'Siguiente', exact: true }).isDisabled(), true)
  await records.getByRole('textbox').fill('Cliente de prueba 23')
  await range(recordPages, '1–1 de 1 · 5 por página')
  await records.getByRole('textbox').fill('')
  await range(recordPages, '1–5 de 23 · 5 por página')
  await records.getByRole('button', { name: 'Revisar / editar', exact: true }).first().click()
  await page.getByRole('region', { name: 'Revisar registro', exact: true }).getByRole('tab', { name: 'Datos del Excel', exact: true }).click()
  await page.getByLabel('FACTURA · Manual', { exact: true }).fill('BORRADOR-CONSERVADO')
  await recordPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await range(recordPages, '6–10 de 23 · 5 por página')
  assert.equal(await page.getByRole('region', { name: 'Revisar registro', exact: true }).count(), 0)
  await jump(recordPages, 1); await range(recordPages, '1–5 de 23 · 5 por página')
  await records.getByRole('button', { name: 'Revisar / editar', exact: true }).first().click()
  await page.getByRole('region', { name: 'Revisar registro', exact: true }).getByRole('tab', { name: 'Datos del Excel', exact: true }).click()
  assert.equal(await page.getByLabel('FACTURA · Manual', { exact: true }).inputValue(), 'BORRADOR-CONSERVADO')
  await page.getByRole('region', { name: 'Revisar registro', exact: true }).getByRole('button', { name: 'Cerrar revisión del registro', exact: true }).click()
  await screenshot('reports-pagination-records-desktop')
  assert.equal(await records.locator('.reports-table-scroll').evaluate(el => el.clientHeight <= 560), true)
  await tabs.getByRole('button', { name: /Informe de OPs/ }).click()
  await range(amountPages, '1–5 de 23 · 5 por página')
  assert.equal(await amounts.locator('tbody tr').count(), 5)
  assert.equal(await amountPages.locator('.reports-page-number').count(), 5)
  await amountPages.getByRole('button', { name: 'Página 3', exact: true }).click()
  await range(amountPages, '11–15 de 23 · 5 por página')
  assert.equal(await amountPages.locator('[aria-current=page]').getAttribute('aria-label'), 'Página 3')
  await amountPages.getByRole('button', { name: 'Página 1', exact: true }).click()
  assert.equal(await records.isVisible(), false)
  await amountPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await range(amountPages, '6–10 de 23 · 5 por página')
  await jump(amountPages, 5); await range(amountPages, '21–23 de 23 · 5 por página')
  assert.equal(await amounts.locator('tbody tr').count(), 3)
  assert.equal(await amountPages.getByRole('button', { name: 'Siguiente', exact: true }).isDisabled(), true)
  await amounts.getByRole('textbox').fill('Cliente de prueba 23')
  await range(amountPages, '1–1 de 1 · 5 por página')
  await amounts.getByRole('textbox').fill('')
  await amounts.locator('.reports-toolbar').getByRole('combobox').selectOption('checked')
  await range(amountPages, '1–1 de 1 · 5 por página')
  await amounts.locator('.reports-toolbar').getByRole('combobox').selectOption('all')
  await range(amountPages, '1–5 de 23 · 5 por página')
  await amounts.getByRole('button', { name: 'Revisar importe', exact: true }).first().click()
  await page.getByLabel('Total confirmado', { exact: true }).fill('90')
  await page.getByLabel('Explicación si el total cambia', { exact: true }).fill('Ajuste ficticio para comprobar el borrador')
  await amountPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await range(amountPages, '6–10 de 23 · 5 por página')
  assert.equal(await page.getByRole('region', { name: 'Revisar importe', exact: true }).count(), 0)
  await jump(amountPages, 1); await range(amountPages, '1–5 de 23 · 5 por página')
  await amounts.getByRole('button', { name: 'Revisar importe', exact: true }).first().click()
  assert.equal(await page.getByLabel('Total confirmado', { exact: true }).inputValue(), '90')
  await tabs.getByRole('button', { name: /Informe de ventas/ }).click()
  await records.getByRole('button', { name: 'Revisar / editar', exact: true }).first().click()
  await page.getByRole('region', { name: 'Revisar registro', exact: true }).getByRole('tab', { name: 'Datos del Excel', exact: true }).click()
  assert.equal(await page.getByLabel('FACTURA · Manual', { exact: true }).inputValue(), 'BORRADOR-CONSERVADO')
  await page.getByRole('button', { name: 'Guardar reporte', exact: true }).first().click()
  await page.getByText('Reporte guardado. Puedes continuar después.', { exact: true }).waitFor()
  assert.equal(savedPayload.details.length, 23)
  assert.equal(savedPayload.documents.length, 23)
  assert.equal(savedPayload.edits.find(edit => edit.key === 'g1').factura, 'BORRADOR-CONSERVADO')
  assert.equal(savedPayload.documents.find(doc => doc.id === 'doc-1').confirmedAmount, 90)
  assert.equal(savedPayload.documents.find(doc => doc.id === 'doc-1').reason, 'Ajuste ficticio para comprobar el borrador')
  await tabs.getByRole('button', { name: /Informe de OPs/ }).click()
  await screenshot('reports-pagination-amounts-desktop')
  await page.setViewportSize({ width: 390, height: 844 })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true)
  assert.equal(await amountPages.evaluate(el => {
    const parent = el.getBoundingClientRect()
    return [...el.querySelectorAll('button, select')].every(child => {
      const bounds = child.getBoundingClientRect()
      return bounds.width === 0 || (bounds.left >= parent.left && bounds.right <= parent.right + 1)
    })
  }), true)
  await screenshot('reports-pagination-mobile')
  await amountPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await range(amountPages, '6–10 de 23 · 5 por página')
  const largeDetails = Array.from({ length: 103 }, (_, index) => ({ ...report.data.details[0], id: `large-${index + 1}`,
    documentId: `large-doc-${index + 1}`, number: String(9000 + index), client: `Cliente ficticio ${index + 1}` }))
  report.data.details = largeDetails
  report.data.edits = []
  report.data.documents = largeDetails.map(detail => ({ ...report.data.documents[0], id: detail.documentId,
    number: detail.number, client: detail.client }))
  report.groups = largeDetails.map(detail => ({ ...report.groups[0], key: detail.id, documentId: detail.documentId,
    number: detail.number, client: detail.client, detailIds: [detail.id] }))
  report.controls = largeDetails.map(detail => ({ ...report.controls[0], id: detail.documentId,
    number: detail.number, client: detail.client }))
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.reload()
  await range(recordPages, '1–5 de 103 · 5 por página')
  await jump(recordPages, 11); await range(recordPages, '51–55 de 103 · 5 por página')
  assert.equal(await records.locator('tbody tr').count(), 5)
  assert.equal(await recordPages.locator('[aria-current=page]').getAttribute('aria-label'), 'Página 11')
  assert.equal(await recordPages.locator('.reports-page-gap').count(), 2)
  await tabs.getByRole('button', { name: /Informe de OPs/ }).click()
  await amountPages.getByRole('button', { name: 'Página 21', exact: true }).click()
  await range(amountPages, '101–103 de 103 · 5 por página')
  assert.equal(await amounts.locator('tbody tr').count(), 3)
  assert.equal(await amountPages.getByRole('button', { name: 'Siguiente', exact: true }).isDisabled(), true)
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true)
  report.data.details = report.data.details.slice(0, 35)
  report.data.documents = report.data.documents.slice(0, 35)
  report.groups = report.groups.slice(0, 35)
  report.controls = report.controls.slice(0, 35)
  await page.reload()
  await range(recordPages, '1–5 de 35 · 5 por página')
  for (const width of [320, 381, 390]) {
    await page.setViewportSize({ width, height: 844 })
    await tabs.getByRole('button', { name: /Informe de ventas/ }).click()
    await jump(recordPages, 4); await range(recordPages, '16–20 de 35 · 5 por página')
    assert.equal(await records.locator('tbody tr').count(), 5)
    await tabs.getByRole('button', { name: /Informe de OPs/ }).click()
    await jump(amountPages, 7); await range(amountPages, '31–35 de 35 · 5 por página')
    assert.equal(await amounts.locator('tbody tr').count(), 5)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true)
    assert.equal(await amountPages.evaluate(el => {
      const parent = el.getBoundingClientRect()
      return [...el.querySelectorAll('button, select')].every(child => {
        const bounds = child.getBoundingClientRect()
        return bounds.width === 0 || (bounds.left >= parent.left && bounds.right <= parent.right + 1)
      })
    }), true)
  }
  assert.deepEqual(errors, [])
  console.log('PASS: ambas secciones paginadas a 5, páginas numeradas, salto y filtros, borradores y guardado completo de 23 registros, navegación de 103 registros, escritorio y móvil sin errores.')
} catch (error) {
  console.error('Browser diagnostic:', page.url(), await page.locator('body').innerText(), errors)
  console.error('Layout diagnostic:', await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth,
    outside: [...document.querySelectorAll('body *')].filter(el => el.getBoundingClientRect().right > window.innerWidth + 1).slice(0, 12).map(el => ({ tag: el.tagName, class: el.className, width: el.getBoundingClientRect().width, right: el.getBoundingClientRect().right })) })))
  throw error
} finally { await browser.close() }
