import { useQuery } from '@tanstack/react-query'
import { getJson } from '../api/client'

type Signal = { id: string; type: string; priority: number; title: string; description: string; ticker: string | null; quality: 'Safe' | 'Qualified' | 'Unsafe'; evidence: Array<{ type: string; id: string; label: string }> }
type Intelligence = { generatedAt: string; summary: string; attentionItems: Signal[]; priceMovers: Signal[]; allocationInsights: Signal[]; decisionReviews: Signal[]; assetInsights: Signal[]; coverageWarnings: Signal[] }

const qualityLabel = (quality: Signal['quality']) => quality === 'Safe' ? 'Fato conhecido' : quality === 'Qualified' ? 'Com ressalva' : 'Indisponível'
function SignalList({ title, items }: { title: string; items: Signal[] }) {
  return <section className="rounded-3xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-200 px-5 py-4"><h2 className="font-semibold">{title}</h2></div>{items.length === 0 ? <p className="px-5 py-5 text-sm text-slate-500">Nenhum sinal relevante.</p> : <div className="divide-y divide-slate-200">{items.map(item => <article key={item.id} className="px-5 py-4"><div className="flex flex-wrap items-center gap-2"><strong>{item.ticker ? `${item.ticker} — ` : ''}{item.title}</strong><span className="rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-600">{qualityLabel(item.quality)}</span></div><p className="mt-1 text-sm leading-6 text-slate-600">{item.description}</p>{item.evidence.length > 0 && <p className="mt-2 text-xs text-slate-400">Fonte: {item.evidence.map(e => e.label).join(' · ')}</p>}</article>)}</div>}</section>
}

export function IntelligenceSection({ portfolioId }: { portfolioId: string }) {
  const query = useQuery({ queryKey: ['intelligence', portfolioId], queryFn: () => getJson<Intelligence>(`/api/portfolios/${portfolioId}/intelligence`) })
  if (query.isLoading) return <section className="mx-auto max-w-6xl px-4 pb-24 pt-8 sm:px-6"><p className="text-slate-500">Analisando os sinais atuais...</p></section>
  if (query.isError || !query.data) return <section className="mx-auto max-w-6xl px-4 pb-24 pt-8 sm:px-6"><p className="text-rose-700">Não foi possível carregar a inteligência da carteira.</p></section>
  const data = query.data
  return <section className="mx-auto max-w-6xl space-y-4 px-4 pb-24 pt-8 sm:px-6"><div><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Inteligência</p><h1 className="mt-2 text-3xl font-semibold tracking-tight">O que merece atenção agora</h1><p className="mt-2 text-sm leading-6 text-slate-600">{data.summary} Os sinais são derivados dos dados conhecidos; variações de preço não representam retorno do investidor.</p></div><SignalList title="Atenção agora" items={data.attentionItems} /><SignalList title="Alocação" items={data.allocationInsights} /><SignalList title="Movimentos de preço" items={data.priceMovers} /><SignalList title="Mudanças e decisões" items={[...data.decisionReviews, ...data.assetInsights.filter(x => x.type === 'AssessmentDeteriorated' || x.type === 'RecentRelevantEvent')]} /><SignalList title="Limitações de cobertura" items={data.coverageWarnings} /></section>
}
