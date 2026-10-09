export interface ProductionOrderReviewer { id: string; name: string; secondaryLabel: string | null }

export type ProductionOrderStatus = 'draft' | 'pendingCommercialReview' | 'correctionRequired' | 'readyForProduction' | 'inProduction' | 'completed' | 'cancelled'
export type ProductionOrderAction = 'view' | 'editCommercial' | 'discard' | 'submitForReview' | 'approveReview' | 'returnForCorrection' | 'duplicate' | 'receive' | 'editProduction' | 'complete' | 'cancel'

export interface OrderPerson { id: string; name: string }

export interface ProductionOrderSummary {
  id: string
  code: string
  status: ProductionOrderStatus
  version: number
  customerOrderNumber: string | null
  quotationNumber: string | null
  clientName: string | null
  productName: string | null
  deliveryDate: string | null
  quantity: number | null
  totalValue: number | null
  commercialOwner: OrderPerson
  productionOwner: OrderPerson | null
  createdAt: string
  updatedAt: string
  allowedActions: ProductionOrderAction[]
}

export interface CommercialOrderData {
  customerOrderNumber: string | null
  quotationNumber: string | null
  deliveryDate: string | null
  clientName: string | null
  productName: string | null
  referenceNumber: string | null
  clientPurchaseOrder: string | null
  quantity: number | null
  unitValue: number | null
  totalValue: number | null
  cityCountry: string | null
  address: string | null
  workType: string
  printColorProof: boolean
  dieType: string
  openSize: string | null
  closedSize: string | null
  observations: string | null
  additionalSpecifications: string | null
  receptionContact: string | null
  deliveryAddress: string | null
  receptionSchedule: string | null
  partialDelivery: boolean
  partialDeliveryQuantity: number | null
  legalContractRequirements: string | null
  dispatchDay: string | null
  qualityCertificateMode: string
  technicalSheetMode: string
}

export interface ProductionOrderData {
  planningDate: string | null
  planningManager: string | null
  materialCutDate: string | null
  cuttingManager: string | null
  printStartShift1: string | null
  printingManagerShift1: string | null
  printStartShift2: string | null
  printingManagerShift2: string | null
  finishingStart: string | null
  finishingManager: string | null
  dieCutStart: string | null
  dieCutManager: string | null
  dieMachine: string | null
  dieNumber: string | null
  dieTotalProcessed: number | null
  dieConforming: number | null
  dieNonConforming: number | null
  gluingStart: string | null
  gluingManager: string | null
  glueType: string | null
  glueTotalProcessed: number | null
  glueConforming: number | null
  glueNonConforming: number | null
  qualityReviewDate: string | null
  qualityReviewer: string | null
  qualityApproved: boolean | null
  qualityNotes: string | null
}

export interface OrderMaterial {
  id: string
  position: number
  material: string | null
  weight: string | null
  caliber: string | null
  optionalSpecifications: string | null
  sheetSize: string | null
  sheetQuantity: number | null
  cutSize: string | null
  fractionPerSheet: number | null
  fitPerFraction: number | null
  totalCutQuantity: number | null
  conformingQuantity: number | null
  nonConformingQuantity: number | null
}

export interface OrderPrintLine {
  id: string
  position: number
  product: string | null
  inks: string | null
  process: string | null
  specials: string | null
  machine: string | null
  mounting: string | null
  shotsToProcess: number | null
  conformingQuantity: number | null
  nonConformingQuantity: number | null
}

export interface OrderFinish {
  id: string
  position: number
  specification: string | null
  front: boolean
  back: boolean
  reserve: boolean
  totalProcessed: number | null
  conformingQuantity: number | null
  nonConformingQuantity: number | null
}

export interface OrderHistory {
  id: string
  status: ProductionOrderStatus
  actor: OrderPerson
  note: string | null
  occurredAt: string
}

