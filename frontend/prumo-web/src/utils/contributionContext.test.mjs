import { test } from 'node:test'
import assert from 'node:assert/strict'
import { contributionContext, translateContributionContext } from './contributionContext.ts'

test('saved explanations translate sector and internal terms without changing tickers or numbers', () => {
  const text = translateContributionContext('TAEE11 (Utilities), ITSA4 (Financials), Real Estate 71% e Energy. classDeviationGain de 2,26 no perfil build, abaixo do target.')
  assert.equal(text, 'TAEE11 (serviços públicos), ITSA4 (serviços financeiros), imóveis 71% e energia. redução do desvio em relação às metas de 2,26 no perfil formação de posição, abaixo da meta.')
})

test('a long explanation stays available in full with a short preview', () => {
  const long = 'A carteira reduz a concentração em imóveis e energia com os ativos selecionados. ' + 'O cenário considera os limites da carteira e a disponibilidade de dados para comparar as alternativas. '.repeat(5)
  const context = contributionContext([long, 'Há dados financeiros divergentes que exigem consulta.'])
  assert.ok(context.summary.length <= 301)
  assert.equal(context.paragraphs[0], long.trim())
  assert.equal(context.paragraphs.length, 2)
  assert.equal(context.hasDetails, true)
})

test('decimal values and cautions remain intact in the expanded explanation', () => {
  const context = contributionContext(['O desvio caiu de 15.36 para 13.09 p.p. Há dados conflitantes. A decisão exige revisão.'])
  assert.match(context.summary, /15\.36 para 13\.09/)
  assert.match(context.paragraphs[0], /A decisão exige revisão/)
  assert.equal(context.hasDetails, true)
})

test('short and empty explanations need no expanded section', () => {
  assert.equal(contributionContext(['Aporte reduz o desvio da carteira.']).hasDetails, false)
  assert.equal(contributionContext([]).summary, '')
})
