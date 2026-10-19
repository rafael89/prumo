const terms: Record<string, string> = {
  classDeviationGain: 'redução do desvio em relação às metas',
  'Real Estate': 'imóveis',
  Utilities: 'serviços públicos',
  Financials: 'serviços financeiros',
  Energy: 'energia',
  Industrials: 'indústria',
  'Consumer Staples': 'consumo essencial',
  'Consumer Discretionary': 'consumo não essencial',
  'Information Technology': 'tecnologia da informação',
  'Health Care': 'saúde',
  Materials: 'materiais básicos',
  'Communication Services': 'comunicações',
  portfolio: 'carteira',
  valuation: 'avaliação de preço',
  target: 'meta',
  build: 'formação de posição',
  underweight: 'peso abaixo da meta',
  overweight: 'peso acima da meta',
  assessments: 'avaliações de impacto',
}

export function translateContributionContext(text: string): string {
  return Object.entries(terms).reduce((value, [term, label]) =>
    value.replace(new RegExp(`\\b${term}\\b`, 'gi'), label), text
      .replace(/\bdo target\b/gi, 'da meta')
      .replace(/\bao target\b/gi, 'à meta')
      .replace(/\bum target\b/gi, 'uma meta')).trim()
}

export function contributionContext(reasons: readonly string[]) {
  const paragraphs = [...new Set(reasons.map(translateContributionContext).filter(Boolean))]
  const first = paragraphs[0] ?? ''
  // Keep a readable excerpt for saved analyses too; the full explanation stays available.
  const sentences = first.split(/(?<=[.!?])\s+(?=[A-ZÀ-Ú])/u)
  const excerpt = sentences.slice(0, 2).join(' ').trim()
  const summary = excerpt.length > 300
    ? `${excerpt.slice(0, 300).replace(/\s+\S*$/, '').trimEnd()}…`
    : excerpt
  return { summary, paragraphs, hasDetails: paragraphs.length > 1 || summary !== first }
}
