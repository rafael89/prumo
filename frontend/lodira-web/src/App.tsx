import { useState, type FormEvent } from 'react'
import { BrowserRouter, Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import './App.css'

const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5041'

type AssetType = 0 | 1 | 2 | 3
type Feedback = { tone: 'success' | 'error'; message: string } | null

type PortfolioSummary = { id: string; name: string; totalValue: number; positionCount: number }
type Position = {
  id: string; assetId: string; assetClass: number
  ticker: string
  name: string
  assetType: AssetType
  sector: string | null
  quantity: number
  averagePrice: number
  currentPrice: number | null
  currentValue: number | null
  portfolioWeight: number
}
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
type RadarItem = {
  documentId: string; documentAnalysisId: string; assetTicker: string; documentType: number; publishedAt: string | null
    priority: 'Low' | 'Medium' | 'High' | 'Critical'; score: number; reasons: string[]; status: 'Unread' | 'Read' | 'Resolved'
  requiresAttention: boolean; importance: string; classification: string; requiresAction: boolean; suggestedAction: string
  summary: string; portfolioImpact: string; confidence: number; affectedAssets: string[]; createdAt: string
}
type RadarResponse = { items: RadarItem[]; unread: number; requiresAttention: number; resolved: number }
type Strategy = { id: string; allocations: Array<{ assetClass: number; targetPercentage: number }> }
type Guidance = { requestedAmount: number; allocatedAmount: number; unallocatedAmount: number; items: Array<{ assetId: string; ticker: string; currentValue: number; currentWeight: number; targetWeight: number; suggestedContribution: number; projectedWeight: number; eligibility: string; reasons: string[] }> }

type CreatePortfolioRequest = { name: string }
type CreatePositionRequest = {
  ticker: string
  name: string
  assetType: AssetType
  sector: string | null
  quantity: number
  averagePrice: number
  currentPrice: number | null
}
type UpdatePositionRequest = {
  quantity: number
  averagePrice: number
  currentPrice: number | null
}

const processingStatuses = ['Descoberto', 'Baixado', 'Aguardando processamento', 'Processando', 'Processado', 'Erro', 'Ignorado']
const documentTypes = ['Fato relevante', 'Comunicado', 'Relatorio gerencial', 'Outro']
const assetTypeOptions: Array<{ value: AssetType; label: string }> = [
  { value: 0, label: 'Acao' },
  { value: 1, label: 'FII' },
  { value: 2, label: 'ETF' },
  { value: 3, label: 'Outro' },
]
const assetTypeLabels: Record<number, string> = {
  0: 'Acao',
  1: 'FII',
  2: 'ETF',
  3: 'Outro',
}
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

async function sendJson<TResponse, TRequest>(path: string, method: 'POST' | 'PUT' | 'PATCH', body: TRequest): Promise<TResponse> {
  const response = await fetch(`${apiUrl}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    throw new Error(await readError(response, path.includes('contribution-guidance') ? 'Nao foi possivel simular o aporte. Tente novamente.' : 'Nao foi possivel concluir a operacao.'))
  }

  return response.json()
}

async function sendVoid(path: string, method: 'DELETE'): Promise<void> {
  const response = await fetch(`${apiUrl}${path}`, { method })
  if (!response.ok) {
    throw new Error(await readError(response, 'Nao foi possivel concluir a operacao.'))
  }
}

async function readError(response: Response, fallback: string) {
  const text = await response.text()
  if (!text || response.status >= 500) return fallback
  try {
    const problem = JSON.parse(text) as { detail?: unknown; title?: unknown }
    if (typeof problem.detail === 'string' && problem.detail.length < 300) return problem.detail
    if (typeof problem.title === 'string' && problem.title.length < 120) return problem.title
  } catch {
    // Domain validation errors may still be returned as plain text.
  }
  if (/exception|stack trace| at |npgsql|microsoft\.|system\.|relation ".*" does not exist|select .* from/i.test(text) || text.length > 500) return fallback
  return text
}

function formatCurrency(value: number | null | undefined) {
  if (value == null) return 'Sem valor'
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

function formatNumber(value: number | null | undefined) {
  if (value == null) return '-'
  return new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 }).format(value)
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

const radarPriorityLabels: Record<string, string> = { Critical: 'CRÍTICA', High: 'ALTA', Medium: 'MÉDIA', Low: 'BAIXA' }
const radarPriorityTone: Record<string, string> = { Critical: 'border-rose-400 bg-rose-50 text-rose-950', High: 'border-orange-300 bg-orange-50 text-orange-950', Medium: 'border-amber-300 bg-amber-50 text-amber-950', Low: 'border-slate-300 bg-white text-slate-800' }
const radarReasonLabels: Record<string, string> = { HighImportance: 'Alta importância', MediumImportance: 'Importância relevante', RequiresAction: 'Requer ação', RelevantCorporateEvent: 'Evento corporativo relevante', AffectsPortfolioAsset: 'Afeta ativo da carteira', ReducedConfidence: 'Análise com confiança reduzida' }

function parseOptionalNumber(value: string) {
  const normalized = value.trim().replace(',', '.')
  return normalized.length === 0 ? null : Number(normalized)
}

function App() {
  const queryClient = useQueryClient()
  const [selectedPortfolioId, setSelectedPortfolioId] = useState<string | null>(null)
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null)
  const [editingPositionId, setEditingPositionId] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)
  const [portfolioName, setPortfolioName] = useState('Minha carteira')
  const [positionForm, setPositionForm] = useState({
    ticker: '',
    name: '',
    assetType: 0 as AssetType,
    sector: '',
    quantity: '',
    averagePrice: '',
    currentPrice: '',
  })
  const [editForm, setEditForm] = useState({
    quantity: '',
    averagePrice: '',
    currentPrice: '',
  })

  const portfolios = useQuery({
    queryKey: ['portfolios'],
    queryFn: () => getJson<PortfolioSummary[]>('/api/portfolios'),
  })

  const activePortfolio =
    portfolios.data?.find((item) => item.id === selectedPortfolioId)
    ?? portfolios.data?.[0]
    ?? null

  const dashboard = useQuery({
    queryKey: ['dashboard', activePortfolio?.id],
    queryFn: () => getJson<Dashboard>(`/api/portfolios/${activePortfolio!.id}/dashboard`),
    enabled: Boolean(activePortfolio),
  })

  const documents = useQuery({
    queryKey: ['documents', activePortfolio?.id],
    queryFn: () => getJson<Document[]>(`/api/portfolios/${activePortfolio!.id}/documents`),
    enabled: Boolean(activePortfolio),
  })

  const selectedDocument =
    documents.data?.find((item) => item.id === selectedDocumentId)
    ?? documents.data?.[0]
    ?? null
  const analysisDocumentId = selectedDocument?.id ?? null

  const analysis = useQuery({
    queryKey: ['document-analysis', analysisDocumentId],
    queryFn: ({ queryKey }) => {
      const documentId = queryKey[1]
      return documentId ? getOptionalJson<DocumentAnalysis>(`/api/documents/${documentId}/analysis`) : Promise.resolve(null)
    },
    enabled: Boolean(analysisDocumentId),
  })

  const createPortfolio = useMutation({
    mutationFn: (request: CreatePortfolioRequest) => sendJson<PortfolioSummary, CreatePortfolioRequest>('/api/portfolios', 'POST', request),
    onSuccess: async (created) => {
      setSelectedPortfolioId(created.id)
      setSelectedDocumentId(null)
      setFeedback({ tone: 'success', message: `Carteira "${created.name}" criada com sucesso.` })
      setPortfolioName(created.name)
      await queryClient.invalidateQueries({ queryKey: ['portfolios'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      await queryClient.invalidateQueries({ queryKey: ['documents'] })
    },
    onError: (error: Error) => {
      setFeedback({ tone: 'error', message: error.message })
    },
  })

  const createPosition = useMutation({
    mutationFn: (request: CreatePositionRequest) => sendJson<Position, CreatePositionRequest>(`/api/portfolios/${activePortfolio!.id}/positions`, 'POST', request),
    onSuccess: async (created) => {
      setFeedback({ tone: 'success', message: `Posicao ${created.ticker} adicionada com sucesso.` })
      setPositionForm({ ticker: '', name: '', assetType: 0, sector: '', quantity: '', averagePrice: '', currentPrice: '' })
      await refreshPortfolioData(queryClient, activePortfolio?.id)
    },
    onError: (error: Error) => {
      setFeedback({ tone: 'error', message: error.message })
    },
  })

  const updatePosition = useMutation({
    mutationFn: ({ positionId, request }: { positionId: string; request: UpdatePositionRequest }) =>
      sendJson<Position, UpdatePositionRequest>(`/api/portfolios/${activePortfolio!.id}/positions/${positionId}`, 'PUT', request),
    onSuccess: async (updated) => {
      setEditingPositionId(null)
      setFeedback({ tone: 'success', message: `Posicao ${updated.ticker} atualizada com sucesso.` })
      await refreshPortfolioData(queryClient, activePortfolio?.id)
    },
    onError: (error: Error) => {
      setFeedback({ tone: 'error', message: error.message })
    },
  })

  const deletePosition = useMutation({
    mutationFn: (positionId: string) => sendVoid(`/api/portfolios/${activePortfolio!.id}/positions/${positionId}`, 'DELETE'),
    onSuccess: async () => {
      setEditingPositionId(null)
      setFeedback({ tone: 'success', message: 'Posicao removida com sucesso.' })
      await refreshPortfolioData(queryClient, activePortfolio?.id)
    },
    onError: (error: Error) => {
      setFeedback({ tone: 'error', message: error.message })
    },
  })

  function handleCreatePortfolio(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const name = portfolioName.trim()
    if (!name) {
      setFeedback({ tone: 'error', message: 'Informe um nome para a carteira.' })
      return
    }

    createPortfolio.mutate({ name })
  }

  function handleCreatePosition(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!activePortfolio) {
      setFeedback({ tone: 'error', message: 'Crie ou selecione uma carteira antes de adicionar posicoes.' })
      return
    }

    const quantity = Number(positionForm.quantity.replace(',', '.'))
    const averagePrice = Number(positionForm.averagePrice.replace(',', '.'))
    const currentPrice = parseOptionalNumber(positionForm.currentPrice)

    if (!positionForm.ticker.trim() || !positionForm.name.trim()) {
      setFeedback({ tone: 'error', message: 'Ticker e nome do ativo sao obrigatorios.' })
      return
    }

    if (Number.isNaN(quantity) || Number.isNaN(averagePrice) || (currentPrice !== null && Number.isNaN(currentPrice))) {
      setFeedback({ tone: 'error', message: 'Preencha quantidade, preco medio e cotacao com numeros validos.' })
      return
    }

    createPosition.mutate({
      ticker: positionForm.ticker.trim(),
      name: positionForm.name.trim(),
      assetType: positionForm.assetType,
      sector: positionForm.sector.trim() || null,
      quantity,
      averagePrice,
      currentPrice,
    })
  }

  function startEditing(position: Position) {
    setEditingPositionId(position.id)
    setEditForm({
      quantity: position.quantity.toString(),
      averagePrice: position.averagePrice.toString(),
      currentPrice: position.currentPrice?.toString() ?? '',
    })
    setFeedback(null)
  }

  function handleUpdatePosition(event: FormEvent<HTMLFormElement>, positionId: string) {
    event.preventDefault()
    const quantity = Number(editForm.quantity.replace(',', '.'))
    const averagePrice = Number(editForm.averagePrice.replace(',', '.'))
    const currentPrice = parseOptionalNumber(editForm.currentPrice)

    if (Number.isNaN(quantity) || Number.isNaN(averagePrice) || (currentPrice !== null && Number.isNaN(currentPrice))) {
      setFeedback({ tone: 'error', message: 'Preencha os campos da edicao com numeros validos.' })
      return
    }

    updatePosition.mutate({ positionId, request: { quantity, averagePrice, currentPrice } })
  }

  return (
    <BrowserRouter>
      <main className="min-h-screen bg-stone-50 text-slate-900">
        <nav className="mx-auto flex max-w-6xl items-center justify-between px-6 py-6">
          <Link to="/" className="text-xl font-semibold tracking-tight">lodira<span className="text-amber-600">.</span></Link>
          <div className="flex items-center gap-5 text-sm"><a href="#radar" className="font-semibold text-amber-700 hover:text-amber-800">Radar</a><a href="#aporte" className="font-semibold text-amber-700 hover:text-amber-800">Aporte</a><span className="text-slate-500">Carteira inteligente</span></div>
        </nav>

        <section className="mx-auto grid max-w-6xl gap-12 px-6 pb-20 pt-16 md:grid-cols-[1.15fr_.85fr] md:items-center md:pt-28">
          <div>
            <p className="mb-5 text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Acompanhamento inteligente</p>
            <h1 className="max-w-2xl text-5xl font-semibold leading-[1.05] tracking-tight md:text-7xl">Veja o que realmente importa na sua carteira.</h1>
            <p className="mt-7 max-w-xl text-lg leading-8 text-slate-600">O Lodira acompanha eventos dos seus ativos e transforma informacao dispersa em contexto claro para suas decisoes.</p>
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
              <div><strong className="block text-2xl text-rose-300">{dashboard.data?.positionCount ?? activePortfolio?.positionCount ?? 0}</strong><span className="text-slate-400">ativos</span></div>
              <div><strong className="block text-2xl text-amber-300">{dashboard.data?.positions.length ?? 0}</strong><span className="text-slate-400">posicoes</span></div>
              <div><strong className="block text-2xl text-emerald-300">{dashboard.isLoading ? '...' : activePortfolio ? 'ok' : 'novo'}</strong><span className="text-slate-400">status</span></div>
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-6xl px-6 pb-20">
          {feedback && (
            <div className={`mb-6 rounded-2xl border px-4 py-3 text-sm ${feedback.tone === 'success' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-rose-200 bg-rose-50 text-rose-900'}`}>
              {feedback.message}
            </div>
          )}

          {portfolios.isLoading && <p className="text-slate-500">Carregando carteiras...</p>}
          {portfolios.isError && <p className="text-rose-700">API indisponivel. Inicie o backend em `http://localhost:5041`.</p>}

          {!portfolios.isError && (
            <div className="grid gap-8 lg:grid-cols-[0.92fr_1.08fr]">
              <section className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex items-baseline justify-between border-b border-slate-200 pb-3">
                  <h2 className="text-2xl font-semibold">Carteiras</h2>
                  <span className="text-sm text-slate-500">{portfolios.data?.length ?? 0} cadastradas</span>
                </div>

                <form onSubmit={handleCreatePortfolio} className="mt-5 space-y-4">
                  <div>
                    <label htmlFor="portfolio-name" className="text-sm font-medium text-slate-700">Nome da carteira</label>
                    <input
                      id="portfolio-name"
                      value={portfolioName}
                      onChange={(event) => setPortfolioName(event.target.value)}
                      className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white"
                      placeholder="Minha carteira"
                    />
                  </div>
                  <button
                    type="submit"
                    disabled={createPortfolio.isPending}
                    className="rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {createPortfolio.isPending ? 'Criando...' : 'Criar carteira'}
                  </button>
                </form>

                <div className="mt-6 space-y-3">
                  {portfolios.data?.length === 0 && !portfolios.isLoading && (
                    <div className="rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-4 text-sm text-slate-600">
                      Nenhuma carteira cadastrada ainda. Crie a primeira para adicionar posicoes e acompanhar documentos.
                    </div>
                  )}

                  {portfolios.data?.map((portfolio) => {
                    const isActive = portfolio.id === activePortfolio?.id
                    return (
                      <button
                        key={portfolio.id}
                        type="button"
                        onClick={() => {
                          setSelectedPortfolioId(portfolio.id)
                          setSelectedDocumentId(null)
                          setFeedback(null)
                        }}
                        className={`w-full rounded-2xl border px-4 py-4 text-left transition ${isActive ? 'border-slate-900 bg-slate-900 text-white' : 'border-slate-200 bg-white text-slate-900 hover:border-amber-300 hover:bg-amber-50'}`}
                      >
                        <div className="flex items-center justify-between gap-3">
                          <strong className="text-base">{portfolio.name}</strong>
                          <span className={`rounded-full px-3 py-1 text-xs ${isActive ? 'bg-white/10 text-white' : 'bg-slate-100 text-slate-600'}`}>
                            {portfolio.positionCount} posicoes
                          </span>
                        </div>
                        <p className={`mt-2 text-sm ${isActive ? 'text-slate-200' : 'text-slate-500'}`}>{formatCurrency(portfolio.totalValue)}</p>
                      </button>
                    )
                  })}
                </div>
              </section>

              <section className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
                <div className="flex items-baseline justify-between border-b border-slate-200 pb-3">
                  <h2 className="text-2xl font-semibold">Posicoes</h2>
                  <span className="text-sm text-slate-500">{activePortfolio?.name ?? 'Nenhuma carteira selecionada'}</span>
                </div>

                {!activePortfolio && (
                  <div className="mt-5 rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">
                    Crie uma carteira para comecar a cadastrar ativos.
                  </div>
                )}

                {activePortfolio && (
                  <>
                    <form onSubmit={handleCreatePosition} className="mt-5 grid gap-4 md:grid-cols-2">
                      <div>
                        <label htmlFor="ticker" className="text-sm font-medium text-slate-700">Ticker</label>
                        <input id="ticker" value={positionForm.ticker} onChange={(event) => setPositionForm((current) => ({ ...current, ticker: event.target.value.toUpperCase() }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="BBSE3" />
                      </div>
                      <div>
                        <label htmlFor="asset-name" className="text-sm font-medium text-slate-700">Nome do ativo</label>
                        <input id="asset-name" value={positionForm.name} onChange={(event) => setPositionForm((current) => ({ ...current, name: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="Brasilseguridade" />
                      </div>
                      <div>
                        <label htmlFor="asset-type" className="text-sm font-medium text-slate-700">Tipo do ativo</label>
                        <select id="asset-type" value={positionForm.assetType} onChange={(event) => setPositionForm((current) => ({ ...current, assetType: Number(event.target.value) as AssetType }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white">
                          {assetTypeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                        </select>
                      </div>
                      <div>
                        <label htmlFor="sector" className="text-sm font-medium text-slate-700">Setor</label>
                        <input id="sector" value={positionForm.sector} onChange={(event) => setPositionForm((current) => ({ ...current, sector: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="Seguros" />
                      </div>
                      <div>
                        <label htmlFor="quantity" className="text-sm font-medium text-slate-700">Quantidade</label>
                        <input id="quantity" inputMode="decimal" value={positionForm.quantity} onChange={(event) => setPositionForm((current) => ({ ...current, quantity: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="100" />
                      </div>
                      <div>
                        <label htmlFor="average-price" className="text-sm font-medium text-slate-700">Preco medio</label>
                        <input id="average-price" inputMode="decimal" value={positionForm.averagePrice} onChange={(event) => setPositionForm((current) => ({ ...current, averagePrice: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="29,15" />
                      </div>
                      <div>
                        <label htmlFor="current-price" className="text-sm font-medium text-slate-700">Preco atual</label>
                        <input id="current-price" inputMode="decimal" value={positionForm.currentPrice} onChange={(event) => setPositionForm((current) => ({ ...current, currentPrice: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="31,40" />
                      </div>
                      <div className="flex items-end">
                        <button type="submit" disabled={createPosition.isPending} className="w-full rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:cursor-not-allowed disabled:opacity-60">
                          {createPosition.isPending ? 'Salvando...' : 'Adicionar posicao'}
                        </button>
                      </div>
                    </form>

                    {dashboard.isLoading && <p className="mt-6 text-sm text-slate-500">Carregando posicoes...</p>}
                    {dashboard.isError && <p className="mt-6 text-sm text-rose-700">Nao foi possivel carregar o dashboard desta carteira.</p>}
                    {!dashboard.isLoading && dashboard.data?.positions.length === 0 && (
                      <div className="mt-6 rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">
                        Nenhuma posicao cadastrada ainda para esta carteira.
                      </div>
                    )}

                    {dashboard.data && dashboard.data.positions.length > 0 && (
                      <div className="mt-6 overflow-hidden rounded-2xl border border-slate-200">
                        <div className="grid grid-cols-[1.2fr_.9fr_.8fr_.8fr_.9fr_.9fr_.7fr_.9fr] gap-3 bg-slate-50 px-4 py-3 text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">
                          <span>Ativo</span>
                          <span>Tipo</span>
                          <span>Qtde</span>
                          <span>P. medio</span>
                          <span>P. atual</span>
                          <span>Valor atual</span>
                          <span>Peso</span>
                          <span>Acoes</span>
                        </div>
                        <div className="divide-y divide-slate-200 bg-white">
                          {dashboard.data.positions.map((position) => (
                            <div key={position.id} className="px-4 py-4">
                              {editingPositionId === position.id ? (
                                <form onSubmit={(event) => handleUpdatePosition(event, position.id)} className="grid gap-3 md:grid-cols-[1.1fr_.9fr_.8fr_.8fr_.9fr_.9fr_.7fr_.9fr] md:items-end">
                                  <div>
                                    <p className="text-sm font-semibold">{position.ticker}</p>
                                    <p className="text-xs text-slate-500">{position.name}</p>
                                  </div>
                                  <div className="text-sm text-slate-600">{assetTypeLabels[position.assetType]}</div>
                                  <input value={editForm.quantity} onChange={(event) => setEditForm((current) => ({ ...current, quantity: event.target.value }))} className="rounded-xl border border-slate-300 px-3 py-2 text-sm" />
                                  <input value={editForm.averagePrice} onChange={(event) => setEditForm((current) => ({ ...current, averagePrice: event.target.value }))} className="rounded-xl border border-slate-300 px-3 py-2 text-sm" />
                                  <input value={editForm.currentPrice} onChange={(event) => setEditForm((current) => ({ ...current, currentPrice: event.target.value }))} className="rounded-xl border border-slate-300 px-3 py-2 text-sm" />
                                  <div className="text-sm text-slate-500">{formatCurrency(position.currentValue)}</div>
                                  <div className="text-sm text-slate-500">{position.portfolioWeight.toFixed(2)}%</div>
                                  <div className="flex gap-2">
                                    <button type="submit" disabled={updatePosition.isPending} className="rounded-full bg-slate-900 px-3 py-2 text-xs font-semibold text-white hover:bg-amber-700 disabled:opacity-60">Salvar</button>
                                    <button type="button" onClick={() => setEditingPositionId(null)} className="rounded-full border border-slate-300 px-3 py-2 text-xs font-semibold text-slate-700">Cancelar</button>
                                  </div>
                                </form>
                              ) : (
                                <div className="grid gap-3 md:grid-cols-[1.2fr_.9fr_.8fr_.8fr_.9fr_.9fr_.7fr_.9fr] md:items-center">
                                  <div>
                                    <p className="text-sm font-semibold">{position.ticker}</p>
                                    <p className="text-xs text-slate-500">{position.name}</p>
                                  </div>
                                  <div className="text-sm text-slate-600">{assetTypeLabels[position.assetType]}</div>
                                  <div className="text-sm text-slate-600">{formatNumber(position.quantity)}</div>
                                  <div className="text-sm text-slate-600">{formatCurrency(position.averagePrice)}</div>
                                  <div className="text-sm text-slate-600">{formatCurrency(position.currentPrice)}</div>
                                  <div className="text-sm text-slate-900">{formatCurrency(position.currentValue)}</div>
                                  <div className="text-sm text-slate-600">{position.portfolioWeight.toFixed(2)}%</div>
                                  <div className="flex gap-2">
                                    <button type="button" onClick={() => startEditing(position)} className="rounded-full border border-slate-300 px-3 py-2 text-xs font-semibold text-slate-700 hover:border-amber-400 hover:text-amber-700">Editar</button>
                                    <button type="button" onClick={() => deletePosition.mutate(position.id)} disabled={deletePosition.isPending} className="rounded-full border border-rose-200 px-3 py-2 text-xs font-semibold text-rose-700 hover:bg-rose-50 disabled:opacity-60">Excluir</button>
                                  </div>
                                </div>
                              )}
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </>
                )}
              </section>
            </div>
          )}

          {activePortfolio && (
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
        <RadarSection />
        {activePortfolio && <ContributionSection portfolioId={activePortfolio.id} positions={dashboard.data?.positions ?? []} />}
      </main>
    </BrowserRouter>
  )
}

function RadarSection() {
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<'all' | 'unread' | 'attention' | 'resolved'>('all')
  const [priorityFilter, setPriorityFilter] = useState('all')
  const [assetFilter, setAssetFilter] = useState('')
  const radar = useQuery({ queryKey: ['radar'], queryFn: () => getJson<RadarResponse>('/api/radar') })
  const statusMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: RadarItem['status'] }) => sendJson<RadarItem, { status: RadarItem['status'] }>(`/api/radar/${id}/status`, 'PATCH', { status }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['radar'] }),
  })
  const items = radar.data?.items.filter((item) => (filter === 'all' || (filter === 'unread' && item.status === 'Unread') || (filter === 'attention' && item.requiresAttention) || (filter === 'resolved' && item.status === 'Resolved')) && (priorityFilter === 'all' || item.priority === priorityFilter) && (!assetFilter.trim() || item.affectedAssets.some((asset) => asset.toLowerCase().includes(assetFilter.trim().toLowerCase())) || item.assetTicker.toLowerCase().includes(assetFilter.trim().toLowerCase()))) ?? []
  return <section id="radar" className="mx-auto max-w-6xl px-6 pb-24 pt-10">
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4"><div><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Radar</p><h2 className="mt-2 text-4xl font-semibold tracking-tight">O que merece sua atenção agora.</h2></div><div className="flex gap-2 text-xs font-semibold"><span className="rounded-full bg-rose-50 px-3 py-2 text-rose-800">{radar.data?.requiresAttention ?? 0} precisam de atenção</span><span className="rounded-full bg-slate-100 px-3 py-2 text-slate-700">{radar.data?.unread ?? 0} não lidos</span></div></div>
    <div className="mb-6 flex flex-wrap gap-2">{([['all', 'Todos'], ['unread', 'Não lidos'], ['attention', 'Requer atenção'], ['resolved', 'Resolvidos']] as const).map(([key, label]) => <button key={key} type="button" onClick={() => setFilter(key)} className={`rounded-full px-4 py-2 text-sm font-semibold ${filter === key ? 'bg-slate-900 text-white' : 'border border-slate-300 text-slate-600'}`}>{label}</button>)}</div>
    <div className="mb-6 flex flex-wrap gap-3"><select aria-label="Filtrar por prioridade" value={priorityFilter} onChange={(event) => setPriorityFilter(event.target.value)} className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm"><option value="all">Todas as prioridades</option><option value="Critical">Crítica</option><option value="High">Alta</option><option value="Medium">Média</option><option value="Low">Baixa</option></select><input aria-label="Filtrar por ativo" value={assetFilter} onChange={(event) => setAssetFilter(event.target.value)} placeholder="Filtrar por ativo" className="rounded-full border border-slate-300 bg-white px-4 py-2 text-sm outline-none focus:border-amber-500" /></div>
    {radar.isLoading && <p className="rounded-2xl border border-slate-200 bg-white p-6 text-sm text-slate-500">Carregando Radar...</p>}
    {radar.isError && <p className="rounded-2xl border border-rose-200 bg-rose-50 p-6 text-sm text-rose-800">Não foi possível carregar o Radar.</p>}
    {!radar.isLoading && !radar.isError && items.length === 0 && <p className="rounded-2xl border border-dashed border-slate-300 bg-white p-8 text-sm text-slate-600">Seu Radar está tranquilo.<br /><span className="text-slate-500">Novos eventos relevantes da sua carteira aparecerão aqui.</span></p>}
    <div className="grid gap-4">{items.map((item) => <article key={item.documentAnalysisId} className={`rounded-3xl border p-6 shadow-sm ${radarPriorityTone[item.priority]}`}>
      <div className="flex flex-wrap items-start justify-between gap-4"><div><p className="text-2xl font-semibold">{item.assetTicker || item.affectedAssets.join(', ') || 'Carteira'}</p><p className="mt-1 text-xs font-bold uppercase tracking-[0.16em]">{radarPriorityLabels[item.priority]} PRIORIDADE · score {item.score}</p></div><div className="text-right text-sm"><p>{formatDate(item.publishedAt)}</p><p className="mt-1 font-semibold">{item.status === 'Resolved' ? 'Resolvido' : item.status === 'Read' ? 'Lido' : 'Não lido'}</p></div></div>
      <p className="mt-5 text-base leading-7">{item.summary}</p><p className="mt-4 text-sm leading-6"><strong>Impacto na sua carteira:</strong> {item.portfolioImpact}</p>
      <div className="mt-4 flex flex-wrap gap-2">{item.reasons.map((reason) => <span key={reason} className="rounded-full border border-current/20 px-3 py-1 text-xs font-semibold">{radarReasonLabels[reason] ?? reason}</span>)}</div>
      <div className="mt-5 flex flex-wrap gap-2"><Link to={`/documents/${item.documentId}`} className="rounded-full border border-current/25 px-4 py-2 text-xs font-semibold">Ver análise</Link>{item.status === 'Unread' && <button type="button" onClick={() => statusMutation.mutate({ id: item.documentAnalysisId, status: 'Read' })} className="rounded-full bg-slate-900 px-4 py-2 text-xs font-semibold text-white">Marcar como lido</button>}{item.status !== 'Resolved' && <button type="button" onClick={() => statusMutation.mutate({ id: item.documentAnalysisId, status: 'Resolved' })} className="rounded-full border border-current/25 px-4 py-2 text-xs font-semibold">Resolver</button>}{item.status === 'Resolved' && <button type="button" onClick={() => statusMutation.mutate({ id: item.documentAnalysisId, status: 'Read' })} className="rounded-full border border-current/25 px-4 py-2 text-xs font-semibold">Reabrir</button>}</div>
    </article>)}</div>
  </section>
}

function ContributionSection({ portfolioId, positions }: { portfolioId: string; positions: Position[] }) {
  const queryClient = useQueryClient()
  const [targets, setTargets] = useState<Record<string, string>>({})
  const [amount, setAmount] = useState('')
  const [guidance, setGuidance] = useState<Guidance | null>(null)
  const strategy = useQuery({ queryKey: ['strategy', portfolioId], queryFn: () => getOptionalJson<Strategy>(`/api/portfolios/${portfolioId}/allocation-strategy`) })
  const simulate = useMutation({ mutationFn: () => sendJson<Guidance, { amount: number }>(`/api/portfolios/${portfolioId}/contribution-guidance`, 'POST', { amount: Number(amount.replace(',', '.')) }), onSuccess: setGuidance })
  const classLabels: Record<number, string> = { 0: 'Ações brasileiras', 1: 'FIIs', 2: 'Renda fixa', 3: 'Internacional', 4: 'Outros' }
  const classes = [0, 1, 2, 3, 4, ...new Set(positions.map((position) => position.assetClass))].filter((value, index, all) => all.indexOf(value) === index)
  const loadedTargets = strategy.data?.allocations ?? []
  const currentTargets = classes.map((assetClass) => ({ assetClass, assetId: String(assetClass), ticker: classLabels[assetClass] ?? 'Outros', label: classLabels[assetClass] ?? 'Outros', value: targets[assetClass] ?? loadedTargets.find((target) => target.assetClass === assetClass)?.targetPercentage.toString() ?? '' }))
  const total = currentTargets.reduce((sum, item) => sum + (Number(item.value.replace(',', '.')) || 0), 0)
  const save = useMutation({ mutationFn: () => sendJson<Strategy, { allocations: Array<{ assetClass: number; targetPercentage: number }> }>(`/api/portfolios/${portfolioId}/allocation-strategy`, 'PUT', { allocations: currentTargets.filter((item) => item.value.trim()).map((item) => ({ assetClass: item.assetClass, targetPercentage: Number(item.value.replace(',', '.')) })) }), onSuccess: () => queryClient.invalidateQueries({ queryKey: ['strategy', portfolioId] }) })
  return <section id="aporte" className="mx-auto max-w-6xl px-6 pb-24 pt-4"><div className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm"><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Aporte</p><h2 className="mt-2 text-4xl font-semibold tracking-tight">Oriente seu próximo aporte.</h2><p className="mt-3 text-sm text-slate-600">A Lodira calcula uma distribuição matemática. Nenhuma operação é executada.</p>
    <div className="mt-8 grid gap-8 lg:grid-cols-[1fr_.8fr]"><div><h3 className="font-semibold">Estratégia por ativo</h3>{positions.length === 0 ? <p className="mt-4 text-sm text-slate-500">Cadastre posições para configurar uma estratégia.</p> : <div className="mt-3 divide-y divide-slate-200">{currentTargets.map((item) => <div key={item.assetId} className="flex items-center justify-between gap-4 py-3 text-sm"><span className="font-semibold">{item.ticker}</span><input inputMode="decimal" aria-label={`Alvo ${item.ticker}`} value={item.value} onChange={(event) => setTargets((current) => ({ ...current, [item.assetId]: event.target.value }))} placeholder="0,00" className="w-28 rounded-xl border border-slate-300 px-3 py-2 text-right" /><span>%</span></div>)}</div>}<p className={`mt-3 text-sm font-semibold ${Math.abs(total - 100) <= .01 ? 'text-emerald-700' : 'text-rose-700'}`}>Soma: {total.toFixed(2)}%</p><button type="button" disabled={save.isPending || Math.abs(total - 100) > .01} onClick={() => save.mutate()} className="mt-4 rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white disabled:opacity-50">{save.isPending ? 'Salvando...' : 'Salvar estratégia'}</button>{save.isError && <p className="mt-3 text-sm text-rose-700">Não foi possível salvar a estratégia.</p>}</div>
      <div><h3 className="font-semibold">Simular aporte</h3><div className="mt-4 flex gap-2"><input inputMode="decimal" value={amount} onChange={(event) => setAmount(event.target.value)} placeholder="2.000,00" className="min-w-0 flex-1 rounded-xl border border-slate-300 px-4 py-3" /><button type="button" disabled={simulate.isPending || !amount} onClick={() => simulate.mutate()} className="rounded-full bg-amber-600 px-5 py-3 text-sm font-semibold text-white disabled:opacity-50">Simular</button></div>{simulate.isError && <p className="mt-3 text-sm text-rose-700">{(simulate.error as Error).message}</p>}{guidance && <div className="mt-5 space-y-3"><p className="text-sm font-semibold">Distribuição sugerida para {formatCurrency(guidance.requestedAmount)}</p>{guidance.items.map((item) => <div key={item.assetId} className="rounded-2xl bg-stone-50 p-4 text-sm"><div className="flex justify-between"><strong>{item.ticker}</strong><strong>{formatCurrency(item.suggestedContribution)}</strong></div><p className="mt-1 text-xs text-slate-600">Atual {item.currentWeight.toFixed(2)}% · alvo {item.targetWeight.toFixed(2)}% · projetado {item.projectedWeight.toFixed(2)}%</p>{item.eligibility === 'ReviewRequired' && <p className="mt-2 text-xs font-semibold text-amber-800">Revisão necessária: existe evento relevante pendente no Radar.</p>}</div>)}<div className="border-t border-slate-200 pt-3 text-sm font-semibold">Alocado: {formatCurrency(guidance.allocatedAmount)} · Sem alocação: {formatCurrency(guidance.unallocatedAmount)}</div></div>}</div></div>
  </div></section>
}

async function refreshPortfolioData(queryClient: QueryClient, portfolioId: string | undefined) {
  await queryClient.invalidateQueries({ queryKey: ['portfolios'] })
  if (portfolioId) {
    await queryClient.invalidateQueries({ queryKey: ['dashboard', portfolioId] })
    await queryClient.invalidateQueries({ queryKey: ['documents', portfolioId] })
  } else {
    await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    await queryClient.invalidateQueries({ queryKey: ['documents'] })
  }
}

export default App
