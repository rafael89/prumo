export const apiBaseUrl = import.meta.env?.VITE_API_URL?.replace(/\/$/, '') ?? ''

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`)
  if (!response.ok) throw new Error('Não foi possível carregar os dados.')
  return response.json() as Promise<T>
}

export async function getOptionalJson<T>(path: string): Promise<T | null> {
  const response = await fetch(`${apiBaseUrl}${path}`)
  if (response.status === 404) return null
  if (!response.ok) throw new Error('Não foi possível carregar os dados.')
  return response.json() as Promise<T>
}

export async function sendJson<TResponse, TRequest>(path: string, method: 'POST' | 'PUT' | 'PATCH', body: TRequest): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  if (!response.ok) throw new Error(await readError(response, 'Não foi possível concluir a operação.'))
  return response.status === 204 ? undefined as TResponse : response.json() as Promise<TResponse>
}

export async function sendVoid(path: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}${path}`, { method: 'DELETE' })
  if (!response.ok) throw new Error(await readError(response, 'Não foi possível concluir a operação.'))
}

export async function sendForm<TResponse>(path: string, form: FormData): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, { method: 'POST', body: form })
  if (!response.ok) throw new Error(await readError(response, 'Não foi possível concluir a operação.'))
  return response.json() as Promise<TResponse>
}

export async function readError(response: Response, fallback: string): Promise<string> {
  const text = await response.text()
  if (!text || response.status >= 500) return fallback
  if (text.includes('GuidanceAlreadyConfirmed')) return 'Este aporte já foi confirmado. Consulte o histórico.'
  if (/exception|stack trace| at |npgsql|microsoft\.|system\.|relation ".*" does not exist|select .* from/i.test(text)) return fallback
  try {
    const problem = JSON.parse(text) as { detail?: unknown; title?: unknown }
    if (typeof problem.detail === 'string' && problem.detail.length < 300) return problem.detail
    if (typeof problem.title === 'string' && problem.title.length < 120) return problem.title
  } catch {
    // Validation errors may be returned as plain text.
  }
  return text.length > 500 ? fallback : text
}
