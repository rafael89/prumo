import { test } from 'node:test'
import assert from 'node:assert/strict'
import { contributionExecution } from './contributionExecution.ts'

test('single execution preserves the actual quantity and price', () => {
  assert.deepEqual(contributionExecution('g', { assetId: 'A' }, 2, 100), {
    guidanceId: 'g', items: [{ assetId: 'A', candidateKey: undefined, quantity: 2, unitPrice: 100 }],
  })
})

test('A and B are independent executions, not copies of planned amounts or each other', () => {
  const allocations = [{ assetId: 'A', amount: 600 }, { assetId: 'B', amount: 400 }]
  const first = contributionExecution('g', allocations[0], 2, 100)
  const later = contributionExecution('g', allocations[1], 3, 90)
  assert.deepEqual(first.items.map(x => x.assetId), ['A'])
  assert.deepEqual(later.items.map(x => x.assetId), ['B'])
  assert.equal(first.items[0].quantity * first.items[0].unitPrice, 200)
  assert.equal(later.items[0].quantity * later.items[0].unitPrice, 270)
})
