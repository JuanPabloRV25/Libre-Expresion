import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
const { chromium } = await import(process.env.PORTAL_PLAYWRIGHT_MODULE ?? 'playwright')
const browser = await chromium.launch({ headless: true, channel: process.env.PORTAL_BROWSER_CHANNEL || undefined })
const page = await browser.newPage({ viewport: { width: 1366, height: 900 } })
const errors = []; page.on('pageerror', e => errors.push(e.message))
const base = process.env.PORTAL_TEST_FRONTEND_URL ?? 'http://127.0.0.1:5178'
const id = '11111111-1111-1111-1111-111111111111', opId = '22222222-2222-2222-2222-222222222222'
const userId = '33333333-3333-3333-3333-333333333333'
const op = { id: opId, number: '25280', code: '', client: 'Cliente de prueba', product: 'Etiqueta', orderVersion: null,
  capturedAt: '2026-10-05T12:00:00Z', origin: 'Registro histórico', data: { cells: ['25280'], portalCode: null } }
const details = [1, 2, 3].map(n => ({ id: String(n), sourceId: id, sourceFile: 'Manager.xlsx', sheet: 'Hoja1', sourceRow: n + 1,
  number: '8877', date: '2026-05-04', client: 'Cliente de prueba', term: 'CONTADO', rawAmount: 823500,
  detail: `Etiqueta detalle ${n}`, line: 'EMPAQUE', seller: 'Vendedor de prueba', managerOp: '25280', documentId: 'doc', matchKey: String(n),
  selectedOpId: opId, selectedOpNumber: '25280', selectedProduct: 'Etiqueta', usePortalCode: false,
  manualGroup: null, reviewed: false, excluded: false, reason: '' }))
let report = { id, name: 'Ventas — Manager.xlsx', version: 1, updatedAt: '2026-10-05T12:00:00Z', lastExportedVersion: null,
  data: { currentSourceId: id, sourceFile: 'Manager.xlsx', sha256: 'test', details,
    documents: [{ id: 'doc', number: '8877', client: 'Cliente de prueba', sourceAmount: 823500, confirmedAmount: null, reason: '' }],
    edits: [], unappliedEdits: [], warnings: ['VALOR_BRUT puede repetirse entre detalles.'] },
  groups: [{ key: 'g1', documentId: 'doc', number: '8877', date: '2026-05-04', op: '25280', product: 'Etiqueta', client: 'Cliente de prueba', term: 'CONTADO',
    amount: null, factura: '', line: 'EMPAQUE', seller: 'Vendedor de prueba', details: details.map(d => d.detail), detailIds: ['1', '2', '3'],
    issues: ['Confirma las OP, completa FACTURA e importe.'], modified: false }],
  controls: [{ id: 'doc', number: '8877', client: 'Cliente de prueba', expected: null, distributed: 0, difference: null }], canExport: false }
