import { BrowserRouter, Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import './App.css'

const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5080'

type Portfolio = { id: string; name: string; totalValue: number; positionCount: number }
type Position = { id: string; ticker: string; quantity: number; currentValue: number | null; portfolioWeight: number }
type Dashboard = { id: string; name: string; totalValue: number; positionCount: number; positions: Position[] }
type Document = { id: string; ticker: string; documentType: number; publishedAt: string; sourceName: string; processingStatus: number }

const processingStatuses = ['Descoberto', 'Baixado', 'Aguardando processamento', 'Processando', 'Processado', 'Erro', 'Ignorado']

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`)
  if (!response.ok) throw new Error('Não foi possível carregar os dados.')
  return response.json()
}

function App() {
  const queryClient = useQueryClient()
  const portfolios = useQuery({ queryKey: ['portfolios'], queryFn: () => getJson<Portfolio[]>('/api/portfolios') })
  const portfolio = portfolios.data?.[0]
  const dashboard = useQuery({
    queryKey: ['dashboard', portfolio?.id],
    queryFn: () => getJson<Dashboard>(`/api/portfolios/${portfolio!.id}/dashboard`),
    enabled: Boolean(portfolio),
  })
  const documents = useQuery({
    queryKey: ['documents', portfolio?.id],
    queryFn: () => getJson<Document[]>(`/api/portfolios/${portfolio!.id}/documents`),
    enabled: Boolean(portfolio),
  })
  const createPortfolio = useMutation({
    mutationFn: () => fetch(`${apiUrl}/api/portfolios`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'Minha carteira' }) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['portfolios'] }),
  })

  return (
    <BrowserRouter>
      <main className="min-h-screen bg-stone-50 text-slate-900">
        <nav className="mx-auto flex max-w-6xl items-center justify-between px-6 py-6">
          <Link to="/" className="text-xl font-semibold tracking-tight">prumo<span className="text-amber-600">.</span></Link>
          <span className="text-sm text-slate-500">Carteira inteligente</span>
        </nav>
        <section className="mx-auto grid max-w-6xl gap-12 px-6 pb-20 pt-16 md:grid-cols-[1.15fr_.85fr] md:items-center md:pt-28">
          <div>
            <p className="mb-5 text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Acompanhamento inteligente</p>
            <h1 className="max-w-2xl text-5xl font-semibold leading-[1.05] tracking-tight md:text-7xl">Veja o que realmente importa na sua carteira.</h1>
            <p className="mt-7 max-w-xl text-lg leading-8 text-slate-600">O Prumo acompanha eventos dos seus ativos e transforma informação dispersa em contexto claro para suas decisões.</p>
            <div className="mt-9 flex gap-4">
              <Link to="/dashboard" className="rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700">Abrir dashboard</Link>
              <span className="rounded-full border border-slate-300 px-5 py-3 text-sm text-slate-600">Sem execução de ordens</span>
            </div>
          </div>
          <div className="relative overflow-hidden rounded-[2rem] bg-slate-900 p-8 text-white shadow-xl shadow-slate-300/50">
            <div className="absolute -right-12 -top-12 h-40 w-40 rounded-full bg-amber-500/20 blur-2xl" />
            <p className="relative text-sm text-slate-400">Status da carteira</p>
            <p className="relative mt-3 text-4xl font-semibold">{dashboard.data ? `R$ ${dashboard.data.totalValue.toFixed(2)}` : 'Sua carteira'}</p>
            <div className="relative mt-12 grid grid-cols-3 gap-3 border-t border-white/10 pt-5 text-sm">
              <div><strong className="block text-2xl text-rose-300">{dashboard.data?.positionCount ?? 0}</strong><span className="text-slate-400">ativos</span></div>
              <div><strong className="block text-2xl text-amber-300">{dashboard.data?.positions.length ?? 0}</strong><span className="text-slate-400">posições</span></div>
              <div><strong className="block text-2xl text-emerald-300">{dashboard.isLoading ? '...' : 'ok'}</strong><span className="text-slate-400">status</span></div>
            </div>
          </div>
        </section>
        <section className="mx-auto max-w-6xl px-6 pb-20">
          {portfolios.isLoading && <p className="text-slate-500">Carregando carteira...</p>}
          {portfolios.isError && <p className="text-rose-700">API indisponível. Inicie o backend em `http://localhost:5080`.</p>}
          {!portfolios.isLoading && !portfolios.isError && !portfolio && (
            <button type="button" onClick={() => createPortfolio.mutate()} disabled={createPortfolio.isPending} className="rounded-full border border-slate-300 px-5 py-3 text-sm font-semibold text-slate-700 hover:border-amber-600 hover:text-amber-700">
              {createPortfolio.isPending ? 'Criando...' : 'Criar minha carteira'}
            </button>
          )}
          {dashboard.data && dashboard.data.positions.length > 0 && (
            <div className="mt-4 grid gap-3 md:grid-cols-3">
              {dashboard.data.positions.map((position) => <div key={position.id} className="border-t border-slate-300 pt-3"><strong>{position.ticker}</strong><span className="ml-3 text-sm text-slate-500">{position.portfolioWeight.toFixed(1)}%</span></div>)}
            </div>
          )}
          {portfolio && (
            <div className="mt-12 max-w-3xl">
              <div className="flex items-baseline justify-between border-b border-slate-300 pb-3">
                <h2 className="text-2xl font-semibold">Documentos recentes</h2>
                <span className="text-sm text-slate-500">{documents.data?.length ?? 0} encontrados</span>
              </div>
              {documents.isLoading && <p className="py-5 text-sm text-slate-500">Carregando documentos...</p>}
              {!documents.isLoading && documents.data?.length === 0 && <p className="py-5 text-sm text-slate-500">Nenhum documento descoberto para esta carteira.</p>}
              <div className="divide-y divide-slate-200">
                {documents.data?.slice(0, 8).map((document) => (
                  <div key={document.id} className="flex items-center justify-between gap-4 py-4 text-sm">
                    <div><strong>{document.ticker}</strong><span className="ml-3 text-slate-600">{document.sourceName}</span></div>
                    <span className="text-slate-500">{processingStatuses[document.processingStatus] ?? 'Desconhecido'}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </section>
      </main>
    </BrowserRouter>
  )
}

export default App
