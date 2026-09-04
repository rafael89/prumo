import { useQuery } from '@tanstack/react-query'
import { getJson } from '../api/client'

type Completeness = { overallStatus: string; reliableFrom: string | null; dimensions: Array<{ name: string; status: string; reliableFrom: string | null; reasons: string[] }>; continuity: Array<{ dimension: string; status: string; expected: number | null; actual: number | null; delta: number | null; reasons: string[] }>; blockingGaps: string[]; metricReadiness: Array<{ name: string; status: string; scope: string }> }

export function HistoricalCompletenessSection({ portfolioId }: { portfolioId: string }) {
  const query = useQuery({ queryKey: ['historical-completeness', portfolioId], queryFn: () => getJson<Completeness>(`/api/portfolios/${portfolioId}/historical-completeness`) })
  if (query.isLoading) return <section className="mx-auto mt-6 max-w-6xl px-6"><p className="text-sm text-slate-500">Calculando completude histórica...</p></section>
  if (query.isError || !query.data) return null
  const data = query.data
  return <section className="mx-auto mt-6 max-w-6xl rounded-3xl border border-slate-200 bg-white p-6 shadow-sm"><div className="flex flex-wrap items-end justify-between gap-3"><div><p className="text-xs font-bold uppercase tracking-[0.16em] text-amber-700">Completude histórica</p><h2 className="mt-2 text-xl font-semibold">Confiabilidade da história financeira</h2></div><p className="text-sm text-slate-600">{data.overallStatus} · confiável desde {data.reliableFrom ?? 'desconhecido'}</p></div><div className="mt-5 grid gap-2 sm:grid-cols-3 lg:grid-cols-5">{data.dimensions.map(item => <div key={item.name} className="rounded-xl bg-stone-50 px-3 py-2 text-sm"><span className="font-semibold">{item.name}</span><br />{item.status}</div>)}</div><details className="mt-5"><summary className="cursor-pointer text-sm font-semibold">Diagnósticos e métricas</summary><div className="mt-3 space-y-2 text-sm">{data.continuity.filter(item => item.status !== 'Match').map((item, index) => <p key={`${item.dimension}-${index}`} className="text-amber-800">{item.dimension}: {item.status} · delta {item.delta ?? 'desconhecido'}</p>)}{data.metricReadiness.map(item => <p key={item.name}><span className="font-semibold">{item.name}</span>: {item.status} ({item.scope})</p>)}</div></details></section>
}
