import type { ProductionOrderReviewer } from './types'

export function initialReviewerSelection(reviewers: ProductionOrderReviewer[]): string {
  return reviewers.length === 1 ? reviewers[0]!.id : ''
}
