type Analysis = {
  usedFallback?: boolean
  executionPath?: string
  provider?: string
  fallbackReason?: string | null
  selectionMode?: string | null
  selectedScenarioKey?: string | null
}

type Guidance = {
  intelligenceAvailability?: string
  analysis?: Analysis
  allocations: unknown[]
  classAllocations?: unknown[]
  allocatedAmount: number
  requestedAmount: number
}

export function describeContributionAnalysis(guidance: Guidance | null) {
  const trace = guidance?.analysis
  const deterministic = trace?.provider?.startsWith('deterministic') === true
    || guidance?.intelligenceAvailability === 'InsufficientIntelligence'
  const isAiAssisted = guidance?.intelligenceAvailability === 'IntelligenceAvailable' && !deterministic
    && trace?.selectionMode !== 'single-safe-scenario'
  const isFallback = deterministic || trace?.usedFallback === true || trace?.executionPath === 'review-fallback'
  let contextMessage = 'A recomendação foi calculada pelo motor determinístico do Prumo.'
  if (deterministic) {
    contextMessage = trace?.fallbackReason ?? 'A análise por IA não foi concluída; o motor de regras calculou a orientação.'
  } else if (trace?.executionPath === 'review-fallback') {
    contextMessage = 'A proposta da IA passou pelas regras do Prumo. A revisão adicional ficou indisponível e a proposta inicial foi preservada.'
  } else if (trace?.usedFallback && isAiAssisted) {
    contextMessage = 'Foi usado um modelo alternativo de IA para calcular ou revisar a distribuição, dentro dos limites validados pelo Prumo.'
  } else if (trace?.selectionMode === 'single-safe-scenario') {
    contextMessage = 'Havia apenas um cenário seguro pré-validado; ele foi aplicado diretamente pelo motor de regras.'
  } else if (isAiAssisted && guidance) {
    contextMessage = guidance.allocations.length === 0 && !guidance.classAllocations?.length
      ? 'A IA analisou os candidatos e concluiu que nenhum aporte em ativo atende aos limites atuais com segurança.'
      : guidance.allocatedAmount > 0 && guidance.allocatedAmount < guidance.requestedAmount
        ? 'A IA analisou a distribuição parcial; o valor restante permaneceu sem alocação porque não havia outra alternativa elegível.'
        : 'A IA construiu esta distribuição dentro do universo elegível e dos limites validados pelo Prumo.'
  }
  return { isFallback, isAiAssisted, contextMessage }
}
