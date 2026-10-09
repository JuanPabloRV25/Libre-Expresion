import { describe, expect, it } from 'vitest'
import { initialReviewerSelection } from './reviewAssignment'

describe('review assignment choice', () => {
  it('leaves an empty list without a selection', () => expect(initialReviewerSelection([])).toBe(''))
  it('preselects the only available reviewer', () => expect(initialReviewerSelection([{ id: 'a', name: 'Ana', secondaryLabel: null }])).toBe('a'))
  it('requires an explicit choice with multiple reviewers on every submission', () => {
    const candidates = [{ id: 'a', name: 'Ana', secondaryLabel: null }, { id: 'b', name: 'Bea', secondaryLabel: null }]
    expect(initialReviewerSelection(candidates)).toBe('')
    expect(initialReviewerSelection(candidates.slice().reverse())).toBe('')
  })
})
