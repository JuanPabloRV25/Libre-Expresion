import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
const { chromium } = await import(process.env.PORTAL_PLAYWRIGHT_MODULE ?? 'playwright')
const browser = await chromium.launch({ headless: true, channel: process.env.PORTAL_BROWSER_CHANNEL || undefined })
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } })
page.setDefaultTimeout(10000)
const base = process.env.PORTAL_TEST_FRONTEND_URL ?? 'http://127.0.0.1:5181'
const artifactDir = process.env.PORTAL_REPORT_ARTIFACT_DIR ?? 'artifacts/plan004-exceptions'
const id = '11111111-1111-1111-1111-111111111111', userId = '33333333-3333-3333-3333-333333333333'
let permissions = ['commercial.reports.view', 'commercial.reports.edit', 'commercial.reports.export']
let creates = 0, previews = 0, saves = 0, approvals = 0, drafts = 0, finals = 0, failSave = false, lastPayload
let omitSourceRowsOnSave = false, sourceEvidenceOmittedResponses = 0
const errors = [], routeErrors = [], actions = []
page.on('pageerror', error => errors.push(error.message))
page.on('dialog', dialog => dialog.accept())
const record = (number, variant) => ({ id: `00000000-0000-0000-0000-${number.padStart(12, '0')}`, number, code: '',
  client: 'Cliente ficticio', product: variant, orderVersion: null, capturedAt: '2026-10-07T12:00:00Z', origin: 'Histórico sintético',
  registryVersion: 1, previousRecordId: null, data: { cells: Array.from({ length: 21 }, (_, index) => index === 0 ? number : index === 5 ? 'Cliente ficticio' : index === 6 || index === 9 ? variant : ''), portalCode: null } })
const history = [record('25273', 'Etiqueta 50 ml'), record('25279', 'Etiqueta 100 ml'), record('25280', 'Etiqueta 250 ml')]
const source = Array.from({ length: 9 }, (_, n) => ({ id: `detail-${n}`, sourceId: id, sourceFile: 'Ventas sintéticas.xlsx', sheet: 'Hoja1', sourceRow: n + 2,
  number: n < 3 ? '8877' : String(8886 + n), date: '2026-10-01', client: `Cliente ficticio ${n < 3 ? 0 : n}`, term: 'CONTADO', rawAmount: n === 8 ? 0 : 100,
  detail: n < 3 ? `${history[n].product} / Detalle de origen ${n}` : `Producto ${n} / Especificación conservada ${n}`,
  line: 'EMPAQUE', seller: 'Vendedor ficticio', managerOp: n < 3 ? '25280' : n === 5 || n === 6 ? '' : n < 5 ? '25255' : `2528${n}`,
  documentId: `doc-${n}`, matchKey: `source-${n}`, selectedOpId: null, selectedOpNumber: '', selectedProduct: '', usePortalCode: false,
  manualGroup: null, reviewed: false, excluded: false, reason: '' }))
const rows = Array.from({ length: 7 }, (_, n) => { const details = n === 0 ? source.slice(0, 3) : [source[n + 2]], first = details[0]
  return { key: `row-${n}`, documentId: '', number: first.number, date: first.date, op: [0, 3, 4, 5].includes(n) ? 'N/A' : first.managerOp,
    product: '', client: first.client, term: first.term, amount: first.rawAmount, factura: 'F01', line: first.line, seller: first.seller,
    details: details.map(detail => detail.detail), detailIds: details.map(detail => detail.id), issues: [], modified: false } })
rows[1].seller = ''
rows[2].date = ''
const finding = (n, classification) => ({ id: `finding-${n}`, code: n === 0 ? 'op_relationship_undetermined' : n === 5 ? 'op_evidence_incompatible' : 'op_not_informed',
  field: 'NUMERO OP', rule: 'confirmed_explicit_op_decision', initialClassification: classification, resolution: 'pending', provenance: 'automatic',
  proposal: 'N/A', reason: n === 5 ? 'La evidencia histórica es incompatible. La auxiliar debe decidir la OP.' : n === 0 ? 'Las candidatas compatibles requieren confirmar su pertenencia a esta venta.' : 'OP no informada. Confirma una OP o mantener N/A.',
  detailIds: rows[n].detailIds, evidence: rows[n].detailIds.map((detailId, i) => ({ detailId, original: source.find(d => d.id === detailId).detail,
    segment: n === 0 ? history[i].product : 'Producto de prueba', normalizedProduct: n === 0 ? history[i].product.toUpperCase() : 'PRODUCTO DE PRUEBA',
    candidates: n === 0 ? [{ historyId: history[i].id, number: history[i].number, matchedFields: ['REFERENCIA', 'PRODUCTO'] }] : [] })),
  allowedActions: ['select_candidates', 'set_manual_op', 'keep_na'], decisionId: null })
