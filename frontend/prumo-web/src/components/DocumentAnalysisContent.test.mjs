import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const server = await createServer({ server: { middlewareMode: true } })
let DocumentAnalysisContent
try {
  ;({ DocumentAnalysisContent } = await server.ssrLoadModule('/src/components/DocumentAnalysisContent.tsx'))
} finally {
  await server.close()
}
const analysis = {
  importance: 'Informative', classification: 'Distribution', summary: 'Resumo do documento',
  impact: 'Impacto do documento', suggestedAction: 'Monitor', requiresAction: false,
  portfolioAssessment: null, confidence: 0.91, model: 'modelo', promptVersion: 'v1',
  affectedAssets: ['TEST11'],
}
const render = overrides => renderToStaticMarkup(createElement(DocumentAnalysisContent, {
  analysis: { ...analysis, ...overrides },
}))

test('document color follows importance even with a critical Radar priority', () => {
  for (const [importance, tone] of [
    ['Critical', 'critical'], ['Relevant', 'medium'], ['Informative', 'informative'], ['NoAction', 'low'],
  ]) {
    assert.match(render({ importance, portfolioAssessment: { priority: 'Critical', requiresAttention: true } }),
      new RegExp(`level-${tone}`))
  }
})

test('news content keeps document details and hides Radar details', () => {
  const html = render()
  for (const content of ['Informativo', 'Distribuição', 'Resumo do documento', 'TEST11']) {
    assert.ok(html.includes(content), content)
  }
  for (const content of ['Impacto', 'Ação sugerida', 'Acompanhar', 'Não requer ação imediata',
    'Confiança', '91%', 'Modelo modelo - Prompt v1']) {
    assert.ok(!html.includes(content), content)
  }
  assert.match(render({ importance: 'NoAction', affectedAssets: [] }), /Sem ação/)
  assert.match(render({ affectedAssets: [] }), /Nenhum ativo listado/)
})

test('Radar content keeps impact, suggested action and confidence even without attention required', () => {
  const html = render({ portfolioAssessment: { requiresAttention: false } })
  for (const content of ['Impacto do documento', 'Ação sugerida', 'Acompanhar',
    'Não requer ação imediata', 'Confiança', '91%', 'Modelo modelo - Prompt v1']) {
    assert.ok(html.includes(content), content)
  }
  assert.match(render({ requiresAction: true, portfolioAssessment: { requiresAttention: false } }), /Não requer ação imediata/)
  assert.match(render({ portfolioAssessment: { requiresAttention: true } }), /Requer atenção/)
  assert.doesNotMatch(render({ requiresAction: true }), /Requer atenção/)
})

test('news hides Radar details even when the document has an assessment', () => {
  const html = renderToStaticMarkup(createElement(DocumentAnalysisContent, {
    analysis: { ...analysis, portfolioAssessment: { requiresAttention: true } }, showRadarDetails: false,
  }))
  assert.doesNotMatch(html, /Impacto do documento|Ação sugerida|Confiança/)
  assert.match(html, /Resumo do documento/)
})

test('modal content has no inner frame and keeps the document content', () => {
  const html = renderToStaticMarkup(createElement(DocumentAnalysisContent, { analysis, framed: false }))
  assert.doesNotMatch(html, /level-card|rounded-3xl|border-slate-300/)
  assert.equal(html.replace(/<[^>]*>/g, ''), render().replace(/<[^>]*>/g, ''))
})
