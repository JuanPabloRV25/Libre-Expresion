import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import fs from 'node:fs/promises'

const { chromium } = await import(process.env.PORTAL_PLAYWRIGHT_MODULE ?? 'playwright')
const browser = await chromium.launch({ headless: true, channel: process.env.PORTAL_BROWSER_CHANNEL || undefined })
const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } })
page.setDefaultTimeout(10000)
page.on('dialog', dialog => dialog.accept())
const errors = [], routeErrors = []
page.on('pageerror', error => errors.push(error.message))
const base = process.env.PORTAL_TEST_FRONTEND_URL ?? 'http://127.0.0.1:5178'
const id = '11111111-1111-1111-1111-111111111111'
const userId = '33333333-3333-3333-3333-333333333333'
const permissionsAll = ['commercial.reports.view', 'commercial.reports.edit', 'commercial.reports.export']
const op = (n, product = `Producto ficticio ${n}`) => ({ id: `op-${n}`, number: String(25000 + n), code: '',
  client: 'Cliente ficticio', product, orderVersion: null, capturedAt: '2026-10-06T12:00:00Z',
  origin: 'Registro histórico ficticio', data: { cells: [], portalCode: null } })
const ops = [op(1), op(2), { ...op(3), code: 'OP-2026-0003' }, op(4, '')]
const normalize = value => String(value ?? '').normalize('NFD').replace(/\p{M}/gu, '').replace(/[^\p{L}\p{N}]/gu, '').toUpperCase()
const hash = (...parts) => createHash('sha256').update(parts.join('\u001f')).digest('hex')
// Fixtures emulate only the documented server response. The production engine
// remains the authority; this test verifies editor state and real request bodies.
const groupKey = detail => hash(detail.documentId, detail.manualGroup?.trim()
  ? `manual:${detail.manualGroup.trim()}` : detail.selectedOpId
    ? `${detail.selectedOpId}:${normalize(detail.selectedProduct)}:${normalize(detail.line)}` : `pending:${detail.id}`)