const cellFinding = (rowIndex, field, reason, classification = 'validation') => ({ id: `cell-${rowIndex}-${field}`, code: 'field_requires_confirmation', field,
  rule: 'existing_cell_correction', initialClassification: classification, resolution: 'pending', provenance: 'automatic', proposal: '', reason,
  detailIds: rows[rowIndex].detailIds, evidence: [], allowedActions: [], decisionId: null })
function summarize(value) {
  const review = value.data.preparation.review
  const result = { sourceRows: source.length, finalRows: value.groups.length, automaticRows: 0, humanResolvedRows: 0,
    validationRows: 0, conflictRows: 0, filePendingCases: 0, pendingCases: 0, pendingFindings: 0 }
  for (const item of review.cases) { const pending = item.findings.filter(f => f.resolution === 'pending')
    if (pending.length) { result.pendingCases++; result.pendingFindings += pending.length
      if (item.scope === 'file') result.filePendingCases++
      else result[pending.some(f => f.initialClassification === 'conflict') ? 'conflictRows' : 'validationRows']++ }
    else if (item.scope !== 'file') result[item.findings.some(f => f.provenance === 'human') ? 'humanResolvedRows' : 'automaticRows']++ }
  review.summary = result
  value.canExportDraft = true
  value.canExportFinal = !result.pendingCases && review.approvals.some(approval => approval.valid && approval.reportVersion === value.version)
  value.canApprove = !result.pendingCases && !value.canExportFinal
  value.canExport = value.canExportFinal
  return value
}
function fixture() { const value = { id, name: 'VENTAS MES de prueba', version: 1, updatedAt: '2026-10-07T12:00:00Z', lastExportedVersion: null,
  data: { currentSourceId: id, sourceFile: 'Ventas sintéticas.xlsx', sha256: 'synthetic-only', details: source, documents: [], edits: [], unappliedEdits: [], warnings: [],
    preparation: { ruleVersion: 3, status: 'generated_with_observations', historyFingerprint: 'synthetic-snapshot', identityKey: 'synthetic-v3', history,
      automaticRows: structuredClone(rows), rows: structuredClone(rows), rowEdits: [], changes: [{ id: 'cleanup', rowKey: null, kind: 'columns_removed', field: 'Columnas',
        before: ['IVA', 'TOTAL', 'NUMERO_REM'], after: '', reason: 'Se excluyeron las columnas indicadas.', detailIds: [], historyIds: [], manual: false }],
      review: { schemaVersion: 1, policyVersion: 1, cases: rows.map((row, n) => ({ id: `case-${n}`, rowKey: row.key, scope: 'row',
        classification: n === 5 ? 'conflict' : [0, 3, 4].includes(n) ? 'validation' : 'automatic', findings: [0, 3, 4, 5].includes(n) ? [finding(n, n === 5 ? 'conflict' : 'validation')] : [] })), decisions: [], approvals: [], summary: {} } } },
  groups: structuredClone(rows), controls: [], canExport: false, canExportDraft: true, canApprove: false, canExportFinal: false }
  const cases = value.data.preparation.review.cases
  cases[0].findings.push(cellFinding(0, 'VALOR_BRUT', 'Los importes de origen son diferentes. Confirma el valor de esta venta.', 'conflict'))
  cases[1].findings.push(cellFinding(1, 'VENDEDOR', 'VENDEDOR está vacío en esta venta.'))
  cases[2].findings.push(cellFinding(2, 'FECHA', 'FECHA está vacía en esta venta.'))
  return summarize(value)
}
let report = fixture()
function apply(payload) {
  const next = structuredClone(report), review = next.data.preparation.review
  for (const edit of payload.rowEdits ?? []) { const row = next.groups.find(value => value.key === edit.key); assert.ok(row)
    const before = structuredClone(row)
    for (const key of ['op', 'factura', 'number', 'date', 'client', 'term', 'line', 'seller']) if (key in edit) row[key] = edit[key]
    if (edit.setAmount) row.amount = edit.amount
    if ('detail' in edit) row.details = edit.detail.split('\n')
    row.modified = true
    next.data.preparation.rowEdits.push(edit)
    for (const approval of review.approvals) approval.valid = false
    const editedFields = { number: 'NUMERO', date: 'FECHA', client: 'NOMBRE', term: 'PLAZO', seller: 'VENDEDOR', line: 'LINEA', detail: 'DETALLE' }
    if (edit.setAmount) editedFields.amount = 'VALOR_BRUT'
    for (const [key, field] of Object.entries(editedFields)) {
      if (!(key in edit)) continue
      const item = review.cases.find(value => value.rowKey === row.key)
      const findings = item.findings.filter(value => value.field === field && value.resolution === 'pending')
      if (!findings.length) continue
      const decisionId = `decision-${review.decisions.length}`
      for (const issue of findings) { issue.resolution = 'resolved'; issue.provenance = 'human'; issue.decisionId = decisionId }
      review.decisions.push({ id: decisionId, caseId: item.id, findingIds: findings.map(value => value.id), action: 'field_edit',
        before: String(before[key] ?? ''), after: String(row[key] ?? ''), historyIds: [], actor: userId, occurredAt: '2026-10-07T13:00:00Z', reportVersion: report.version + 1, contentFingerprint: 'synthetic-field-edit' })
    }
  }
  for (const command of payload.decisions ?? []) { const item = review.cases.find(value => value.id === command.caseId); assert.ok(item)
    const row = next.groups.find(value => value.key === item.rowKey), before = row.op
    if (command.action === 'keep_na') { assert.equal(command.op, undefined); assert.equal(command.historyIds, undefined); row.op = 'N/A' }
    else if (command.action === 'set_manual_op') { assert.ok(command.op); row.op = command.op }
    else { assert.equal(command.action, 'select_candidates'); assert.ok(command.historyIds.length); row.op = command.historyIds.map(id => history.find(record => record.id === id).number).join(' / ') }
    const decisionId = `decision-${review.decisions.length}`
    for (const findingId of command.findingIds) { const issue = item.findings.find(value => value.id === findingId); assert.equal(issue.resolution, 'pending')
      issue.resolution = 'resolved'; issue.provenance = 'human'; issue.decisionId = decisionId }
    review.decisions.push({ id: decisionId, caseId: item.id, findingIds: command.findingIds, action: command.action, before, after: row.op,
      historyIds: command.historyIds ?? [], actor: userId, occurredAt: '2026-10-07T13:00:00Z', reportVersion: report.version + 1, contentFingerprint: 'synthetic-decision' })
  }
  next.data.preparation.rows = structuredClone(next.groups)
  return summarize(next)
}
await page.route('**/api/**', async route => {
  try { const path = new URL(route.request().url()).pathname, method = route.request().method()
    const json = (value, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    if (!path.startsWith('/api/')) return route.continue()
    if (path === '/api/auth/me') return json({ id: userId, documentNumber: 'TEST', firstName: 'Auxiliar', lastName: 'Prueba', email: 'auxiliar@example.test', area: null,
      roles: ['Auxiliar Comercial'], availableRoles: [{ id: userId, name: 'Auxiliar Comercial' }], activeRoleIds: [userId], permissions, isActive: true, mustChangePassword: false })
    if (path === '/api/auth/csrf') return json({ token: 'test-csrf' })
    if (method !== 'GET') assert.equal(route.request().headers()['x-xsrf-token'], 'test-csrf')
    if (path === '/api/commercial/reports' && method === 'GET') return json([report])
    if (path === '/api/commercial/reports' && method === 'POST') { creates++; return json(report) }
    if (path === `/api/commercial/reports/${id}` && method === 'GET') return json(report)
    if (path === `/api/commercial/reports/${id}/prepared/preview`) { previews++; lastPayload = route.request().postDataJSON(); assert.equal(lastPayload.version, report.version); return json(apply(lastPayload)) }
    if (path === `/api/commercial/reports/${id}/prepared`) { saves++; lastPayload = route.request().postDataJSON(); assert.equal(lastPayload.version, report.version)
      if (failSave) return json({ message: 'Otro usuario guardó una versión. Tus datos locales se conservan.' }, 409)
      report = apply(lastPayload); report.version++; summarize(report); actions.push(...(lastPayload.decisions ?? []).map(command => command.action))
      if (omitSourceRowsOnSave) { const response = structuredClone(report); delete response.data.sourceWarningEvidence
        for (const item of response.data.preparation.review.cases) for (const issue of item.findings) delete issue.sourceRows
        sourceEvidenceOmittedResponses++; return json(response) }
      return json(report) }
    if (path === `/api/commercial/reports/${id}/approve`) { assert.equal(route.request().postDataJSON().version, report.version)
      if (report.data.preparation.review.summary.pendingCases) return json({ message: 'Resuelve los casos pendientes.' }, 422)
      approvals++; report.version++; report.data.preparation.review.approvals.push({ actor: userId, occurredAt: '2026-10-07T14:00:00Z', reportVersion: report.version, contentFingerprint: 'synthetic-approved', valid: true }); return json(summarize(report)) }
    if (path === `/api/commercial/reports/${id}/export/draft` || path === `/api/commercial/reports/${id}/export`) {
      assert.equal(route.request().postDataJSON().version, report.version)
      const draft = path.endsWith('/draft')
      if (!draft && !report.canExportFinal) return json({ message: 'Falta aprobación vigente.' }, 422)
      if (draft) drafts++; else finals++
      return route.fulfill({ contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', headers: { 'content-disposition': `attachment; filename="${draft ? 'BORRADOR - ' : ''}VENTAS MES.xlsx"` }, body: Buffer.from('synthetic UI download; workbook checked by integration tests') }) }
    if (path === '/api/commercial/reports/ops') return json(history)
    routeErrors.push(`Unexpected ${method} ${path}`); return json({ message: 'Unexpected test endpoint' }, 404)
  } catch (error) { routeErrors.push(error.message); return route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ message: error.message }) }) }
})
const widths = [320, 390, 768, 1024, 1440]
async function fits(label) {
  const geometry = await page.evaluate(() => ({ fits: document.documentElement.scrollWidth <= innerWidth + 1,
    scrollers: [...document.querySelectorAll('.rw-workspace *')].filter(element => { const style = getComputedStyle(element)
      return element.clientWidth > 0 && style.display !== 'none' && style.visibility !== 'hidden' && ['auto', 'scroll'].includes(style.overflowX) && element.scrollWidth > element.clientWidth + 1 }).map(element => element.className) }))
  assert.equal(geometry.fits, true, `Page overflow: ${label}`); assert.deepEqual(geometry.scrollers, [], `Horizontal scroll: ${label}`)
}
async function download(button, draft) { const promise = page.waitForEvent('download'); await button.click(); const file = await promise
  assert.equal(file.suggestedFilename().includes('BORRADOR'), draft) }
