import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'

const server = await createServer({ server: { middlewareMode: true, hmr: false } })
let HistorySection
try {
  ;({ HistorySection } = await server.ssrLoadModule('/src/sections/HistorySection.tsx'))
} finally {
  await server.close()
}

test('portfolio and asset history show loading instead of an empty timeline during the initial fetch', () => {
  for (const assetId of [null, 'asset-1']) {
    const client = new QueryClient()
    try {
      const html = renderToStaticMarkup(createElement(QueryClientProvider, { client },
        createElement(MemoryRouter, null,
          createElement(HistorySection, { portfolioId: 'portfolio-1', positions: [], assetId }))))
      assert.match(html, /role="status"/)
      assert.match(html, /Carregando seu histórico…/)
      assert.doesNotMatch(html, /Nenhum registro histórico encontrado/)
    } finally {
      client.clear()
    }
  }
})
