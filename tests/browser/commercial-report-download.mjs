import assert from 'node:assert/strict'
import fs from 'node:fs/promises'

const { chromium } = await import(process.env.PORTAL_PLAYWRIGHT_MODULE ?? 'playwright')
const browser = await chromium.launch({ headless: true, channel: process.env.PORTAL_BROWSER_CHANNEL || undefined })
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
page.setDefaultTimeout(10000)
const errors = []
page.on('pageerror', error => errors.push(error.message))
page.on('dialog', dialog => dialog.accept())
const base = process.env.PORTAL_TEST_FRONTEND_URL ?? 'http://127.0.0.1:5178'
const id = '11111111-1111-1111-1111-111111111111'
const userId = '33333333-3333-3333-3333-333333333333'
const ops = Array.from({ length: 23 }, (_, index) => ({
  id: `op-${index + 1}`, number: String(25001 + index), code: '', client: `Cliente ficticio ${index + 1}`,
  product: `Producto ficticio ${index + 1}`, orderVersion: null, capturedAt: '2026-10-06T12:00:00Z',
  origin: 'Prueba aislada', data: { cells: [], portalCode: null },
}))

function fixture(ready = false) {
  const details = ops.map((op, index) => ({
    id: `detail-${index + 1}`, sourceId: id, sourceFile: 'Manager ficticio.xlsx', sheet: 'Hoja1', sourceRow: index + 2,
    number: String(8001 + index), date: '2026-05-04', client: op.client, term: 'CONTADO', rawAmount: 100,
    detail: `Detalle ficticio ${index + 1}`, line: 'EMPAQUE', seller: 'Vendedor ficticio', managerOp: op.number,
    documentId: `doc-${index + 1}`, matchKey: String(index + 1), selectedOpId: op.id, selectedOpNumber: op.number,
    selectedProduct: op.product, usePortalCode: false, manualGroup: null, reviewed: ready, excluded: false, reason: '',
  }))
  const groups = details.map((detail, index) => ({
    key: `g${index + 1}`, documentId: detail.documentId, number: detail.number, date: detail.date,
    op: detail.selectedOpNumber, product: detail.selectedProduct, client: detail.client, term: detail.term,
    amount: 100, factura: ready ? `MANUAL-${index + 1}` : '', line: detail.line, seller: detail.seller,
    details: [detail.detail], detailIds: [detail.id],
    issues: ready ? [] : ['Completa FACTURA.', 'Confirma la OP y el producto.'], modified: false,
  }))
  const documents = details.map((detail, index) => ({
    id: detail.documentId, number: detail.number, client: detail.client, sourceAmount: 100,
    confirmedAmount: ready ? 100 : index % 2 ? 90 : null, reason: '',
  }))
  return {
    id, name: 'Prueba aislada de descarga', version: 1, updatedAt: '2026-10-06T12:00:00Z', lastExportedVersion: null,
    data: { currentSourceId: id, sourceFile: 'Manager ficticio.xlsx', sha256: 'synthetic-download-test',
      details, documents, edits: [], unappliedEdits: [], warnings: [] },
    groups, controls: documents.map(doc => ({ id: doc.id, number: doc.number, client: doc.client,
      expected: doc.confirmedAmount, distributed: 100, difference: doc.confirmedAmount === null ? null : 100 - doc.confirmedAmount })),
    canExport: ready,
  }
}