const distinct = values => [...new Set(values)]
function refresh(report) {
  const buckets = new Map()
  for (const detail of report.data.details.filter(item => !item.excluded)) {
    const key = groupKey(detail)
    if (!buckets.has(key)) buckets.set(key, [])
    buckets.get(key).push(detail)
  }
  report.groups = [...buckets].map(([key, details]) => {
    const first = details[0], edit = report.data.edits.find(item => item.key === key)
    const doc = report.data.documents.find(item => item.id === first.documentId)
    const all = report.data.details.filter(item => item.documentId === first.documentId)
    const proposedAmount = details.length === all.length && details.every(item => item.selectedOpId && item.reviewed) ? doc.sourceAmount : null
    const amount = edit ? edit.amount : proposedAmount
    const issues = []
    if (details.some(item => !item.selectedOpId || !item.reviewed)) issues.push('Confirma la OP y el producto de cada detalle.')
    if (details.some(item => !item.selectedProduct.trim())) issues.push('Completa el producto que falta en el registro histórico.')
    if (distinct(details.map(item => normalize(item.line))).length > 1) issues.push('El grupo tiene líneas distintas. Revisa la agrupación.')
    if (distinct(details.map(item => normalize(item.selectedProduct)).filter(Boolean)).length > 1) issues.push('Los productos distintos deben quedar en registros separados.')
    if (!edit?.factura.trim()) issues.push('Completa FACTURA.')
    if (amount === null) issues.push('Completa el importe de este registro.')
    return { key, documentId: first.documentId, number: first.number, date: first.date,
      op: distinct(details.map(item => item.selectedOpNumber).filter(Boolean)).join('/'),
      product: distinct(details.map(item => item.selectedProduct).filter(Boolean)).join(' / '),
      client: edit?.client ?? first.client, term: first.term, amount, factura: edit?.factura ?? '',
      line: edit?.line ?? first.line, seller: edit?.seller ?? first.seller,
      details: details.map(item => item.detail), detailIds: details.map(item => item.id), issues, modified: !!edit }
  })
  report.controls = report.data.documents.filter(doc => report.data.details.some(item => item.documentId === doc.id && !item.excluded)).map(doc => {
    const distributed = report.groups.filter(group => group.documentId === doc.id).reduce((sum, group) => sum + (group.amount ?? 0), 0)
    return { id: doc.id, number: doc.number, client: doc.client, expected: doc.confirmedAmount,
      distributed, difference: doc.confirmedAmount === null ? null : doc.confirmedAmount - distributed }
  })
  report.canExport = report.groups.length > 0 && report.groups.every(group => !group.issues.length) &&
    report.controls.every(control => control.expected !== null && control.difference === 0) &&
    report.data.details.filter(item => item.excluded).every(item => item.reason.trim())
  return report
}
function fixture({ count = 1, sameDocument = true, selected = true, reviewed = true, factura = '', split = false } = {}) {
  const details = Array.from({ length: count }, (_, index) => {
    const chosen = selected ? ops[split && index ? 1 : 0] : null
    return { id: `detail-${index + 1}`, sourceId: id, sourceFile: 'Manager ficticio.xlsx', sheet: 'Hoja1', sourceRow: index + 2,
      number: String(8001 + (sameDocument ? 0 : index)), date: '2026-05-04', client: 'Cliente ficticio', term: 'CONTADO', rawAmount: 100,
      detail: `Descripción íntegra ficticia ${index + 1}`, line: 'EMPAQUE', seller: `Vendedor ficticio ${index + 1}`,
      managerOp: chosen?.number ?? '99999', documentId: `doc-${sameDocument ? 1 : index + 1}`, matchKey: `match-${index + 1}`,
      selectedOpId: chosen?.id ?? null, selectedOpNumber: chosen?.number ?? '', selectedProduct: chosen?.product ?? '',
      usePortalCode: false, reviewed: selected && reviewed, excluded: false, reason: '', manualGroup: null }
  })
  const report = { id, name: 'Editor de prueba aislado', version: 1, updatedAt: '2026-10-06T12:00:00Z', lastExportedVersion: null,
    data: { currentSourceId: id, sourceFile: 'Manager ficticio.xlsx', sha256: 'synthetic-record-editor', details,
      documents: distinct(details.map(item => item.documentId)).map(docId => {
        const first = details.find(item => item.documentId === docId)
        return { id: docId, number: first.number, client: first.client, sourceAmount: 100, confirmedAmount: 100, reason: '' }
      }), edits: [], unappliedEdits: [], warnings: [] }, groups: [], controls: [], canExport: false }
  refresh(report)
  report.data.edits = report.groups.map(group => ({ key: group.key, factura, amount: split ? 50 : 100,
    client: null, line: null, seller: null, reason: '' }))
  return refresh(report)
}
let report = fixture(), permissions = [...permissionsAll], failSave = false, savedPayload
let saves = 0
await page.route('**/api/**', async route => {
  try {
    const path = new URL(route.request().url()).pathname, method = route.request().method()
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (!path.startsWith('/api/')) return route.continue()
    if (path === '/api/auth/me') return json({ id: userId, documentNumber: 'TEST', firstName: 'Auxiliar', lastName: 'Prueba',
      email: 'auxiliar@example.test', area: null, roles: ['Auxiliar Comercial'], availableRoles: [{ id: userId, name: 'Auxiliar Comercial' }],
      activeRoleIds: [userId], permissions, isActive: true, mustChangePassword: false })
    if (path === '/api/auth/csrf') return json({ token: 'test-csrf' })
    if (path === '/api/commercial/reports/ops') return json(ops)
    if (path === `/api/commercial/reports/${id}` && method === 'GET') return json(report)
    if (path === `/api/commercial/reports/${id}` && method === 'PUT') {
      saves++
      savedPayload = route.request().postDataJSON()
      assert.equal(route.request().headers()['x-xsrf-token'], 'test-csrf')
      assert.equal(savedPayload.version, report.version)
      assert.equal(savedPayload.details.length, report.data.details.length)
      assert.equal(savedPayload.documents.length, report.data.documents.length)
      if (failSave) return json({ code: 'synthetic_unavailable', message: 'Error ficticio de guardado. El borrador se conserva.' }, 503)
      const result = structuredClone(report)
      for (const detail of result.data.details) {
        const decision = savedPayload.details.find(item => item.id === detail.id)
        const selected = ops.find(item => item.id === decision.selectedOpId)
        Object.assign(detail, decision, { selectedOpId: selected?.id ?? null,
          selectedOpNumber: selected ? decision.usePortalCode ? selected.code : selected.number : '',
          selectedProduct: selected ? selected.product || decision.productIfMissing || '' : '',
          reviewed: !!selected && decision.reviewed })
      }
      for (const doc of result.data.documents) Object.assign(doc, savedPayload.documents.find(item => item.id === doc.id))
      const keys = new Set(result.data.details.filter(item => !item.excluded).map(groupKey))
      result.data.unappliedEdits.push(...savedPayload.edits.filter(edit => !keys.has(edit.key)))
      result.data.edits = savedPayload.edits.filter(edit => keys.has(edit.key))
      result.version++
      result.name = savedPayload.name
      report = refresh(result)
      return json(report)
    }
    routeErrors.push(`Unexpected synthetic request: ${method} ${path}`)
    return json({ message: 'Unexpected synthetic endpoint' }, 404)
  } catch (error) {
    routeErrors.push(error.message)
    return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: 'Synthetic fixture failure' }) })
  }
})
const records = page.getByRole('region', { name: 'Informe de ventas', exact: true })
const editor = page.getByRole('region', { name: 'Revisar registro', exact: true })
const excel = editor.getByRole('tab', { name: 'Datos del Excel', exact: true })
const correspondence = editor.getByRole('tab', { name: 'OP y detalles', exact: true })
const preview = page.getByRole('region', { name: 'Vista previa de VENTAS MES', exact: true })
const recordPages = records.getByRole('navigation', { name: 'Páginas del informe de ventas', exact: true })
async function loadFixture(next) {
  report = next
  failSave = false
  savedPayload = undefined
  await page.goto(`${base}/commercial/reports/ventas/${id}`)
  await page.reload()
  await records.waitFor()
}
async function openRecord(index = 0) { await records.getByRole('button', { name: permissions.includes('commercial.reports.edit') ? 'Revisar / editar' : 'Ver', exact: true }).nth(index).click(); await editor.waitFor() }
async function chooseOp(id) {
  await correspondence.click()
  const select = editor.getByLabel('OP del detalle', { exact: true })
  if (!await select.isVisible()) await editor.getByRole('button', { name: 'Cambiar OP', exact: true }).click()
  await select.selectOption(id)
}
async function confirm() { await editor.getByRole('button', { name: 'Confirmar OP y producto', exact: true }).click() }
async function saveEditor() {
  const previous = saves
  await editor.getByRole('button', { name: /^Guardar reporte(?: y continuar)?$/ }).click()
  await page.getByRole('status').filter({ hasText: 'Reporte guardado.' }).waitFor()
  assert.equal(saves, previous + 1)
}
async function screenshot(name) {
  const directory = process.env.PORTAL_SCREENSHOT_DIR
  if (!directory) return
  await fs.mkdir(directory, { recursive: true })
  await editor.scrollIntoViewIfNeeded()
  await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true, animations: 'disabled' })
}
async function screenshotPreview(name) {
  const directory = process.env.PORTAL_SCREENSHOT_DIR
  if (!directory) return
  await fs.mkdir(directory, { recursive: true })
  const viewport = page.viewportSize()
  // Give the region enough vertical room for the capture so a fixed app header
  // does not overlay its fields. The responsive width stays exactly the same.
  if (viewport.width <= 390) await page.setViewportSize({ ...viewport, height: 2200 })
  await preview.screenshot({ path: `${directory}/${name}.png`, animations: 'disabled' })
  if (viewport.width <= 390) await page.setViewportSize(viewport)
}

