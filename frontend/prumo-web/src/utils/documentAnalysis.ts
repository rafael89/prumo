export function analysisTone(importance: string) {
  if (importance === 'Critical') return 'level-critical'
  if (importance === 'Relevant') return 'level-medium'
  if (importance === 'Informative') return 'level-informative'
  return 'level-low'
}