export interface ProductionOrderDetail {
  id: string
  code: string
  status: ProductionOrderStatus
  version: number
  sourceOrderId: string | null
  operationGroupId: string
  commercialOwner: OrderPerson
  currentAssignee: OrderPerson | null
  reviewOwner: OrderPerson | null
  productionOwner: OrderPerson | null
  commercial: CommercialOrderData
  production: ProductionOrderData
  materials: OrderMaterial[]
  printLines: OrderPrintLine[]
  finishes: OrderFinish[]
  history: OrderHistory[]
  documents: ProductionOrderDocument[]
  checklist: ProductionOrderChecklist
  relatedOrders: RelatedProductionOrder[]
  lastReturnReason: string | null
  createdAt: string
  updatedAt: string
  submittedAt: string | null
  reviewSubmittedAt: string | null
  reviewReturnedAt: string | null
  productionReceivedAt: string | null
  completedAt: string | null
  allowedActions: ProductionOrderAction[]
}

export interface RelatedProductionOrder {
  id: string
  code: string
  status: ProductionOrderStatus
  productName: string | null
  quantity: number | null
}

export interface CommercialMaterialInput { material: string; weight: string; caliber: string; optionalSpecifications: string }
export interface CommercialPrintLineInput { product: string; inks: string; process: string; specials: string }
export interface CommercialFinishInput { specification: string; front: boolean; back: boolean; reserve: boolean }

export interface CommercialOrderInput {
  version?: number
  quotationNumber: string
  deliveryDate: string | null
  clientName: string
  productName: string
  referenceNumber: string
  clientPurchaseOrder: string
  quantity: number | null
  unitValue: number | null
  cityCountry: string
  address: string
  workType: string
  printColorProof: boolean
  dieType: string
  openSize: string
  closedSize: string
  observations: string
  additionalSpecifications: string
  receptionContact: string
  deliveryAddress: string
  receptionSchedule: string
  partialDelivery: boolean
  partialDeliveryQuantity: number | null
  legalContractRequirements: string
  dispatchDay: string | null
  qualityCertificateMode: string
  technicalSheetMode: string
  materials: CommercialMaterialInput[]
  printLines: CommercialPrintLineInput[]
  finishes: CommercialFinishInput[]
}

export interface ProductionOrderDocument {
  id: string
  type: 'quotation' | 'purchaseOrder' | 'design'
  applicability: 'pending' | 'attached' | 'notApplicable'
  originalFileName: string
  contentType: string
  size: number
  sha256: string
  uploadedAt: string
}

export interface ProductionOrderChecklist {
  quotationReady: boolean
  purchaseOrder: 'pending' | 'attached' | 'notApplicable'
  design: 'pending' | 'attached' | 'notApplicable'
  complete: boolean
}

export interface QuotationPriceOption { quantity: number; unitValue: number; netValue: number }
export interface QuotationItem {
  index: number
  description: string
  productName: string | null
  openSize: string | null
  material: string | null
  caliber: string | null
  inks: string | null
  process: string | null
  options: QuotationPriceOption[]
}
export interface QuotationPreview {
  fileName: string
  format: string
  templateFingerprint: string
  quotationNumber: string | null
  clientName: string | null
  cityCountry: string | null
  address: string | null
  items: QuotationItem[]
  warnings: string[]
}

export interface ProductionOrderInput extends ProductionOrderData {
  version: number
  materials: Array<Pick<OrderMaterial, 'id' | 'sheetSize' | 'sheetQuantity' | 'cutSize' | 'fractionPerSheet' | 'fitPerFraction' | 'totalCutQuantity' | 'conformingQuantity' | 'nonConformingQuantity'>>
  printLines: Array<Pick<OrderPrintLine, 'id' | 'machine' | 'mounting' | 'shotsToProcess' | 'conformingQuantity' | 'nonConformingQuantity'>>
  finishes: Array<Pick<OrderFinish, 'id' | 'totalProcessed' | 'conformingQuantity' | 'nonConformingQuantity'>>
}
