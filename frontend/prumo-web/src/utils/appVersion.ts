export function stableVersion(tag: string): string | null {
  return /^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(tag) ? tag.replace(/^v/, '') : null
}

export function isNewerVersion(candidate: string, installed: string): boolean {
  const next = stableVersion(candidate)?.split('.').map(BigInt)
  const current = stableVersion(installed)?.split('.').map(BigInt)
  if (!next || !current) return false
  for (let index = 0; index < 3; index++) {
    if (next[index] !== current[index]) return next[index] > current[index]
  }
  return false
}

export async function fetchLatestStableTag(signal?: AbortSignal, request: typeof fetch = fetch): Promise<string | null> {
  let latest: string | null = null
  let page = 1
  // GitHub orders tags by commit date, so inspect every page and compare numerically.
  while (true) {
    const response = await request(`https://api.github.com/repos/rafael89/prumo/tags?per_page=100&page=${page}`, {
      headers: { Accept: 'application/vnd.github+json' },
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(10000)]) : AbortSignal.timeout(10000),
    })
    if (!response.ok) throw new Error('Não foi possível verificar atualizações.')
    const tags: unknown = await response.json()
    if (!Array.isArray(tags)) throw new Error('Resposta de tags inválida.')
    for (const tag of tags) {
      if (typeof tag?.name !== 'string' || !stableVersion(tag.name)) continue
      if (!latest || isNewerVersion(tag.name, latest)) latest = tag.name
    }
    if (!response.headers.get('link')?.includes('rel="next"')) return latest
    page++
  }
}