let report = fixture()
let failSave = false, failExport = false
let saves = 0, exports = 0
let permissions = ['commercial.reports.view', 'commercial.reports.edit', 'commercial.reports.export']
const requests = []
const routeErrors = []
let savedPayload, exportPayload
await page.route('**/api/**', async route => {
  try {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()
    if (!path.startsWith('/api/')) return route.continue()
    requests.push({ path, method })
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (path === '/api/auth/me') return json({ id: userId, documentNumber: 'TEST', firstName: 'Auxiliar', lastName: 'Prueba',
      email: 'auxiliar@example.test', area: null, roles: ['Auxiliar Comercial'],
      availableRoles: [{ id: userId, name: 'Auxiliar Comercial' }], activeRoleIds: [userId], permissions,
      isActive: true, mustChangePassword: false })
    if (path === '/api/auth/csrf') return json({ token: 'test-csrf' })
    if (path === '/api/commercial/reports/ops') return json(ops)
    if (path === `/api/commercial/reports/${id}` && method === 'PUT') {
      saves++
      savedPayload = route.request().postDataJSON()
      if (route.request().headers()['x-xsrf-token'] !== 'test-csrf') routeErrors.push('Save omitted the CSRF token')
      if (failSave) return json({ code: 'test_unavailable', message: 'Error ficticio al guardar. Los cambios siguen disponibles.' }, 503)
      report = structuredClone(report)
      report.version++
      report.name = savedPayload.name
      report.data.edits = savedPayload.edits
      for (const detail of report.data.details) Object.assign(detail, savedPayload.details.find(item => item.id === detail.id))
      for (const doc of report.data.documents) Object.assign(doc, savedPayload.documents.find(item => item.id === doc.id))
      for (const group of report.groups) {
        const edit = savedPayload.edits.find(item => item.key === group.key)
        if (edit) { group.factura = edit.factura; group.amount = edit.amount; group.modified = true }
        group.issues = []
        if (!group.factura) group.issues.push('Completa FACTURA.')
        if (group.detailIds.some(detailId => !report.data.details.find(detail => detail.id === detailId)?.reviewed))
          group.issues.push('Confirma la OP y el producto.')
      }
      for (const control of report.controls) {
        control.expected = report.data.documents.find(doc => doc.id === control.id).confirmedAmount
        control.distributed = report.groups.filter(group => group.documentId === control.id).reduce((sum, group) => sum + (group.amount ?? 0), 0)
        control.difference = control.expected === null ? null : control.distributed - control.expected
      }
      report.canExport = report.groups.length > 0 && report.groups.every(group => !group.issues.length) &&
        report.controls.every(control => control.expected !== null && control.difference === 0)
      return json(report)
    }
    if (path === `/api/commercial/reports/${id}` && method === 'GET') return json(report)
    if (path === `/api/commercial/reports/${id}/export` && method === 'POST') {
      exports++
      exportPayload = route.request().postDataJSON()
      if (route.request().headers()['x-xsrf-token'] !== 'test-csrf') routeErrors.push('Export omitted the CSRF token')
      if (failExport) return json({ code: 'test_export_rejected', message: 'Error ficticio de validación al descargar.' }, 422)
      return route.fulfill({ contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', body: Buffer.from('SYNTHETIC DOWNLOAD TEST ONLY') })
    }
    if (path === '/api/commercial/reports' && method === 'GET') return json([report])
    routeErrors.push(`Unexpected API call: ${method} ${path}`)
    return json({ message: 'Unexpected synthetic endpoint' }, 404)
  } catch (error) {
    routeErrors.push(error.message)
    await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic route failure' }) })
  }
})

const downloadButton = page.getByRole('button', { name: 'Descargar VENTAS MES', exact: true })
const downloadState = page.getByRole('region', { name: 'Estado de la descarga', exact: true })
const downloadFile = page.getByRole('region', { name: 'Archivo a descargar', exact: true })
const pendingPanel = page.getByRole('region', { name: 'Pendientes para descargar', exact: true })
const pendingTabs = pendingPanel.getByRole('navigation', { name: 'Tipos de pendientes', exact: true })
const pendingPages = pendingPanel.getByRole('navigation', { name: 'Páginas de pendientes', exact: true })
const pendingSearch = pendingPanel.getByRole('textbox', { name: 'Buscar pendiente', exact: true })
const reviewTabs = page.getByRole('navigation', { name: 'Secciones de revisión', exact: true })
const records = page.getByRole('region', { name: 'Informe de ventas', exact: true })
const amounts = page.getByRole('region', { name: 'Informe de OPs', exact: true })
const recordPages = records.getByRole('navigation', { name: 'Páginas del informe de ventas', exact: true })
const amountPages = amounts.getByRole('navigation', { name: 'Páginas del informe de OPs', exact: true })
const groupEditor = page.getByRole('region', { name: 'Revisar registro', exact: true })
const amountEditor = page.getByRole('region', { name: 'Revisar importe', exact: true })
const saveAndCheck = page.getByRole('button', { name: 'Guardar y comprobar', exact: true })

async function stepThree() {
  await page.getByRole('button', { name: 'Revisar descarga', exact: true }).click()
  await page.getByRole('heading', { name: 'Descargar Excel', exact: true }).waitFor()
}
async function range(navigation, text) { await navigation.getByText(text, { exact: true }).waitFor() }
async function screenshot(name) {
  const directory = process.env.PORTAL_SCREENSHOT_DIR
  if (!directory) return
  await fs.mkdir(directory, { recursive: true })
  await page.getByRole('heading', { name: 'Descargar Excel', exact: true }).scrollIntoViewIfNeeded()
  await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true, animations: 'disabled' })
}
async function reloadFixture() {
  await page.reload()
  await page.getByRole('heading', { name: 'Reporte de ventas', exact: true }).waitFor()
  await records.waitFor()
}

