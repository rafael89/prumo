export const importanceLabels: Record<string, string> = {
  Critical: 'Crítico',
  Relevant: 'Relevante',
  Informative: 'Informativo',
  NoAction: 'Sem ação',
}

export function analysisTone(importance: string) {
  if (importance === 'Critical') return 'level-critical'
  if (importance === 'Relevant') return 'level-medium'
  if (importance === 'Informative') return 'level-informative'
  return 'level-low'
}
