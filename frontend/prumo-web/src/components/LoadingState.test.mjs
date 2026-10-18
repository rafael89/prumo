import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const server = await createServer({ server: { middlewareMode: true, hmr: false } })
let LoadingState
try {
  ;({ LoadingState } = await server.ssrLoadModule('/src/components/LoadingState.tsx'))
} finally {
  await server.close()
}

test('loading announces its message and keeps the motion-safe indicator decorative', () => {
  const html = renderToStaticMarkup(createElement(LoadingState))
  assert.match(html, /role="status"/)
  assert.match(html, /aria-live="polite"/)
  assert.match(html, /aria-atomic="true"/)
  assert.match(html, /Só um instante…/)
  assert.match(html, /aria-hidden="true"[^>]*motion-safe:animate-spin/)
  assert.doesNotMatch(html, /<p class="mt-1/)
})

test('loading supports contextual messages, descriptions and placement', () => {
  const html = renderToStaticMarkup(createElement(LoadingState, {
    message: 'Preparando sua carteira…', description: 'Estamos reunindo suas posições.', className: 'mt-4',
  }))
  assert.match(html, /Preparando sua carteira…/)
  assert.match(html, /Estamos reunindo suas posições\./)
  assert.match(html, /mt-4/)
})
