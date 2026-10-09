import { describe, expect, it } from 'vitest'
import { ApiError } from '../../../api/httpClient'
import { documentStatusLabel, isProductionOrderVersionConflict } from './documentPackage'

describe('commercial document package', () => {
  it('distinguishes current status from the available action', () => {
    expect(documentStatusLabel('purchaseOrder', 'pending')).toBe('Pendiente')
    expect(documentStatusLabel('purchaseOrder', 'attached')).toBe('Archivo adjunto')
    expect(documentStatusLabel('purchaseOrder', 'notApplicable')).toBe('Marcada como no aplica')
    expect(documentStatusLabel('design', 'notApplicable')).toBe('Marcado como no requerido')
  })

  it('only identifies the production-order version conflict', () => {
    expect(isProductionOrderVersionConflict(new ApiError(409, 'production_order_version_conflict'))).toBe(true)
    expect(isProductionOrderVersionConflict(new ApiError(409, 'another_conflict'))).toBe(false)
    expect(isProductionOrderVersionConflict(new Error('network'))).toBe(false)
  })
})
