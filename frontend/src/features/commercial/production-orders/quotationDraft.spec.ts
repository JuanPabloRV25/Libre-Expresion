import { describe, expect, it } from 'vitest'
import { commercialDraftFromQuotation } from './quotationDraft'
import type { QuotationPreview } from './types'

// Synthetic quotation data; no customer records.
const preview: QuotationPreview = {
  fileName: 'Cotizacion.pdf',
  format: 'pdf',
  templateFingerprint: 'sample',
  quotationNumber: '99999-1',
  clientName: 'CLIENTE FICTICIO SAS',
  cityCountry: 'CIUDAD FICTICIA',
  address: 'CALLE FICTICIA 123',
  warnings: [],
  items: [{
    index: 0,
    description: 'C - PLEGADIZA: PRODUCTO FICTICIO - ABIERTA: 20CMX10CM - MATERIAL: CARTULINA FICTICIA CAL 30 - IMPRESION: 1X0 (PANTONE) - TROQUELADO - PEGADO 4 PUNTAS NOTA: 2 ENTREGAS EN EL MES',
    productName: 'PRODUCTO FICTICIO',
    openSize: '20CMX10CM',
    material: 'CARTULINA FICTICIA',
    caliber: '30',
    inks: '1X0',
    process: 'PANTONE',
    options: [{ quantity: 1_000, unitValue: 10, netValue: 10_000 }],
  }],
}

describe('commercialDraftFromQuotation', () => {
  it('maps the EMLAZE quotation into the editable commercial OP without persisting it', () => {
    const draft = commercialDraftFromQuotation(preview, 0, 0)

    expect(draft).toMatchObject({
      quotationNumber: '99999-1',
      clientName: 'CLIENTE FICTICIO SAS',
      productName: 'PRODUCTO FICTICIO',
      quantity: 1_000,
      unitValue: 10,
      cityCountry: 'CIUDAD FICTICIA',
      address: 'CALLE FICTICIA 123',
      openSize: '20CMX10CM',
      partialDelivery: true,
      partialDeliveryQuantity: 500,
    })
    expect(draft.materials).toEqual([{ material: 'CARTULINA FICTICIA', weight: '30', caliber: '', optionalSpecifications: '' }])
    expect(draft.printLines).toEqual([{ product: 'PRODUCTO FICTICIO', inks: '1 X 0', process: 'PANTONE', specials: '' }])
    expect(draft.observations).toContain('PEGADO 4 PUNTAS')
  })
})
