import { useState } from 'react'
import { BrowserRouter, Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import './App.css'

const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5080'

type Portfolio = { id: string; name: string; totalValue: number; positionCount: number }
type Position = { id: string; ticker: string; quantity: number; currentValue: number | null; portfolioWeight: number }
type Dashboard = { id: string; name: string; totalValue: number; positionCount: number; positions: Position[] }
type Document = {
  id: string
  ticker: string
  assetName: string
  documentType: number
  publishedAt: string
  sourceName: string
  sourceUrl: string
  filePath: string | null
  processingStatus: number
  discoveredAt: string
  processedAt: string | null
}
type DocumentAnalysis = {
  id: string
  documentId: string
  model: string
  promptVersion: string
  importance: string
  classification: string
  requiresAction: boolean
  suggestedAction: string
  summary: string
  impact: string
  portfolioImpact: string
  affectedAssets: string[]
  confidence: number
  createdAt: string
}

const processingStatuses = ['Descoberto', 'Baixado', 'Aguardando processamento', 'Processando', 'Processado', 'Erro', 'Ignorado']
const documentTypes = ['Fato relevante', 'Comunicado', 'Relatorio gerencial', 'Outro']
const importanceLabels: Record<string, string> = {
  Critical: 'Critico',
  Relevant: 'Relevante',
  Informative: 'Informativo',
  NoAction: 'Sem acao',
}
const classificationLabels: Record<string, string> = {
  CorporateAction: 'Acao corporativa',
  FinancialResult: 'Resultado financeiro',
  Distribution: 'Distribuicao',
  CapitalRaise: 'Captacao',
  AssetTransaction: 'Transacao de ativos',
  Governance: 'Governanca',
  Operational: 'Operacional',
  Liquidation: 'Liquidacao',
  Other: 'Outro',
}
const actionLabels: Record<string, string> = {
  Maintain: 'Manter',
  IncreasePosition: 'Aumentar posicao',
  ReduceExposure: 'Reduzir exposicao',
  EvaluateSale: 'Avaliar venda',
  Monitor: 'Acompanhar',
  NoAction: 'Sem acao',
  OpenPosition: 'Abrir posicao',
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`)
  if (!response.ok) throw new Error('Nao foi possivel carregar os dados.')
  return response.json()
}

async function getOptionalJson<T>(path: string): Promise<T | null> {
  const response = await fetch(`${apiUrl}${path}`)
  if (response.status === 404) return null
  if (!response.ok) throw new Error('Nao foi possivel carregar os dados.')
  return response.json()
}

function formatCurrency(value: number | null | undefined) {
  if (value == null) return 'Sem valor'
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

function formatDate(value: string | null) {
  if (!value) return '-'
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function analysisTone(importance: string) {
  if (importance === 'Critical') return 'border-rose-300 bg-rose-50 text-rose-900'
  if (importance === 'Relevant') return 'border-amber-300 bg-amber-50 text-amber-900'
  if (importance === 'Informative') return 'border-sky-300 bg-sky-50 text-sky-900'
  return 'border-slate-300 bg-white text-slate-900'
}

function App() {
  const queryClient = useQueryClient()
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null)
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
  const selectedDocument = documents.data?.find((item) => item.id === selectedDocumentId) ?? documents.data?.[0] ?? null
  const analysis = useQuery({
    queryKey: ['document-analysis', selectedDocument?.id],
    queryFn: () => getOptionalJson<DocumentAnalysis>(`/api/documents/${selectedDocument!.id}/analysis`),
    enabled: Boolean(selectedDocument),
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
            <p className="mt-7 max-w-xl text-lg leading-8 text-slate-600">O Prumo acompanha eventos dos seus ativos e transforma informacao dispersa em contexto claro para suas decisoes.</p>
            <div className="mt-9 flex gap-4">
              <Link to="/dashboard" className="rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700">Abrir dashboard</Link>
              <span className="rounded-full border border-slate-300 px-5 py-3 text-sm text-slate-600">Sem execucao de ordens</span>
            </div>
          </div>

          <div className="relative overflow-hidden rounded-[2rem] bg-slate-900 p-8 text-white shadow-xl shadow-slate-300/50">
            <div className="absolute -right-12 -top-12 h-40 w-40 rounded-full bg-amber-500/20 blur-2xl" />
            <p className="relative text-sm text-slate-400">Status da carteira</p>
            <p className="relative mt-3 text-4xl font-semibold">{dashboard.data ? formatCurrency(dashboard.data.totalValue) : 'Sua carteira'}</p>
            <div className="relative mt-12 grid grid-cols-3 gap-3 border-t border-white/10 pt-5 text-sm">
              <div><strong className="block text-2xl text-rose-300">{dashboard.data?.positionCount ?? 0}</strong><span className="text-slate-400">ativos</span></div>
              <div><strong className="block text-2xl text-amber-300">{dashboard.data?.positions.length ?? 0}</strong><span className="text-slate-400">posicoes</span></div>
              <div><strong className="block text-2xl text-emerald-300">{dashboard.isLoading ? '...' : 'ok'}</strong><span className="text-slate-400">status</span></div>
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-6xl px-6 pb-20">
          {portfolios.isLoading && <p className="text-slate-500">Carregando carteira...</p>}
          {portfolios.isError && <p className="text-rose-700">API indisponivel. Inicie o backend em `http://localhost:5080`.</p>}
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
            <div className="mt-12 grid gap-8 lg:grid-cols-[0.95fr_1.05fr]">
              <div className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex items-baseline justify-between border-b border-slate-200 pb-3">
                  <h2 className="text-2xl font-semibold">Documentos recentes</h2>
                  <span className="text-sm text-slate-500">{documents.data?.length ?? 0} encontrados</span>
                </div>
                {documents.isLoading && <p className="py-5 text-sm text-slate-500">Carregando documentos...</p>}
                {!documents.isLoading && documents.data?.length === 0 && <p className="py-5 text-sm text-slate-500">Nenhum documento descoberto para esta carteira.</p>}
                <div className="mt-3 divide-y divide-slate-200">
                  {documents.data?.slice(0, 8).map((document) => {
                    const isSelected = document.id === selectedDocument?.id
                    return (
                      <button
                        key={document.id}
                        type="button"
                        onClick={() => setSelectedDocumentId(document.id)}
                        className={`flex w-full items-center justify-between gap-4 py-4 text-left text-sm transition ${isSelected ? 'text-slate-950' : 'text-slate-700 hover:text-slate-950'}`}
                      >
                        <div>
                          <strong>{document.ticker}</strong>
                          <span className="ml-3 text-slate-500">{document.sourceName}</span>
                          <p className="mt-1 text-xs text-slate-500">{formatDate(document.publishedAt)} - {documentTypes[document.documentType] ?? 'Documento'}</p>
                        </div>
                        <span className={`rounded-full px-3 py-1 text-xs ${isSelected ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-600'}`}>
                          {processingStatuses[document.processingStatus] ?? 'Desconhecido'}
                        </span>
                      </button>
                    )
                  })}
                </div>
              </div>

              <section className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
                {!selectedDocument && <p className="text-sm text-slate-500">Selecione um documento para ver os detalhes.</p>}
                {selectedDocument && (
                  <div className="space-y-6">
                    <div className="border-b border-slate-200 pb-4">
                      <p className="text-sm font-semibold uppercase tracking-[0.16em] text-slate-500">Detalhe do documento</p>
                      <h2 className="mt-3 text-3xl font-semibold">{selectedDocument.ticker}</h2>
                      <p className="mt-2 text-sm text-slate-500">{selectedDocument.assetName} - {selectedDocument.sourceName}</p>
                      <div className="mt-4 grid gap-2 text-sm text-slate-600 md:grid-cols-2">
                        <span>Publicado em {formatDate(selectedDocument.publishedAt)}</span>
                        <span>Status {processingStatuses[selectedDocument.processingStatus] ?? 'Desconhecido'}</span>
                        <span>Processado em {formatDate(selectedDocument.processedAt)}</span>
                        <a href={selectedDocument.sourceUrl} target="_blank" rel="noreferrer" className="text-amber-700 hover:text-amber-800">Abrir origem</a>
                      </div>
                    </div>

                    {analysis.isLoading && <p className="text-sm text-slate-500">Carregando analise...</p>}
                    {analysis.isError && <p className="text-sm text-rose-700">Nao foi possivel carregar a analise deste documento.</p>}
                    {!analysis.isLoading && !analysis.isError && !analysis.data && (
                      <div className="rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">
                        Nenhuma analise validada foi encontrada para este documento ainda.
                      </div>
                    )}
                    {analysis.data && (
                      <div className={`rounded-[1.5rem] border p-5 ${analysisTone(analysis.data.importance)}`}>
                        <div className="grid gap-4 md:grid-cols-2">
                          <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Importancia</p>
                            <p className="mt-2 text-2xl font-semibold">{analysis.data.importance === 'Critical' ? 'Critico' : importanceLabels[analysis.data.importance] ?? analysis.data.importance}</p>
                          </div>
                          <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Classificacao</p>
                            <p className="mt-2 text-lg font-semibold">{classificationLabels[analysis.data.classification] ?? analysis.data.classification}</p>
                          </div>
                        </div>

                        <div className="mt-6 grid gap-5">
                          <article>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Resumo</p>
                            <p className="mt-2 text-sm leading-7">{analysis.data.summary}</p>
                          </article>
                          <article>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Impacto</p>
                            <p className="mt-2 text-sm leading-7">{analysis.data.impact}</p>
                          </article>
                          <article>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Impacto na minha carteira</p>
                            <p className="mt-2 text-sm leading-7">{analysis.data.portfolioImpact}</p>
                          </article>
                        </div>

                        <div className="mt-6 grid gap-4 border-t border-current/10 pt-5 md:grid-cols-2">
                          <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Acao sugerida</p>
                            <p className="mt-2 text-lg font-semibold">{actionLabels[analysis.data.suggestedAction] ?? analysis.data.suggestedAction}</p>
                            <p className="mt-1 text-sm opacity-75">{analysis.data.requiresAction ? 'Requer atencao' : 'Nao requer acao imediata'}</p>
                          </div>
                          <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.16em]">Confianca</p>
                            <p className="mt-2 text-lg font-semibold">{Math.round(analysis.data.confidence * 100)}%</p>
                            <p className="mt-1 text-sm opacity-75">Modelo {analysis.data.model} - Prompt {analysis.data.promptVersion}</p>
                          </div>
                        </div>

                        <div className="mt-6">
                          <p className="text-xs font-semibold uppercase tracking-[0.16em]">Ativos afetados</p>
                          <div className="mt-3 flex flex-wrap gap-2">
                            {analysis.data.affectedAssets.length > 0
                              ? analysis.data.affectedAssets.map((asset) => <span key={asset} className="rounded-full border border-current/15 px-3 py-1 text-xs font-semibold">{asset}</span>)
                              : <span className="text-sm opacity-75">Nenhum ativo listado.</span>}
                          </div>
                          <p className="mt-4 text-xs opacity-75">Analise criada em {formatDate(analysis.data.createdAt)}</p>
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </section>
            </div>
          )}
        </section>
      </main>
    </BrowserRouter>
  )
}

export default App
