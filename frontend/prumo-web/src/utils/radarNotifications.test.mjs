import { test } from 'node:test'
import assert from 'node:assert/strict'
import { consumeRadarNotifications } from './radarNotifications.ts'

const item = documentAnalysisId => ({ documentAnalysisId })

test('shows only the newest document and consumes the entire batch', () => {
  const items = [item('latest'), item('older'), item('oldest')]
  const notified = new Set()
  assert.equal(consumeRadarNotifications(items, notified), items[0])
  assert.equal(consumeRadarNotifications(items, notified), null)
  assert.equal(consumeRadarNotifications(items.slice(1), notified), null)
  assert.deepEqual([...notified], ['latest', 'older', 'oldest'])
})

test('a saved latest document never falls through to older unseen items', () => {
  const notified = new Set(['latest'])
  assert.equal(consumeRadarNotifications([item('latest'), item('older')], notified), null)
  assert.equal(notified.has('older'), true)
})

test('a new batch shows only its latest document, once across reloads', () => {
  const notified = new Set(['previous'])
  const latest = item('newest')
  const items = [latest, item('new'), item('previous')]
  assert.equal(consumeRadarNotifications(items, notified), latest)
  const restored = new Set(JSON.parse(JSON.stringify([...notified])))
  assert.equal(consumeRadarNotifications(items, restored), null)
  assert.equal(consumeRadarNotifications([], restored), null)
})