try {
  // A confirmed OP must lead directly to the missing manual field. Preview
  // shows all ten workbook columns and draft overrides before any API save.
  await loadFixture(fixture({ count: 7, sameDocument: false }))
  await records.locator('.reports-toolbar').getByRole('combobox').selectOption('review')
  await openRecord()
  assert.equal(await excel.getAttribute('aria-selected'), 'true')
  await editor.getByRole('button', { name: 'Ir a lo pendiente', exact: true }).click()
  assert.equal(await editor.getByLabel('FACTURA · Manual', { exact: true }).evaluate(element => document.activeElement === element), true)
  await preview.waitFor()
  for (const name of ['NUMERO OP', 'FACTURA', 'NUMERO', 'FECHA', 'NOMBRE', 'PLAZO', 'VALOR_BRUT', 'DETALLE', 'LINEA', 'VENDEDOR'])
    assert.equal(await preview.getByRole('columnheader', { name: new RegExp(`${name}$`) }).count(), 1)
  const previewPages = preview.getByRole('navigation', { name: 'Páginas de la vista previa', exact: true })
  await previewPages.getByText('1–5 de 7 · 5 por página', { exact: true }).waitFor()
  assert.equal(await preview.locator('tbody tr').count(), 5)
  assert.match(await preview.locator('tbody tr').first().innerText(), /Descripción íntegra ficticia 1/)
  await editor.getByLabel('FACTURA · Manual', { exact: true }).fill('BORRADOR-MANUAL')
  await editor.getByLabel('Importe del registro', { exact: true }).fill('0')
  assert.match(await preview.locator('tbody tr').first().innerText(), /BORRADOR-MANUAL/)
  assert.match(await preview.locator('tbody tr').first().innerText(), /\$\s*0/)
  await editor.getByLabel('Importe del registro', { exact: true }).fill('')
  assert.match(await preview.locator('tbody tr').first().innerText(), /Sin completar/)
  await editor.getByLabel('Importe del registro', { exact: true }).fill('100')
  await editor.getByText('Corregir estos datos', { exact: true }).click()
  await editor.getByLabel('Cliente', { exact: true }).fill('')
  assert.equal((await preview.locator('tbody tr').first().locator('td').nth(4).innerText()).trim(), 'Sin completar')
  await editor.getByLabel('Cliente', { exact: true }).fill('Cliente corregido ficticio')
  assert.equal((await preview.locator('tbody tr').first().locator('td').nth(4).innerText()).trim(), 'Cliente corregido ficticio')
  await excel.focus()
  await excel.press('ArrowLeft')
  assert.equal(await correspondence.getAttribute('aria-selected'), 'true')
  await correspondence.press('End')
  assert.equal(await excel.getAttribute('aria-selected'), 'true')
  assert.equal(await excel.evaluate(element => document.activeElement === element), true)
  await screenshot('report-record-excel-desktop')
  await screenshotPreview('report-record-preview-desktop')
  for (const width of [320, 390]) {
    await page.setViewportSize({ width, height: 844 })
    const card = preview.locator('details[aria-label="Fila 4 del Excel"]')
    assert.equal(await card.isVisible(), true)
    assert.notEqual(await card.getAttribute('open'), null)
    assert.equal(await card.locator('dt').count(), 10)
    assert.match(await card.innerText(), /BORRADOR-MANUAL/)
    assert.match(await card.innerText(), /Cliente corregido ficticio/)
    const other = preview.locator('details[aria-label="Fila 5 del Excel"]')
    await other.locator('summary').click()
    assert.equal(await other.locator('.report-excel-card-content').isVisible(), true)
    assert.equal(await other.locator('dt').count(), 10)
    assert.match(await other.innerText(), /Descripción íntegra ficticia 2/)
    await other.locator('summary').click()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true)
    await screenshot(`report-record-excel-${width}`)
    if (width === 320) await screenshotPreview('report-record-preview-320')
  }
  await page.setViewportSize({ width: 1440, height: 1100 })
  await previewPages.getByRole('button', { name: 'Siguiente', exact: true }).click()
  await previewPages.getByText('6–7 de 7 · 5 por página', { exact: true }).waitFor()
  assert.equal(await preview.locator('tbody tr').count(), 2)
  await saveEditor()
  await editor.waitFor()
  assert.equal(await records.locator('.reports-toolbar').getByRole('combobox').inputValue(), 'all')
  assert.equal(savedPayload.edits.find(edit => edit.key === report.groups[0].key).factura, 'BORRADOR-MANUAL')
  assert.equal(savedPayload.details.length, 7)
  assert.equal(savedPayload.documents.length, 7)
  await editor.getByLabel('FACTURA · Manual', { exact: true }).fill('')
  assert.equal((await preview.locator('tbody tr').first().locator('td').nth(1).innerText()).trim(), 'Sin completar')
  await preview.getByRole('navigation', { name: 'Páginas de la vista previa', exact: true }).getByRole('button', { name: 'Siguiente', exact: true }).click()
  await preview.getByRole('button', { name: 'Editar datos de la fila 10, NUMERO 8007', exact: true }).click()
  assert.match(await editor.innerText(), /NUMERO 8007/)
  assert.equal(await excel.getAttribute('aria-selected'), 'true')
  assert.equal(await editor.getByLabel('FACTURA · Manual', { exact: true }).evaluate(element => document.activeElement === element), true)
  await screenshot('report-record-preview-other-row')

  // A pending OP needs selection followed by explicit confirmation. Save
  // changes the old pending key, then follows detail identity into its result.
  await loadFixture(fixture({ selected: false, reviewed: false }))
  await openRecord()
  assert.equal(await correspondence.getAttribute('aria-selected'), 'true')
  await chooseOp('op-1')
  assert.equal(await editor.getByRole('button', { name: 'Confirmar OP y producto', exact: true }).isEnabled(), true)
  await confirm()
  assert.equal(savedPayload, undefined)
  await saveEditor()
  assert.equal(savedPayload.details[0].selectedOpId, 'op-1')
  assert.equal(savedPayload.details[0].reviewed, true)
  await editor.waitFor()
  assert.equal(await excel.getAttribute('aria-selected'), 'true')
  assert.equal(await editor.getByLabel('FACTURA · Manual', { exact: true }).isEnabled(), true)

  // Historical products are completed in this report, without modifying the
  // source OP. The explicit portal-code choice also survives its save payload.
  await loadFixture(fixture({ selected: false, reviewed: false }))
  await openRecord()
  await chooseOp('op-4')
  assert.equal(await editor.getByRole('button', { name: 'Confirmar OP y producto', exact: true }).isDisabled(), true)
  await editor.getByLabel('Producto que falta en el histórico', { exact: true }).fill('Producto histórico verificado')
  await confirm()
  await saveEditor()
  assert.equal(savedPayload.details[0].productIfMissing, 'Producto histórico verificado')
  assert.equal(report.groups[0].product, 'Producto histórico verificado')
  assert.equal(ops[3].product, '')
  await loadFixture(fixture({ selected: false, reviewed: false }))
  await openRecord()
  await chooseOp('op-3')
  await confirm()
  await editor.getByText('Otras correcciones de este detalle', { exact: true }).click()
  await editor.getByRole('checkbox', { name: 'Usar el código interno del Portal en lugar del número de pedido', exact: true }).check()
  await saveEditor()
  assert.equal(savedPayload.details[0].usePortalCode, true)
  assert.equal(report.groups[0].op, 'OP-2026-0003')

  // Detail pagination keeps all seven decisions in the full-report payload.
  await loadFixture(fixture({ count: 7, reviewed: false }))
  await openRecord()
  const detailPages = editor.getByRole('navigation', { name: 'Páginas de detalles', exact: true })
  await detailPages.getByText('1–5 de 7 · 5 por página', { exact: true }).waitFor()
  assert.equal(await editor.getByRole('button', { name: /^Ver detalle \d+$/ }).count(), 5)
  for (let n = 1; n <= 5; n++) {
    await editor.getByRole('button', { name: `Ver detalle ${n}`, exact: true }).click()
    await confirm()
  }
  // Confirming detail five automatically opens the next pending detail on page two.
  await detailPages.getByText('6–7 de 7 · 5 por página', { exact: true }).waitFor()
  for (let n = 6; n <= 7; n++) {
    await editor.getByRole('button', { name: `Ver detalle ${n}`, exact: true }).click()
    await confirm()
  }
  await saveEditor()
  assert.equal(savedPayload.details.length, 7)
  assert.equal(savedPayload.details.every(detail => detail.reviewed), true)
  assert.equal(report.groups[0].details.length, 7)

  // Structural decisions stay pending when another record is opened. The save
  // includes both the OP correction and drafts made in another editor.
  await loadFixture(fixture({ count: 2, sameDocument: false, factura: 'ORIGINAL' }))
  await openRecord()
  await chooseOp('op-2')
  await confirm()
  await openRecord(1)
  await excel.click()
  await editor.getByRole('button', { name: 'Guardar reporte y continuar', exact: true }).waitFor()
  assert.equal(await editor.getByLabel('FACTURA · Manual', { exact: true }).count(), 0)
  await saveEditor()
  assert.equal(savedPayload.details.find(detail => detail.id === 'detail-1').selectedOpId, 'op-2')
  assert.equal(report.groups.find(group => group.detailIds.includes('detail-1')).op, '25002')
  assert.match(await editor.innerText(), /8002/)

  // A split returns two results, rather than losing the editor or silently
  // copying invoice and amounts to a new group key.
  await loadFixture(fixture({ count: 2, factura: 'ANTES-SEPARAR' }))
  await openRecord()
  await correspondence.click()
  await editor.getByRole('button', { name: 'Ver detalle 2', exact: true }).click()
  await chooseOp('op-2')
  await confirm()
  await saveEditor()
  const results = page.getByRole('region', { name: 'Registros resultantes', exact: true })
  await results.waitFor()
  assert.equal(report.groups.length, 2)
  assert.equal(report.groups.find(group => group.op === '25002').factura, '')
  assert.match(await results.innerText(), /25001/)
  assert.match(await results.innerText(), /25002/)
  await results.getByRole('button', { name: 'Revisar registro', exact: true }).first().click()
  await editor.waitFor()
  await excel.click()
  await editor.getByLabel('FACTURA · Manual', { exact: true }).fill('PRIMER-RESULTADO-REVISADO')
  await saveEditor()
  await results.waitFor()
  assert.equal(await results.getByRole('button', { name: 'Revisar registro', exact: true }).count(), 2)
  await results.getByRole('button', { name: 'Revisar registro', exact: true }).last().click()
  await editor.waitFor()
  assert.equal(await excel.getAttribute('aria-selected'), 'true')

  // Merge keeps the destination edit with its valid key and preserves the
  // detached source edit for explicit reconfirmation, as the real service does.
  await loadFixture(fixture({ count: 2, split: true, factura: 'DESTINO' }))
  const detachedKey = report.groups[1].key
  report.data.edits.find(edit => edit.key === detachedKey).factura = 'ORIGEN-CONSERVADO'
  refresh(report)
  await page.reload()
  await openRecord(1)
  await chooseOp('op-1')
  await confirm()
  await saveEditor()
  assert.equal(report.groups.length, 1)
  assert.equal(report.groups[0].factura, 'DESTINO')
  assert.equal(report.data.unappliedEdits.find(edit => edit.key === detachedKey).factura, 'ORIGEN-CONSERVADO')
  await editor.waitFor()
  assert.match(await editor.innerText(), /Descripción íntegra ficticia 1/)
  assert.match(await editor.innerText(), /Descripción íntegra ficticia 2/)

  // Excluding every detail leaves a clear outcome instead of an empty editor.
  await loadFixture(fixture())
  await openRecord()
  await correspondence.click()
  await editor.getByText('Otras correcciones de este detalle', { exact: true }).click()
  await editor.getByRole('checkbox', { name: 'Excluir del reporte', exact: true }).check()
  await editor.getByLabel('Motivo', { exact: true }).fill('Duplicado ficticio comprobado')
  await saveEditor()
  const excluded = page.getByRole('region', { name: 'Resultado de la revisión', exact: true })
  await excluded.waitFor()
  assert.equal(report.groups.length, 0)
  assert.equal(savedPayload.details[0].excluded, true)
  assert.equal(savedPayload.details[0].reason, 'Duplicado ficticio comprobado')
  assert.equal(await editor.count(), 0)

  // Failed saves retain the editor, draft, and task; read-only users get no
  // editable fields and the same ten-column preview.
  await loadFixture(fixture())
  await openRecord()
  await editor.getByLabel('FACTURA · Manual', { exact: true }).fill('NO-PERDER')
  failSave = true
  await editor.getByRole('button', { name: 'Guardar reporte', exact: true }).click()
  await page.getByRole('alert').filter({ hasText: 'Error ficticio de guardado.' }).waitFor()
  assert.equal(await editor.getByLabel('FACTURA · Manual', { exact: true }).inputValue(), 'NO-PERDER')
  assert.equal(await excel.getAttribute('aria-selected'), 'true')
  failSave = false
  await saveEditor()
  permissions = ['commercial.reports.view']
  await loadFixture(fixture({ count: 6, sameDocument: false, factura: 'CONSULTA' }))
  await openRecord()
  await excel.click()
  assert.equal(await editor.getByRole('textbox').count(), 0)
  assert.equal(await editor.getByLabel('FACTURA · Manual', { exact: true }).count(), 0)
  assert.equal(await editor.getByRole('button', { name: /^Guardar reporte/ }).count(), 0)
  await preview.waitFor()
  for (const width of [320, 390]) {
    await page.setViewportSize({ width, height: 844 })
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true)
    await preview.getByRole('navigation', { name: 'Páginas de la vista previa', exact: true }).getByRole('button', { name: 'Siguiente', exact: true }).click()
    await preview.locator('details[aria-label="Fila 9 del Excel"] summary').click()
    assert.match(await preview.innerText(), /Descripción íntegra ficticia 6/)
    await preview.getByRole('navigation', { name: 'Páginas de la vista previa', exact: true }).getByRole('button', { name: 'Anterior', exact: true }).click()
  }
  await screenshot('report-record-excel-readonly-mobile')
  assert.deepEqual(errors, [])
  assert.deepEqual(routeErrors, [])
  console.log('PASS: tareas del editor, preview de diez columnas y todos los registros, borradores cero/vacío, confirmación explícita, paginación de siete detalles, guardado completo, continuidad por identidad al cambiar/separar/unir/excluir, error conservado, permisos y móvil.')
} catch (error) {
  console.error('Record editor diagnostic:', page.url(), await page.locator('body').innerText(), errors, routeErrors)
  throw error
} finally { await browser.close() }