async function screenshot(name) {
  await page.evaluate(() => { window.scrollTo({ top: 0, behavior: 'instant' }) })
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
  await page.screenshot({ path: `${artifactDir}/${name}.png`, fullPage: true })
}
try {
  await fs.mkdir(artifactDir, { recursive: true })
  await page.goto(`${base}/commercial/reports`)
  await page.getByRole('heading', { name: 'Informe de ventas', exact: true }).waitFor()
  await page.locator('#rw-manager-file').setInputFiles({ name: 'Ventas sintéticas.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: Buffer.from('synthetic source') })
  await page.getByRole('button', { name: 'Preparar reporte', exact: true }).click()
  await page.getByRole('heading', { name: 'INFORME DE VENTAS MENSUAL', exact: true }).first().waitFor()
  assert.equal(creates, 1)
  assert.equal(await page.locator('.rw-excel-table').isVisible(), false, 'Complete table must be secondary')
  assert.equal(await page.getByRole('button', { name: 'Descargar informe', exact: true }).count(), 0)
  await download(page.getByRole('button', { name: 'Descargar borrador', exact: true }), true)
  assert.equal(report.data.preparation.review.summary.pendingCases, 6)
  assert.equal(report.data.preparation.review.summary.pendingFindings, 7)
  assert.equal(await page.locator('.rw-incidence-count strong').innerText(), '7')
  const removedCopy = ['Resueltos por la auxiliar', 'Con conflictos pendientes', 'Además hay', 'Solo necesitas resolver los casos pendientes', 'Preparación del archivo', 'Columnas excluidas', 'BORRADOR VENTAS MES']
  for (const copy of removedCopy) assert.equal((await page.locator('.rw-workspace').innerText()).includes(copy), false, `Removed copy must stay hidden: ${copy}`)
  assert.equal(await page.locator('.rw-review-summary .rw-source-identity').getByText('Archivo de origen', { exact: true }).count(), 1)
  assert.equal(await page.locator('.rw-review-summary .rw-source-identity').getByText('Ventas sintéticas.xlsx', { exact: true }).count(), 1)
  assert.equal(await page.locator('.rw-review-summary').getByText('VENTAS MES de prueba', { exact: true }).count(), 0, 'Redundant report name must be removed')
  for (const width of widths) { await page.setViewportSize({ width, height: 960 }); await page.waitForTimeout(250); await fits(`summary ${width}`) }
  await page.setViewportSize({ width: 1440, height: 1050 })
  await screenshot('summary-desktop')
  await page.getByRole('button', { name: 'Ver las 7 incidencias pendientes', exact: true }).click()
  const activeCase = page.locator('.rw-exception-case')
  const list = page.locator('.rw-incidence-list')
  const listButtons = list.locator('ul > li > button')
  assert.equal(await listButtons.count(), 5)
  assert.equal(await list.getByRole('textbox').count(), 0, 'Incidences must be directly listed, without search')
  await list.getByRole('button', { name: 'Página siguiente', exact: true }).click()
  assert.equal(await listButtons.count(), 2)
  await list.getByRole('combobox', { name: 'Ir a la página', exact: true }).selectOption('1')
  assert.equal(await listButtons.count(), 5)
  await listButtons.filter({ hasText: 'VALOR_BRUT' }).filter({ hasText: 'Venta 8877' }).click()
  await activeCase.getByRole('heading', { name: 'Venta 8877', exact: true }).waitFor()
  const amount = activeCase.getByRole('spinbutton', { name: 'Corregir VALOR_BRUT', exact: true })
  await amount.waitFor()
  assert.equal(await page.locator('.rw-row-dialog').count(), 0, 'Cell correction must be inline')
  assert.equal(await activeCase.getByRole('button', { name: 'Escribir OP', exact: true }).count(), 0, 'The selected amount finding must not expose OP actions')
  await amount.fill('823500')
  assert.equal(await listButtons.evaluateAll(buttons => buttons.every(button => button.disabled)), true)
  assert.equal(await list.getByRole('combobox', { name: 'Ir a la página', exact: true }).isDisabled(), true)
  assert.equal(await list.getByRole('button', { name: 'Página siguiente', exact: true }).isDisabled(), true)
  await activeCase.getByRole('button', { name: 'Previsualizar', exact: true }).click()
  await activeCase.getByRole('status').waitFor()
  assert.equal(report.groups[0].amount, 100, 'Preview must not persist the corrected amount')
  failSave = true
  await activeCase.getByRole('button', { name: 'Guardar VALOR_BRUT', exact: true }).click()
  await activeCase.getByRole('alert').waitFor()
  assert.equal(await amount.inputValue(), '823500', '409 must preserve the typed correction')
  assert.equal(await list.getByRole('combobox', { name: 'Ir a la página', exact: true }).isDisabled(), true)
  assert.deepEqual(lastPayload.rowEdits, [{ key: 'row-0', amount: 823500, setAmount: true }])
  assert.deepEqual(lastPayload.decisions, [], 'An amount correction must not send an OP decision')
  failSave = false
  await activeCase.getByRole('button', { name: 'Guardar VALOR_BRUT', exact: true }).click()
  await page.getByRole('button', { name: 'Ver las 6 incidencias pendientes', exact: true }).waitFor()
  await listButtons.filter({ hasText: 'NUMERO OP' }).filter({ hasText: 'Venta 8877' }).click()
  await activeCase.getByRole('button', { name: 'Elegir OPs', exact: true }).waitFor()
  assert.equal(report.groups[0].amount, 823500)
  assert.equal(report.groups[0].op, 'N/A')
  assert.equal(report.data.preparation.review.cases[0].findings[0].resolution, 'pending', 'Saving amount must leave the OP finding unresolved')
  assert.equal(report.data.preparation.review.cases[0].findings[1].resolution, 'resolved')
  assert.equal(report.data.preparation.review.summary.pendingFindings, 6)
  assert.equal(report.data.preparation.review.summary.pendingCases, 6)
  await listButtons.filter({ hasText: 'Venta 8893' }).click()
  await activeCase.getByRole('heading', { name: 'Venta 8893', exact: true }).waitFor()
  await activeCase.getByRole('button', { name: 'Escribir OP', exact: true }).click()
  await activeCase.getByRole('textbox', { name: 'Número OP correcto', exact: true }).fill('25288')
  await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).click()
  await activeCase.getByRole('heading', { name: 'Venta 8877', exact: true }).waitFor()
  await activeCase.getByRole('button', { name: 'Elegir OPs', exact: true }).click()
  for (const number of ['25273', '25279', '25280']) await activeCase.getByRole('checkbox', { name: new RegExp(`OP ${number}`) }).check()
  assert.equal(await activeCase.getByRole('textbox', { name: 'FACTURA del caso actual', exact: true }).count(), 0)
  await activeCase.getByRole('button', { name: 'Editar FACTURA del caso actual', exact: true }).click()
  assert.equal(await activeCase.getByRole('textbox', { name: 'FACTURA del caso actual', exact: true }).inputValue(), 'F01')
  await activeCase.getByRole('textbox', { name: 'FACTURA del caso actual', exact: true }).fill('Temporal')
  await activeCase.getByRole('button', { name: 'Cancelar edición de FACTURA del caso actual', exact: true }).click()
  assert.equal(await activeCase.getByRole('textbox', { name: 'FACTURA del caso actual', exact: true }).count(), 0)
  assert.equal(await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).isEnabled(), true, 'Canceling FACTURA must retain the selected OPs')
  await activeCase.getByRole('button', { name: 'Editar FACTURA del caso actual', exact: true }).click()
  await activeCase.getByRole('textbox', { name: 'FACTURA del caso actual', exact: true }).fill('0007-MANUAL')
  await activeCase.getByRole('button', { name: 'Previsualizar', exact: true }).click()
  await activeCase.getByRole('status').waitFor()
  assert.equal(report.groups[0].factura, 'F01', 'Preview must not persist')
  assert.equal(report.data.preparation.review.summary.pendingCases, 5)
  for (const width of widths) { await page.setViewportSize({ width, height: 960 }); await page.waitForTimeout(250); await fits(`case ${width}`) }
  await page.setViewportSize({ width: 390, height: 844 })
  await screenshot('case-mobile')
  await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).click()
  await listButtons.filter({ hasText: 'Venta 8891' }).click()
  await activeCase.getByRole('heading', { name: 'Venta 8891', exact: true }).waitFor()
  assert.equal(report.groups[0].op, '25273 / 25279 / 25280'); assert.equal(report.groups[0].factura, '0007-MANUAL')
  assert.deepEqual(lastPayload.decisions[0].historyIds, history.map(record => record.id))
  await activeCase.getByRole('button', { name: 'Mantener N/A', exact: true }).click()
  failSave = true
  await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).click()
  await activeCase.getByRole('alert').waitFor()
  assert.equal(report.data.preparation.review.summary.pendingCases, 4)
  assert.equal(await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).isEnabled(), true)
  failSave = false
  await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).click()
  await listButtons.filter({ hasText: 'Venta 8892' }).click()
  await activeCase.getByRole('heading', { name: 'Venta 8892', exact: true }).waitFor()
  const kept = report.data.preparation.review.decisions.find(decision => decision.action === 'keep_na')
  assert.equal(kept.before, 'N/A'); assert.equal(kept.after, 'N/A')
  await activeCase.getByRole('button', { name: 'Escribir OP', exact: true }).click()
  await activeCase.getByRole('textbox', { name: 'Número OP correcto', exact: true }).fill('000449')
  await activeCase.getByRole('button', { name: 'Guardar decisión', exact: true }).click()
  await listButtons.filter({ hasText: 'VENDEDOR' }).filter({ hasText: 'Venta 8889' }).click()
  await activeCase.getByRole('textbox', { name: 'Corregir VENDEDOR', exact: true }).fill('Vendedor corregido')
  await activeCase.getByRole('button', { name: 'Guardar VENDEDOR', exact: true }).click()
  assert.deepEqual(lastPayload.rowEdits, [{ key: 'row-1', seller: 'Vendedor corregido' }])
  assert.deepEqual(lastPayload.decisions, [])
  await activeCase.locator('input[aria-label="Corregir FECHA"]').fill('2026-10-02')
  await activeCase.getByRole('button', { name: 'Guardar FECHA', exact: true }).click()
  assert.deepEqual(lastPayload.rowEdits, [{ key: 'row-2', date: '2026-10-02' }])
  assert.deepEqual(lastPayload.decisions, [])
  await page.getByRole('button', { name: 'Aprobar informe', exact: true }).waitFor()
  assert.equal(report.data.preparation.review.summary.pendingCases, 0)
  assert.equal(report.data.preparation.review.summary.humanResolvedRows, 6)
  assert.equal(report.data.preparation.review.summary.automaticRows, 1)
  assert.equal(report.canExportFinal, false)
  await page.getByRole('button', { name: 'Aprobar informe', exact: true }).click()
  await download(page.getByRole('button', { name: 'Descargar informe', exact: true }), false)
  assert.equal(approvals, 1); assert.equal(finals, 1)
  await page.goto(`${base}/commercial/reports/ventas/${id}`)
  await page.getByRole('button', { name: 'Descargar informe', exact: true }).waitFor()
  assert.equal(await page.locator('.rw-result-table').count(), 0)
  await page.getByRole('button', { name: 'Ver informe completo', exact: true }).click()
  const table = page.locator('.rw-result-table')
  await table.waitFor()
  await table.getByRole('combobox', { name: 'Ir a la página', exact: true }).selectOption('1')
  assert.equal(await table.locator('tbody tr').count(), 5)
  assert.equal(await table.locator('thead th').count(), 10)
  assert.deepEqual(await table.locator('thead th').allTextContents(), ['NUMERO OP', 'FACTURA', 'NUMERO', 'FECHA', 'NOMBRE', 'PLAZO', 'VALOR_BRUT', 'DETALLE', 'LINEA', 'VENDEDOR'])
  await table.getByRole('button', { name: 'Página siguiente', exact: true }).click()
  assert.equal(await table.locator('tbody tr').count(), 2)
  await table.getByRole('combobox', { name: 'Ir a la página', exact: true }).selectOption('1')
  const firstRow = table.locator('tbody tr').first()
  assert.equal(await firstRow.getByRole('textbox').count(), 0, 'FACTURA stays visible until Editar is selected')
  await firstRow.getByRole('button', { name: 'Editar factura de venta 8877', exact: true }).click()
  const invoice = firstRow.getByRole('textbox', { name: 'FACTURA de venta 8877', exact: true })
  assert.equal(await invoice.inputValue(), '0007-MANUAL')
  await invoice.fill('Temporal')
  await firstRow.getByRole('button', { name: 'Cancelar edición de factura de venta 8877', exact: true }).click()
  assert.equal(await firstRow.getByRole('textbox').count(), 0)
  assert.equal(report.groups[0].factura, '0007-MANUAL')
  await firstRow.getByRole('button', { name: 'Editar factura de venta 8877', exact: true }).click()
  await invoice.fill('0008-EDITADA')
  await invoice.press('Tab')
  failSave = true
  await firstRow.getByRole('button', { name: 'Guardar factura de venta 8877', exact: true }).click()
  await page.getByRole('alert').waitFor()
  assert.equal(await invoice.inputValue(), '0008-EDITADA', 'Failed invoice save must retain the entered value')
  failSave = false
  await firstRow.getByRole('button', { name: 'Guardar factura de venta 8877', exact: true }).click()
  await page.waitForFunction(() => document.body.innerText.includes('Aprobar informe'))
  assert.equal(await firstRow.getByRole('textbox').count(), 0, 'Successful invoice save closes the editor')
  assert.equal(report.groups[0].factura, '0008-EDITADA'); assert.equal(report.canExportFinal, false)
  assert.equal(report.data.preparation.review.summary.pendingCases, 0, 'FACTURA must not reopen OP')
  assert.equal(report.data.preparation.review.summary.humanResolvedRows, 6)
  for (const width of widths) { await page.setViewportSize({ width, height: 960 }); await page.waitForTimeout(250); await fits(`table ${width}`)
    assert.equal(await table.locator('tbody tr').count(), 5)
    assert.equal(await table.locator('tbody tr').first().locator('td[data-label]').evaluateAll(cells => cells.filter(cell => cell.getClientRects().length && getComputedStyle(cell).display !== 'none').length), 10) }
  await page.setViewportSize({ width: 1440, height: 1050 })
  await screenshot('complete-table-desktop')
  await page.getByRole('button', { name: 'Aprobar informe', exact: true }).click()
  await download(page.getByRole('button', { name: 'Descargar informe', exact: true }), false)
  await page.mouse.move(0, 0)
  await page.getByRole('button', { name: 'Descargar informe', exact: true }).evaluate(async button => {
    await Promise.all(button.getAnimations().map(animation => animation.finished))
  })
  const brand = await page.getByRole('button', { name: 'Descargar informe', exact: true }).evaluate(button => { const probe = document.createElement('span')
    document.body.append(probe); const palette = ['--orange', '--orange-dark'].map(token => { probe.style.color = `var(${token})`; return getComputedStyle(probe).color }); probe.remove()
    return { palette, actual: getComputedStyle(button).backgroundColor } })
  assert.ok(brand.palette.includes(brand.actual), `Primary action must use the existing Portal palette: ${JSON.stringify(brand)}`)
  permissions = ['commercial.reports.view']
  await page.goto(`${base}/commercial/reports/ventas/${id}`)
  await page.getByRole('heading', { name: 'INFORME DE VENTAS MENSUAL', exact: true }).first().waitFor()
  assert.equal(await page.getByRole('button', { name: 'Descargar informe', exact: true }).count(), 0)
  assert.equal(await page.getByRole('button', { name: 'Descargar borrador', exact: true }).count(), 0)
  // Separate file scenario: evidence is inspectable, but no unapproved business rule resolves it.
  permissions = ['commercial.reports.view', 'commercial.reports.edit', 'commercial.reports.export']
  report = fixture()
  const fileRows = Array.from({ length: 6 }, (_, index) => ({ sourceFile: 'Ventas sintéticas.xlsx', sheet: 'Notas originales', sourceRow: index + 27,
    cells: [{ column: 'B', header: 'FECHA', value: '' }, { column: 'G', header: 'VALOR_BRUT', value: '' }, { column: 'H', header: 'DETALLE', value: `Nota original conservada ${index + 27}` }] }))
  const fileFinding = { id: 'file-pending', code: 'file_source_warning', field: 'Archivo', rule: 'existing_source_warning', initialClassification: 'validation',
    resolution: 'pending', provenance: 'automatic', proposal: '', reason: 'Se omitieron 6 filas sin FECHA ni VALOR_BRUT. Revisa su contenido original.',
    detailIds: [], evidence: [], sourceRows: fileRows, allowedActions: [], decisionId: null }
  report.data.sourceWarningEvidence = [{ warning: fileFinding.reason, rows: fileRows }]
  report.data.preparation.review.cases.push({ id: 'case-file', rowKey: null, scope: 'file', classification: 'validation', findings: [fileFinding] })
  report.data.preparation.changes.push({ id: 'file-observation', rowKey: null, kind: 'observation', field: 'Archivo', before: [], after: '', reason: fileFinding.reason, detailIds: [], historyIds: [], manual: false })
  summarize(report)
  await page.goto(`${base}/commercial/reports/ventas/${id}`)
  await page.getByRole('button', { name: 'Ver incidencias', exact: true }).waitFor()
  assert.equal(await page.locator('.rw-incidence-count strong').innerText(), '8', 'File findings are included in the incidence count')
  assert.equal(report.data.preparation.review.summary.filePendingCases, 1)
  await page.getByRole('button', { name: 'Ver incidencias', exact: true }).click()
  assert.equal(await listButtons.count(), 5)
  await list.getByRole('button', { name: 'Página siguiente', exact: true }).click()
  assert.equal(await listButtons.count(), 3)
  await listButtons.filter({ hasText: 'Archivo de origen' }).click()
  await activeCase.getByRole('heading', { name: 'Incidencia del archivo', exact: true }).waitFor()
  const fileEvidence = activeCase.getByRole('region', { name: 'Filas originales de la incidencia', exact: true })
  assert.equal(await fileEvidence.locator('article').count(), 5)
  await fileEvidence.getByRole('heading', { name: 'Fila 27', exact: true }).waitFor()
  assert.equal(await fileEvidence.getByText('Hoja: Notas originales', { exact: true }).count(), 5)
  assert.equal(await fileEvidence.getByText('Nota original conservada 27', { exact: true }).count(), 1)
  assert.equal(await fileEvidence.getByText('Columna H', { exact: true }).count(), 5)
  await fileEvidence.getByRole('button', { name: 'Página siguiente', exact: true }).click()
  assert.equal(await fileEvidence.locator('article').count(), 1)
  await fileEvidence.getByRole('heading', { name: 'Fila 32', exact: true }).waitFor()
  assert.equal(await fileEvidence.getByText('Nota original conservada 32', { exact: true }).count(), 1)
  assert.equal(await activeCase.getByRole('button', { name: /^Guardar/ }).count(), 0, 'No invented action may resolve a source-file observation')
  assert.equal(await activeCase.getByRole('textbox').count(), 0)
  assert.equal(report.canApprove, false)
  assert.equal(report.canExportFinal, false)
  // A legacy GET can enrich evidence that an unrelated save response does not repeat.
  // The visible row evidence must survive that save without resolving the file finding.
  await list.getByRole('combobox', { name: 'Ir a la página', exact: true }).selectOption('1')
  await listButtons.filter({ hasText: 'VALOR_BRUT' }).filter({ hasText: 'Venta 8877' }).click()
  await activeCase.getByRole('spinbutton', { name: 'Corregir VALOR_BRUT', exact: true }).fill('900')
  omitSourceRowsOnSave = true
  await activeCase.getByRole('button', { name: 'Guardar VALOR_BRUT', exact: true }).click()
  await page.getByRole('button', { name: 'Ver las 7 incidencias pendientes', exact: true }).waitFor()
  omitSourceRowsOnSave = false
  assert.equal(sourceEvidenceOmittedResponses, 1)
  assert.equal(report.data.preparation.review.summary.filePendingCases, 1)
  assert.equal(report.canApprove, false)
  assert.equal(report.canExportFinal, false)
  await list.getByRole('combobox', { name: 'Ir a la página', exact: true }).selectOption('2')
  await listButtons.filter({ hasText: 'Archivo de origen' }).click()
  await activeCase.getByRole('heading', { name: 'Incidencia del archivo', exact: true }).waitFor()
  assert.equal(await fileEvidence.locator('article').count(), 5)
  await fileEvidence.getByRole('heading', { name: 'Fila 27', exact: true }).waitFor()
  assert.equal(await fileEvidence.getByText('Hoja: Notas originales', { exact: true }).count(), 5)
  assert.equal(await fileEvidence.getByText('Nota original conservada 27', { exact: true }).count(), 1)
  for (const width of widths) { await page.setViewportSize({ width, height: 960 }); await page.waitForTimeout(250); await fits(`file incidence ${width}`) }
  await page.setViewportSize({ width: 390, height: 844 })
  await screenshot('file-incidence-mobile')
  for (const copy of removedCopy) assert.equal((await page.locator('.rw-workspace').innerText()).includes(copy), false, `Removed copy must stay hidden in file scenario: ${copy}`)
  assert.deepEqual(errors, []); assert.deepEqual(routeErrors, [])
  const result = { status: 'passed', mode: 'synthetic-ui-with-mocked-api', creates, previews, saves, approvals, drafts, finals, actions,
    completeReportOpenedDirectly: true, incidencePagination: [5, 2], inlineCellCorrection: true, independentOpAndAmountFindings: true,
    defaultInvoice: 'F01', invoiceEditingRequiresExplicitAction: true, invoiceCancelPreservesOpDecision: true, invoiceSaveFailureRetainsInput: true,
    concurrencyRetainsTypedCorrectionAndLocksList: true, sourceFileEvidencePagination: [5, 1], fileFindingRemainsPending: true,
    sourceEvidenceSurvivesUnrelatedLegacySave: true, sourceEvidenceOmittedResponses,
    responsiveWidths: widths, screenshotDirectory: artifactDir }
  await fs.writeFile(`${artifactDir}/result.json`, JSON.stringify(result, null, 2))
  console.log(JSON.stringify(result))
} catch (error) { console.error(JSON.stringify({ url: page.url(), errors, routeErrors, text: (await page.locator('body').innerText()).slice(0, 6000) })); throw error }
finally { await browser.close() }
