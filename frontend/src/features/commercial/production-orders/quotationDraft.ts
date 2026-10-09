import type { CommercialOrderInput, QuotationPreview } from './types'

export function commercialDraftFromQuotation(
  preview: QuotationPreview,
  itemIndex: number,
  optionIndex: number,
): Partial<CommercialOrderInput> {
  const item = preview.items.find((candidate) => candidate.index === itemIndex)
  const option = item?.options[optionIndex]
  if (!item || !option) throw new Error('Selecciona una referencia y una opción de cantidad válidas.')

  const deliveryCount = Number(item.description.match(/NOTA\s*:?\s*(\d+)\s+ENTREGAS/i)?.[1] ?? 0)
  const partialDelivery = deliveryCount > 1
  const inks = item.inks?.replace(/x/gi, ' X ') ?? ''

  return {
    quotationNumber: preview.quotationNumber ?? '',
    clientName: preview.clientName ?? '',
    productName: item.productName ?? item.description,
    referenceNumber: extractInternalReference(item.description),
    quantity: option.quantity,
    unitValue: option.unitValue,
    cityCountry: preview.cityCountry ?? '',
    address: preview.address ?? '',
    openSize: item.openSize ?? '',
    observations: item.description,
    partialDelivery,
    partialDeliveryQuantity: partialDelivery ? option.quantity / deliveryCount : null,
    materials: item.material || item.caliber
      ? [{ material: item.material ?? '', weight: item.caliber ?? '', caliber: '', optionalSpecifications: '' }]
      : [{ material: '', weight: '', caliber: '', optionalSpecifications: '' }],
    printLines: item.inks || item.process
      ? [{ product: item.productName ?? '', inks, process: item.process ?? '', specials: '' }]
      : [{ product: '', inks: '', process: '', specials: '' }],
    finishes: [{ specification: '', front: false, back: false, reserve: false }],
  }
}

function extractInternalReference(description: string) {
  return description.match(/(?:ITEM|REF(?:ERENCIA)?)\s*[:#-]?\s*([A-Z0-9-]+)/i)?.[1] ?? ''
}
