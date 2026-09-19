import { test } from 'node:test'
import assert from 'node:assert/strict'
import { describeContributionAnalysis } from './contributionAnalysis.ts'

const guidance = { intelligenceAvailability: 'IntelligenceAvailable', allocations: [{}], allocatedAmount: 100, requestedAmount: 100 }

test('a secondary AI model is distinguished from deterministic fallback', () => {
  const status = describeContributionAnalysis({ ...guidance, analysis: { provider: 'Gemini', usedFallback: true } })
  assert.equal(status.isAiAssisted, true)
  assert.match(status.contextMessage, /modelo alternativo de IA/)
})

test('configured rules explain that AI was not used', () => {
  const status = describeContributionAnalysis({ ...guidance, intelligenceAvailability: 'InsufficientIntelligence', analysis: {
    provider: 'deterministic-fallback', executionPath: 'deterministic-configured', usedFallback: true,
    fallbackReason: 'A análise de aportes por IA está desativada na configuração.'
  } })
  assert.equal(status.isAiAssisted, false)
  assert.match(status.contextMessage, /desativada/)
})

test('review failure preserves the valid AI proposal and explains the limitation', () => {
  const status = describeContributionAnalysis({ ...guidance, analysis: { provider: 'Gemini', executionPath: 'review-fallback' } })
  assert.equal(status.isAiAssisted, true)
  assert.equal(status.isFallback, true)
  assert.match(status.contextMessage, /proposta inicial foi preservada/)
})

test('a single rule-based scenario never claims AI influence', () => {
  const status = describeContributionAnalysis({ ...guidance, analysis: { selectionMode: 'single-safe-scenario' } })
  assert.equal(status.isAiAssisted, false)
  assert.match(status.contextMessage, /motor de regras/)
})
