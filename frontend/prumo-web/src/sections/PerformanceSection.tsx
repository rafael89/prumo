import { useQuery } from '@tanstack/react-query'
import { getJson } from '../api/client'

type Metric = { value: number | null; coverage: 'Complete' | 'Partial' | 'Unavailable'; reasons: string[] }
type Performance = {
  currentValuation: Metric; knownNetContributions: Metric; knownIncome: Metric
  currentCostBasis: Metric; unrealizedPnl: Metric; realizedPnl: Metric
  assets: Array<{ ticker: string; currentValuation: Metric; currentCostBasis: Metric; unrealizedPnl: Metric; priceReturn: Metric }>
}
const money = (value: number | null) => value === null ? 'Indisponível' : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
const coverageLabel = (value: Metric) => value.coverage === 'Complete' ? 'Completo' : value.coverage === 'Partial' ? 'Parcial' : 'Indisponível'

function MetricCard({ label, metric, percent = false }: { label: string; metric: Metric; percent?: boolean }) {
  return <div className="rounded-2xl border border-slate-200 bg-white p-4"><p className="text-xs text-slate-500">{label}</p><strong className="mt-1 block text-xl">{metric.value === null ? 'Indisponível' : percent ? `${metric.value.toFixed(2)}%` : money(metric.value)}</strong><span className={`mt-2 inline-block rounded-full px-2 py-1 text-xs font-semibold ${metric.coverage === 'Complete' ? 'bg-emerald-50 text-emerald-700' : metric.coverage === 'Partial' ? 'bg-amber-50 text-amber-700' : 'bg-slate-100 text-slate-600'}`}>{coverageLabel(metric)}</span></div>
}

export function PerformanceSection({ portfolioId }: { portfolioId: string }) {
  const performance = useQuery({ queryKey: ['performance', portfolioId], queryFn: () => getJson<Performance>(`/api/portfolios/${portfolioId}/performance`) })
  if (performance.isLoading) return <section className="mx-auto max-w-6xl px-4 pb-24 pt-8 sm:px-6"><p className="text-slate-500">Calculando métricas conhecidas...</p></section>
  if (performance.isError || !performance.data) return <section className="mx-auto max-w-6xl px-4 pb-24 pt-8 sm:px-6"><p className="text-rose-700">Não foi possível carregar as métricas.</p></section>
  const data = performance.data
  return <section className="mx-auto max-w-6xl px-4 pb-24 pt-8 sm:px-6"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Performance</p><h1 className="mt-2 text-3xl font-semibold tracking-tight">Métricas conhecidas</h1><p className="mt-2 text-sm leading-6 text-slate-600">Valores derivados do livro financeiro e dos preços persistidos. Parcial significa que parte do histórico é desconhecida.</p><div className="mt-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-3"><MetricCard label="Valor de mercado atual" metric={data.currentValuation} /><MetricCard label="Custo atual conhecido" metric={data.currentCostBasis} /><MetricCard label="P&L não realizado" metric={data.unrealizedPnl} /><MetricCard label="P&L realizado conhecido" metric={data.realizedPnl} /><MetricCard label="Aportes líquidos registrados" metric={data.knownNetContributions} /><MetricCard label="Rendimentos registrados" metric={data.knownIncome} /></div><div className="mt-8 overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-sm"><div className="border-b border-slate-200 px-5 py-4"><h2 className="font-semibold">Por ativo</h2></div><div className="divide-y divide-slate-200">{data.assets.map(asset => <div key={asset.ticker} className="grid gap-3 px-5 py-4 sm:grid-cols-4 sm:items-center"><strong>{asset.ticker}</strong><MetricCard label="Valor" metric={asset.currentValuation} /><MetricCard label="P&L não realizado" metric={asset.unrealizedPnl} /><MetricCard label="Variação de preço" metric={asset.priceReturn} percent /></div>)}</div></div></section>
}
