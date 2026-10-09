import assert from 'node:assert/strict'
const { chromium } = await import(process.env.PORTAL_PLAYWRIGHT_MODULE ?? 'playwright')

const browser = await chromium.launch({ headless: true })
const page = await browser.newPage()
const errors = []
page.on('pageerror', error => errors.push(error.message))
const base = process.env.PORTAL_TEST_FRONTEND_URL ?? 'http://127.0.0.1:5173'
const id = '10000000-0000-0000-0000-000000000001'
const a = { id: '20000000-0000-0000-0000-000000000001', name: 'Ana Prueba', secondaryLabel: null }
const b = { id: '20000000-0000-0000-0000-000000000002', name: 'Beatriz Prueba', secondaryLabel: null }
const draft = () => ({
  id, code: 'OP-2026-00001', status: 'draft', version: 1, sourceOrderId: null,
  operationGroupId: id, commercialOwner: { id, name: 'Agente Prueba' }, currentAssignee: { id, name: 'Agente Prueba' },
  reviewOwner: null, productionOwner: null,
  commercial: { customerOrderNumber: null, quotationNumber: 'TEST', clientName: 'Cliente ficticio', productName: 'Caja',
    deliveryDate: '2026-10-15', quantity: 100, unitValue: 100, totalValue: 10000, workType: 'new', dieType: 'none',
    qualityCertificateMode: 'none', technicalSheetMode: 'none' },
  production: {}, materials: [], printLines: [], finishes: [], history: [], documents: [], relatedOrders: [],
  checklist: { quotationReady: true, purchaseOrder: 'notApplicable', design: 'notApplicable', complete: true },
  allowedActions: ['view', 'submitForReview'], createdAt: '2026-10-05T12:00:00Z', updatedAt: '2026-10-05T12:00:00Z',
})
let order = draft(), candidates = [], loadFailure = false, posts = 0, postMode = 'success', release
let candidateRequests = 0
await page.route('**/api/**', async route => {
  const path = new URL(route.request().url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  if (path === '/api/auth/me') return json({ id, documentNumber: 'TEST', firstName: 'Agente', lastName: 'Prueba', email: 'agent@example.test',
    area: null, roles: ['Agente Comercial'], availableRoles: [{ id, name: 'Agente Comercial' }], activeRoleIds: [id],
    permissions: ['commercial.production_orders.view', 'commercial.production_orders.submit_for_review'], isActive: true, mustChangePassword: false })
  if (path === '/api/auth/csrf') return json({ token: 'test-only-csrf' })
  if (path.endsWith('/reviewers')) { candidateRequests++; return loadFailure ? json({ message: 'Controlled failure' }, 503) : json(candidates) }
  if (path.endsWith('/submit-for-review')) {
    posts++
    const body = route.request().postDataJSON()
    assert.equal(body.version, order.version)
    assert.equal(route.request().headers()['x-xsrf-token'], 'test-only-csrf')
    if (postMode === 'unavailable') return json({ code: 'reviewer_unavailable', message: 'La auxiliar seleccionada ya no está disponible.' }, 409)
    if (postMode === 'conflict') { order = { ...order, status: 'pendingCommercialReview', version: 2, reviewOwner: b, currentAssignee: b, allowedActions: ['view'] }; return json({ code: 'production_order_version_conflict', message: 'La orden cambió.' }, 409) }
    if (postMode === 'delay') await new Promise(resolve => { release = resolve })
    const selected = candidates.find(item => item.id === body.reviewerUserId)
    assert.ok(selected)
    order = { ...order, status: 'pendingCommercialReview', version: order.version + 1, reviewOwner: selected, currentAssignee: selected, allowedActions: ['view'] }
    return json(order)
  }
  if (path === `/api/commercial/production-orders/${id}`) return json(order)
  return json([])
})
const visit = () => page.goto(`${base}/commercial/production-orders/${id}`)
const open = () => page.getByRole('button', { name: 'Enviar a revisión', exact: true }).click()
const dialog = () => page.getByRole('dialog', { name: 'Enviar a revisión comercial' })
const confirm = () => dialog().getByRole('button', { name: 'Enviar a revisión', exact: true })
const ready = async () => { await page.getByRole('heading', { name: 'OP-2026-00001' }).waitFor({ timeout: 10000 }) }
const results = []
try {
  await visit(); await ready(); await open()
  await dialog().getByText('No hay auxiliares comerciales disponibles para revisar esta orden.').waitFor()
  assert.equal(await confirm().isDisabled(), true)
  await dialog().getByRole('button', { name: 'Cancelar' }).click()
  assert.equal(posts, 0); results.push('cero candidatas y cancelar sin cambios')

  loadFailure = true; await open()
  await dialog().getByText('No fue posible consultar las auxiliares. Reintentar.').waitFor()
  assert.equal(await confirm().isDisabled(), true)
  loadFailure = false; candidates = [a]
  await dialog().getByRole('button', { name: 'Reintentar' }).click()
  await dialog().getByText('Revisará: Ana Prueba').waitFor()
  assert.equal(await confirm().isEnabled(), true)
  await dialog().getByRole('button', { name: 'Cancelar' }).click()
  assert.equal(posts, 0); results.push('fallo de consulta distinto de lista vacía y reintento con una candidata')

  candidates = [a, b]; await open(); await dialog().getByRole('combobox').waitFor()
  assert.equal(await dialog().getByRole('combobox').inputValue(), '')
  assert.equal(await confirm().isDisabled(), true)
  await dialog().getByRole('combobox').selectOption(b.id)
  postMode = 'delay'; await confirm().click()
  await dialog().getByRole('button', { name: 'Enviando…' }).waitFor()
  assert.equal(await dialog().getByRole('button', { name: 'Cancelar' }).isDisabled(), true)
  await page.keyboard.press('Escape'); assert.equal(await dialog().isVisible(), true)
  for (let attempt = 0; !release && attempt < 100; attempt++) await new Promise(resolve => setTimeout(resolve, 10))
  assert.equal(posts, 1); assert.ok(release); release()
  await dialog().waitFor({ state: 'hidden' })
  await page.reload(); await ready()
  await page.getByText('Asignada a: Beatriz Prueba', { exact: true }).waitFor()
  assert.equal(posts, 1); results.push('elección explícita, bloqueo durante envío y responsable persistente al recargar')

  order = { ...draft(), status: 'correctionRequired', reviewOwner: a, lastReturnReason: 'Corregir' }; postMode = 'success'
  await visit(); await ready(); await open(); await dialog().getByRole('combobox').waitFor()
  assert.equal(await dialog().getByRole('combobox').inputValue(), '')
  await dialog().getByText('Revisión anterior: Ana Prueba. Puedes elegirla nuevamente o seleccionar otra auxiliar.').waitFor()
  await dialog().getByRole('combobox').selectOption(b.id)
  await dialog().getByText('Este envío quedará asignado a Beatriz Prueba en lugar de Ana Prueba.').waitFor()
  await dialog().getByRole('button', { name: 'Cancelar' }).click(); results.push('reenvío sugiere anterior sin elegirla automáticamente')

  candidates = [b]; await open()
  await dialog().getByText('Ana Prueba ya no está disponible. Elige a otra auxiliar para este envío.').waitFor()
  await dialog().getByText('Revisará: Beatriz Prueba').waitFor()
  await dialog().getByRole('button', { name: 'Cancelar' }).click(); results.push('revisora anterior inhabilitada y aviso de cambio')

  candidates = [a, b]; postMode = 'unavailable'; await open(); await dialog().getByRole('combobox').selectOption(a.id)
  const before = candidateRequests; await confirm().click()
  await dialog().getByText('La auxiliar seleccionada ya no está disponible.').waitFor()
  await page.waitForFunction(() => document.querySelector('[role="dialog"] select')?.value === '')
  assert.ok(candidateRequests > before); assert.equal(await confirm().isDisabled(), true)
  await dialog().getByRole('button', { name: 'Cancelar' }).click(); results.push('rechazo de elegibilidad obliga a consultar y elegir nuevamente')

  postMode = 'conflict'; await open(); await dialog().getByRole('combobox').selectOption(a.id); await confirm().click()
  await dialog().waitFor({ state: 'hidden' })
  await page.getByText('Asignada a: Beatriz Prueba', { exact: true }).waitFor()
  await page.getByText('La orden cambió. Consulta el estado y responsable actuales antes de continuar.').waitFor()
  assert.equal(posts, 3); results.push('conflicto recarga estado y responsable sin repetir el envío')
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ browser: 'Chromium', passed: results.length, scenarios: results, pageErrors: errors }, null, 2))
} catch (error) {
  console.log(JSON.stringify({ url: page.url(), pageErrors: errors, visibleText: (await page.locator('body').innerText()).slice(0, 1500) }))
  throw error
} finally { await browser.close() }
