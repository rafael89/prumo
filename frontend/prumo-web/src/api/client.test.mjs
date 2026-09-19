import { test } from 'node:test'
import assert from 'node:assert/strict'
import { getOptionalJson, readError, sendForm, sendJson } from './client.ts'
import { contributionExecution } from '../utils/contributionExecution.ts'

test('preview, confirmation, simulation and partial executions preserve request contracts', async t => {
  const calls = []
  const responses = [{ importSessionId: 'import' }, { status: 'Completed' }, { guidanceId: 'guidance', jobStatus: 'Queued' }, {}, {}]
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    calls.push({ url, ...options })
    return Response.json(responses.shift())
  })
  const file = new FormData()
  file.append('file', new Blob(['xlsx']), 'portfolio.xlsx')
  const preview = await sendForm('/api/portfolios/p/imports/b3/preview', file)
  await sendJson(`/api/portfolios/p/imports/${preview.importSessionId}/confirm`, 'POST', {})
  const guidance = await sendJson('/api/portfolios/p/contribution-guidance', 'POST', { amount: 1000 })
  await sendJson('/api/portfolios/p/contributions', 'POST', contributionExecution(guidance.guidanceId, { assetId: 'A' }, 2, 100))
  await sendJson('/api/portfolios/p/contributions', 'POST', contributionExecution(guidance.guidanceId, { assetId: 'B' }, 3, 90))
  assert.equal(calls[0].body, file)
  assert.equal(calls[0].headers, undefined, 'the browser must supply the multipart boundary')
  assert.equal(calls[1].url, '/api/portfolios/p/imports/import/confirm')
  assert.deepEqual(JSON.parse(calls[2].body), { amount: 1000 })
  const first = JSON.parse(calls[3].body), second = JSON.parse(calls[4].body)
  assert.equal(first.guidanceId, 'guidance')
  assert.deepEqual(first.items, [{ assetId: 'A', quantity: 2, unitPrice: 100 }])
  assert.deepEqual(second.items, [{ assetId: 'B', quantity: 3, unitPrice: 90 }])
})

test('successful updates accept 204 and optional guidance accepts 404', async t => {
  t.mock.method(globalThis, 'fetch', async () => new Response(null, { status: 204 }))
  assert.equal(await sendJson('/portfolio', 'PUT', {}), undefined)
  globalThis.fetch.mock.mockImplementation(async () => new Response(null, { status: 404 }))
  assert.equal(await getOptionalJson('/guidance/latest'), null)
})

test('server internals are hidden while validation and duplicate errors stay useful', async () => {
  assert.equal(await readError(new Response('Npgsql.PostgresException: database error', { status: 500 }), 'Tente novamente.'), 'Tente novamente.')
  assert.equal(await readError(new Response('System.Exception: database error', { status: 400 }), 'Tente novamente.'), 'Tente novamente.')
  assert.match(await readError(new Response('GuidanceAlreadyConfirmed', { status: 409 }), 'Erro'), /já foi confirmado/)
  assert.equal(await readError(Response.json({ detail: 'Quantidade deve ser positiva.' }, { status: 400 }), 'Erro'), 'Quantidade deve ser positiva.')
})