let saves = 0, exports = 0
await page.route('**/api/**', async route => {
  const path = new URL(route.request().url()).pathname, method = route.request().method()
  if (!path.startsWith('/api/')) return route.continue()
  const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
  if (path === '/api/auth/me') return json({ id: userId, documentNumber: 'TEST', firstName: 'Auxiliar', lastName: 'Prueba', email: 'auxiliar@example.test', area: null,
    roles: ['Auxiliar Comercial'], availableRoles: [{ id: userId, name: 'Auxiliar Comercial' }], activeRoleIds: [userId],
    permissions: ['commercial.reports.view', 'commercial.reports.edit', 'commercial.reports.export'], isActive: true, mustChangePassword: false })
  if (path === '/api/auth/csrf') return json({ token: 'test-csrf' })
  if (path === '/api/commercial/reports/ops') return json([op])
  if (path === '/api/commercial/reports' && method === 'GET') return json([report])
  if (path === '/api/commercial/reports' && method === 'POST') return json(report)
  if (path === `/api/commercial/reports/${id}` && method === 'GET') return json(report)
  if (path === `/api/commercial/reports/${id}` && method === 'PUT') {
    const body = route.request().postDataJSON(); assert.equal(route.request().headers()['x-xsrf-token'], 'test-csrf')
    assert.equal(body.details.length, 3); assert.ok(body.details.every(d => d.reviewed)); assert.equal(body.edits[0].factura, 'MANUAL')
    assert.equal(body.edits[0].amount, 823500); assert.equal(body.documents[0].confirmedAmount, 823500); saves++
    report = structuredClone(report); report.version++; report.data.edits = body.edits; report.data.documents[0].confirmedAmount = 823500
    report.data.details.forEach(d => { d.reviewed = true }); report.groups[0].factura = 'MANUAL'; report.groups[0].amount = 823500
    report.groups[0].issues = []; report.groups[0].modified = true; report.controls[0] = { ...report.controls[0], expected: 823500, distributed: 823500, difference: 0 }; report.canExport = true
    return json(report)
  }
  if (path.endsWith('/export') && method === 'POST') { exports++; assert.equal(route.request().headers()['x-xsrf-token'], 'test-csrf'); return route.fulfill({ contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', body: Buffer.from('TEST ONLY') }) }
  return json([])
})
try {
  await page.goto(`${base}/commercial/reports`)
  await page.getByRole('heading', { name: 'Reportes', exact: true }).waitFor()
  assert.equal(await page.locator('input[type=month]').count(), 0)
  await page.locator('#manager-file').setInputFiles({ name: 'Manager.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: Buffer.from('TEST ONLY') })
  await page.getByRole('button', { name: 'Preparar reporte', exact: true }).click()
  await page.getByRole('heading', { name: 'Reporte de ventas', exact: true }).waitFor()
  await page.getByRole('button', { name: 'Revisar / editar', exact: true }).click()
  const editor = page.getByRole('region', { name: 'Revisar registro', exact: true })
  for (let i = 1; i <= 3; i++) {
    await editor.getByRole('button', { name: `Ver detalle ${i}`, exact: true }).click()
    await editor.getByRole('article', { name: `Revisar detalle ${i}`, exact: true }).getByText(`Etiqueta detalle ${i}`, { exact: true }).waitFor()
    await editor.getByRole('button', { name: 'Confirmar OP y producto', exact: true }).click()
  }
  await editor.getByRole('tab', { name: 'Datos del Excel', exact: true }).click()
  await page.getByLabel('FACTURA · Manual', { exact: true }).fill('MANUAL')
  await page.getByLabel('Importe del registro', { exact: true }).fill('823500')
  await page.getByRole('navigation', { name: 'Secciones de revisión', exact: true }).getByRole('button', { name: /Informe de OPs/ }).click()
  await page.getByRole('button', { name: 'Revisar importe', exact: true }).click()
  await page.getByRole('button', { name: 'Confirmar este valor de Manager', exact: true }).click()
  await page.getByRole('button', { name: 'Guardar reporte', exact: true }).first().click()
  await page.getByText('Reporte guardado. Puedes continuar después.', { exact: true }).waitFor()
  assert.equal(saves, 1)
  const directory = process.env.PORTAL_SCREENSHOT_DIR
  if (directory) { await fs.mkdir(directory, { recursive: true }); await page.screenshot({ path: `${directory}/reports-desktop.png`, fullPage: true }) }
  await page.getByRole('button', { name: 'Revisar descarga', exact: true }).click()
  assert.equal(await page.getByRole('button', { name: 'Descargar VENTAS MES', exact: true }).isEnabled(), true)
  const download = page.waitForEvent('download'); await page.getByRole('button', { name: 'Descargar VENTAS MES', exact: true }).click(); await download
  assert.equal(exports, 1)
  await page.getByRole('button', { name: 'Volver a revisar', exact: true }).click()
  await page.setViewportSize({ width: 390, height: 844 })
  if (await page.locator('.sidebar.mobile-open').count()) await page.locator('.sidebar-mobile-close').click()
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true)
  if (directory) await page.screenshot({ path: `${directory}/reports-mobile.png`, fullPage: true, animations: 'disabled' })
  assert.deepEqual(errors, [])
  console.log('PASS: preparar, confirmar tres detalles, FACTURA manual, reparto, guardar, descargar y vista móvil sin errores.')
} finally { await browser.close() }