try {
  await page.goto(`${base}/commercial/reports/ventas/${id}`)
  await records.waitFor()
  // Keep filters active on both review views; opening a pending row must clear them.
  await records.getByRole('textbox').fill('Cliente ficticio 2')
  await records.locator('.reports-toolbar').getByRole('combobox').selectOption('review')
  await reviewTabs.getByRole('button', { name: /Informe de OPs/ }).click()
  await amounts.getByRole('textbox').fill('Cliente ficticio 3')
  await amounts.locator('.reports-toolbar').getByRole('combobox').selectOption('checked')
  await stepThree()
  await page.getByRole('heading', { name: 'Hay pendientes por resolver', exact: true }).waitFor()
  assert.equal(await downloadButton.isDisabled(), true)
  await range(pendingPages, '1–5 de 23 · 5 por página')
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar registro', exact: true }).count(), 5)
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar importe', exact: true }).count(), 0)
  assert.match(await pendingPanel.innerText(), /Completa FACTURA\./)
  await pendingPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await range(pendingPages, '6–10 de 23 · 5 por página')
  await pendingPages.getByRole('combobox').selectOption('5')
  await range(pendingPages, '21–23 de 23 · 5 por página')
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar registro', exact: true }).count(), 3)
  assert.equal(await pendingPages.getByRole('button', { name: 'Siguiente', exact: true }).isDisabled(), true)
  await pendingSearch.fill('Cliente ficticio 23')
  await range(pendingPages, '1–1 de 1 · 5 por página')
  await pendingSearch.fill('No existe este cliente')
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar registro', exact: true }).count(), 0)
  await pendingSearch.fill('')
  await range(pendingPages, '1–5 de 23 · 5 por página')
  await screenshot('reports-download-sales-desktop')
  await pendingPages.getByRole('combobox').selectOption('5')
  await pendingPanel.getByRole('button', { name: 'Revisar registro', exact: true }).first().click()
  await groupEditor.waitFor()
  assert.match(await groupEditor.innerText(), /NUMERO 8021/)
  assert.match(await groupEditor.innerText(), /Cliente ficticio 21/)
  assert.equal(await records.getByRole('textbox').inputValue(), '')
  assert.equal(await records.locator('.reports-toolbar').getByRole('combobox').inputValue(), 'all')
  await range(recordPages, '21–23 de 23 · 5 por página')
  await groupEditor.getByRole('tab', { name: 'Datos del Excel', exact: true }).click()
  await page.getByLabel('FACTURA · Manual', { exact: true }).fill('BORRADOR-PAGINA-21')
  await stepThree()
  await page.getByRole('heading', { name: 'Guarda los cambios antes de descargar', exact: true }).waitFor()
  assert.equal(await downloadButton.isDisabled(), true)
  assert.equal(exports, 0)
  failSave = true
  await saveAndCheck.click()
  await page.getByRole('alert').filter({ hasText: 'Error ficticio al guardar.' }).waitFor()
  assert.equal(await downloadButton.isDisabled(), true)
  await page.getByRole('heading', { name: 'Guarda los cambios antes de descargar', exact: true }).waitFor()
  await pendingSearch.fill('Cliente ficticio 21')
  await pendingPanel.getByRole('button', { name: 'Revisar registro', exact: true }).click()
  await groupEditor.waitFor()
  await groupEditor.getByRole('tab', { name: 'Datos del Excel', exact: true }).click()
  assert.equal(await page.getByLabel('FACTURA · Manual', { exact: true }).inputValue(), 'BORRADOR-PAGINA-21')
  await stepThree()
  failSave = false
  await saveAndCheck.click()
  await page.getByRole('status').filter({ hasText: 'Reporte guardado. Puedes continuar después.' }).waitFor()
  await page.getByRole('heading', { name: 'Descargar Excel', exact: true }).waitFor()
  await page.getByRole('heading', { name: 'Hay pendientes por resolver', exact: true }).waitFor()
  assert.equal(savedPayload.details.length, 23)
  assert.equal(savedPayload.documents.length, 23)
  assert.equal(savedPayload.edits.find(edit => edit.key === 'g21').factura, 'BORRADOR-PAGINA-21')
  assert.equal(await records.count(), 0)
  await pendingTabs.getByRole('button', { name: /Informe de OPs/ }).click()
  await pendingSearch.fill('')
  await pendingPages.getByRole('combobox').selectOption('1')
  await range(pendingPages, '1–5 de 23 · 5 por página')
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar importe', exact: true }).count(), 5)
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar registro', exact: true }).count(), 0)
  assert.match(await pendingPanel.innerText(), /total.*confirm|confirm.*total/i)
  assert.match(await pendingPanel.innerText(), /diferencia/i)
  await pendingPages.getByRole('combobox').selectOption('5')
  await range(pendingPages, '21–23 de 23 · 5 por página')
  assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar importe', exact: true }).count(), 3)
  await pendingSearch.fill('Cliente ficticio 23')
  await range(pendingPages, '1–1 de 1 · 5 por página')
  await pendingSearch.fill('')
  await range(pendingPages, '1–5 de 23 · 5 por página')
  await screenshot('reports-download-ops-desktop')
  await pendingPages.getByRole('combobox').selectOption('5')
  await pendingPanel.getByRole('button', { name: 'Revisar importe', exact: true }).first().click()
  await amountEditor.waitFor()
  assert.match(await amountEditor.innerText(), /NUMERO 8021/)
  assert.match(await amountEditor.innerText(), /Cliente ficticio 21/)
  assert.equal(await amounts.getByRole('textbox').inputValue(), '')
  assert.equal(await amounts.locator('.reports-toolbar').getByRole('combobox').inputValue(), 'all')
  await range(amountPages, '21–23 de 23 · 5 por página')
  await page.getByLabel('Total confirmado', { exact: true }).fill('100')
  await stepThree()
  await saveAndCheck.click()
  await page.getByRole('status').filter({ hasText: 'Reporte guardado. Puedes continuar después.' }).waitFor()
  await pendingTabs.getByRole('button', { name: /Informe de OPs/ }).click()
  await pendingSearch.fill('')
  await pendingPages.getByRole('combobox').selectOption('1')
  await range(pendingPages, '1–5 de 22 · 5 por página')
  assert.equal(savedPayload.documents.find(doc => doc.id === 'doc-21').confirmedAmount, 100)
  assert.equal(savedPayload.details.length, 23)
  assert.equal(savedPayload.documents.length, 23)
  for (const width of [320, 390]) {
    await page.setViewportSize({ width, height: 844 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), true)
    assert.equal(await pendingPanel.getByRole('button', { name: 'Revisar importe', exact: true }).count(), 5)
    await pendingPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
    await range(pendingPages, '6–10 de 22 · 5 por página')
    await pendingPages.getByRole('combobox').selectOption('1')
    await range(pendingPages, '1–5 de 22 · 5 por página')
  }
  await screenshot('reports-download-mobile')
  await page.setViewportSize({ width: 1440, height: 1000 })
  report = fixture(true)
  await reloadFixture()
  await stepThree()
  await page.getByRole('heading', { name: 'Listo para descargar', exact: true }).waitFor()
  assert.equal(await pendingPanel.count(), 0)
  assert.equal(await downloadButton.isEnabled(), true)
  await page.getByRole('button', { name: 'Volver a revisar', exact: true }).click()
  await records.getByRole('button', { name: 'Revisar / editar', exact: true }).first().click()
  await page.getByLabel('FACTURA · Manual', { exact: true }).fill('MANUAL-ACTUALIZADA')
  await stepThree()
  await page.getByRole('heading', { name: 'Guarda los cambios antes de descargar', exact: true }).waitFor()
  assert.equal(await downloadButton.isDisabled(), true)
  await saveAndCheck.click()
  await page.getByRole('heading', { name: 'Listo para descargar', exact: true }).waitFor()
  assert.equal(savedPayload.edits.find(edit => edit.key === 'g1').factura, 'MANUAL-ACTUALIZADA')
  assert.equal(savedPayload.details.length, 23)
  assert.equal(savedPayload.documents.length, 23)
  failExport = true
  await downloadButton.click()
  await page.getByRole('alert').filter({ hasText: 'Error ficticio de validación al descargar.' }).waitFor()
  assert.equal(await page.getByText(/Esta versión ya se descargó|La última descarga corresponde/).count(), 0)
  assert.equal(await page.getByText(/Descargada la versión/).count(), 0)
  failExport = false
  const downloadPromise = page.waitForEvent('download')
  await downloadButton.click()
  const download = await downloadPromise
  assert.equal(download.suggestedFilename(), 'VENTAS MES.xlsx')
  await page.getByRole('status').filter({ hasText: `Descargada la versión ${report.version}.` }).waitFor()
  assert.equal(exports, 2)
  assert.equal(exportPayload.version, report.version)
  await screenshot('reports-download-ready-desktop')
  // A rejected retry must clear the prior success message while preserving the
  // real historical download version, rather than inventing another success.
  failExport = true
  await downloadButton.click()
  await page.getByRole('alert').filter({ hasText: 'Error ficticio de validación al descargar.' }).waitFor()
  await page.getByRole('heading', { name: 'No se pudo descargar el Excel', exact: true }).waitFor()
  assert.equal(await page.getByText(/Descargada la versión/).count(), 0)
  await page.getByText('Esta versión ya se descargó. Puedes descargarla de nuevo.', { exact: true }).waitFor()
  assert.equal(exports, 3)
  failExport = false
  report = fixture(true)
  report.canExport = false
  await reloadFixture()
  await stepThree()
  assert.equal(await downloadButton.isDisabled(), true)
  assert.equal(await page.getByRole('heading', { name: 'Listo para descargar', exact: true }).count(), 0)
  assert.equal(await pendingPanel.count(), 0)
  await downloadState.getByRole('heading', { name: 'Falta completar la revisión', exact: true }).waitFor()
  report = fixture(true)
  report.groups = []
  report.controls = []
  for (const detail of report.data.details) detail.excluded = true
  report.canExport = false
  await reloadFixture()
  await stepThree()
  await page.getByRole('heading', { name: 'No hay registros para descargar', exact: true }).waitFor()
  assert.equal(await downloadButton.isDisabled(), true)
  assert.equal(await page.getByRole('heading', { name: 'Listo para descargar', exact: true }).count(), 0)
  assert.equal(await pendingPanel.count(), 0)
  assert.match(await downloadState.innerText(), /no.*descargar|no.*listo|bloquead|validaci|sin registros/i)
  report = fixture(true)
  permissions = ['commercial.reports.view', 'commercial.reports.export']
  await reloadFixture()
  await stepThree()
  assert.equal(await downloadButton.isEnabled(), true)
  assert.equal(await saveAndCheck.count(), 0)
  permissions = ['commercial.reports.view']
  await reloadFixture()
  await stepThree()
  assert.equal(await downloadButton.count(), 0)
  assert.match(await downloadFile.innerText(), /permiso|autoriz/i)
  assert.deepEqual(errors, [])
  assert.deepEqual(routeErrors, [])
  assert.ok(saves >= 4)
  assert.ok(requests.every(request => request.path.startsWith('/api/')))
  console.log('PASS: descarga con 23 pendientes por tipo, cinco filas, búsqueda y paginación, editores de página posterior, borradores y error de guardado, validación guardada, permisos, bloqueo sin filas, error de exportación y descarga con CSRF, escritorio y móvil.')
} catch (error) {
  console.error('Download browser diagnostic:', page.url(), await page.locator('body').innerText(), errors, routeErrors)
  throw error
} finally { await browser.close() }
