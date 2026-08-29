import { useEffect, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import './App.css'
import { B3ImportSection, ContributionSection, ObjectiveSection, PortfolioHealthSection, RadarSection, StrategyPreferencesSection } from './sections/FeatureSections'
import { PortfolioSection } from './sections/PortfolioSection'
import { DocumentsSection } from './sections/DocumentsSection'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faEye, faEyeSlash, faArrowUp } from '@fortawesome/free-solid-svg-icons'

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
type Dashboard = { id: string; name: string; totalValue: number; positionCount: number; positions: Position[]; page: number; pageSize: number; totalItems: number; totalPages: number; state?: string; portfolioOverview?: { currentValue: number; healthStatus: string; healthSummary: string; mainDeviationSummary: string | null; lastPortfolioUpdate: string | null }; attentionNow?: Array<{ assessmentId: string; asset: string; eventSummary: string; whyItMatters: string; suggestedBehavior: string; occurredAt: string | null }>; contributionPreview?: { status: string; primaryCapitalNeed: number | null; summary: string }; portfolioHealthSummary?: { status: string; summary: string; allocationDeviations: Array<{ assetClass: number; currentPercentage: number; targetPercentage: number; deviationPercentagePoints: number }>; mainDeviation: { assetClass: number; currentPercentage: number; targetPercentage: number; deviationPercentagePoints: number } | null; concentrations: Array<{ ticker: string; currentWeight: number; configuredMaximumWeight: number | null; exceedsConfiguredMaximum: boolean }> }; recentUpdates?: Array<{ assessmentId: string; asset: string; eventSummary: string; whyItMatters: string; suggestedBehavior: string; occurredAt: string | null }> }
type DashboardGuidance = { allocationStyle: string; contributionAmount: number; allocations: Array<{ assetId: string; ticker: string; amount: number; projectedAssetWeight: number; reason: string }>; avoidedCandidates: Array<{ assetId: string; ticker: string; reason: string }>; unallocatedAmount: number; reasons: string[] }

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

const assetTypeOptions: Array<{ value: AssetType; label: string }> = [
  { value: 0, label: 'Ação' },
  { value: 1, label: 'FII' },
  { value: 2, label: 'ETF' },
  { value: 3, label: 'Outro' },
]

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`)
  if (!response.ok) throw new Error('Não foi possível carregar os dados.')
  return response.json()
}

async function sendJson<TResponse, TRequest>(path: string, method: 'POST' | 'PUT' | 'PATCH', body: TRequest): Promise<TResponse> {
  const response = await fetch(`${apiUrl}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    throw new Error(await readError(response, path.includes('contribution-guidance') ? 'Não foi possível simular o aporte. Tente novamente.' : 'Não foi possível concluir a operação'))
  }

  return response.json()
}

async function sendVoid(path: string, method: 'DELETE'): Promise<void> {
  const response = await fetch(`${apiUrl}${path}`, { method })
  if (!response.ok) {
    throw new Error(await readError(response, 'Não foi possível concluir a operação'))
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

export function formatCurrency(value: number | null | undefined, hidden = false) {
  if (hidden) return 'R$ ••••••'
  if (value == null) return 'Sem valor'
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

function parseOptionalNumber(value: string) {
  const normalized = value.trim().replace(',', '.')
  return normalized.length === 0 ? null : Number(normalized)
}

function formatDate(value: string) { return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium' }).format(new Date(value)) }

function DashboardOverview({ dashboard, activePortfolio, privacyHidden }: { dashboard: { data?: Dashboard; isLoading: boolean; isError: boolean }; activePortfolio: PortfolioSummary | null; privacyHidden: boolean }) {
  const [amount, setAmount] = useState('')
  const [guidance, setGuidance] = useState<DashboardGuidance | null>(null)
  const [guidanceError, setGuidanceError] = useState<string | null>(null)
  const guidanceMutation = useMutation({ mutationFn: () => sendJson<DashboardGuidance, { amount: number }>(`/api/portfolios/${activePortfolio?.id ?? ''}/contribution-guidance`, 'POST', { amount: Number(amount.replace(',', '.')) }), onSuccess: (result) => { setGuidance(result); setGuidanceError(null) }, onError: () => setGuidanceError('Falha ao gerar orientação.') })
  if (!activePortfolio) return <section className="mx-auto max-w-6xl px-6 py-12"><div className="rounded-3xl border border-amber-200 bg-amber-50 p-8"><h1 className="text-3xl font-semibold">Comece pela sua carteira</h1><p className="mt-3 text-slate-700">Crie ou importe sua carteira para o Lodira entender seus investimentos.</p><Link to="/portfolio" className="mt-5 inline-flex rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Ir para Carteira</Link></div></section>
  if (dashboard.isLoading) return <section className="mx-auto max-w-6xl px-6 py-12"><p className="rounded-2xl bg-white p-6 text-slate-500">Carregando sua visão geral...</p></section>
  if (dashboard.isError || !dashboard.data) return <section className="mx-auto max-w-6xl px-6 py-12"><p className="rounded-2xl bg-rose-50 p-6 text-rose-800">Não foi possível carregar a visão geral.</p></section>
  const data = dashboard.data
  if (data.positionCount === 0) return <section className="mx-auto max-w-6xl px-6 py-12"><div className="rounded-3xl border border-amber-200 bg-amber-50 p-8"><h1 className="text-3xl font-semibold">Comece pela sua carteira</h1><p className="mt-3 text-slate-700">Importe seu relatório consolidado da B3 ou cadastre posições para o Lodira entender seus investimentos.</p><Link to="/portfolio" className="mt-5 inline-flex rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Ir para Carteira</Link></div></section>
  const overview = data.portfolioOverview ? { ...data.portfolioOverview, portfolioName: data.name } : undefined
  const preview = data.contributionPreview
  /* legacy conditional mutation removed from execution */
  /*
  const guidanceMutation = useMutation({ mutationFn: () => sendJson<DashboardGuidance, { amount: number }>(`/api/portfolios/${activePortfolio.id}/contribution-guidance`, 'POST', { amount: Number(amount.replace(',', '.')) }), onSuccess: (result) => { setGuidance(result); setGuidanceError(null) }, onError: () => setGuidanceError('Não foi possível gerar a orientação de aporte.') })
  */
  return <section className="mx-auto max-w-6xl space-y-6 px-6 py-8">
    {preview?.status === 'Available' && <DashboardContributionForm privacyHidden={privacyHidden} amount={amount} setAmount={setAmount} guidance={guidance} error={guidanceError} mutation={guidanceMutation} />}
    <div className="rounded-3xl border border-slate-200 bg-white p-7 shadow-sm"><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Minha carteira</p><h1 className="mt-2 text-4xl font-semibold">{overview?.portfolioName ?? data.name}</h1><p className="mt-4 text-3xl font-semibold">{formatCurrency(overview?.currentValue ?? data.totalValue, privacyHidden)}</p><p className="mt-2 text-lg text-slate-700">{overview?.healthSummary ?? 'Estado da carteira indisponível.'}</p>{overview?.mainDeviationSummary && <p className="mt-2 text-sm text-slate-600">{overview.mainDeviationSummary}</p>}{overview?.lastPortfolioUpdate && <p className="mt-4 text-xs text-slate-500">Atualizada em {formatDate(overview.lastPortfolioUpdate)}</p>}</div>
    <div className="rounded-3xl border border-slate-200 bg-white p-7 shadow-sm"><div className="flex items-center justify-between"><div><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Atenção agora</p><h2 className="mt-2 text-2xl font-semibold">O que merece sua atenção.</h2></div><Link to="/radar" className="rounded-full border border-slate-300 px-4 py-2 text-sm font-semibold">Ver Radar</Link></div>{data.attentionNow?.length ? <div className="mt-5 grid gap-3 md:grid-cols-3">{data.attentionNow.map(item => <div key={item.assessmentId} className="rounded-2xl bg-amber-50 p-4"><strong>{item.asset}</strong><p className="mt-2 text-sm">{item.eventSummary}</p><p className="mt-2 text-xs text-slate-600">{item.whyItMatters}</p></div>)}</div> : <p className="mt-5 rounded-2xl bg-emerald-50 p-5 text-emerald-900"><strong>Tudo tranquilo por aqui.</strong><br /><span className="text-sm">Nenhum acontecimento recente exige sua atenção.</span></p>}</div>
    <div className="rounded-3xl border border-slate-200 bg-white p-7 shadow-sm"><div className="flex items-center justify-between"><div><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Próximo aporte</p><h2 className="mt-2 text-2xl font-semibold">{preview?.summary ?? 'Configure sua estratégia para receber orientação de aporte.'}</h2></div><Link to="/contributions" className="rounded-full bg-slate-900 px-4 py-2 text-sm font-semibold text-white">Gerar orientação</Link></div></div>
    <div className="rounded-3xl border border-slate-200 bg-white p-7 shadow-sm"><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Saúde da carteira</p><h2 className="mt-2 text-2xl font-semibold">{data.portfolioHealthSummary?.summary}</h2>{data.portfolioHealthSummary?.mainDeviation && <p className="mt-3 text-sm text-slate-600">Principal desvio: atual {data.portfolioHealthSummary.mainDeviation.currentPercentage.toFixed(2)}% contra alvo {data.portfolioHealthSummary.mainDeviation.targetPercentage.toFixed(2)}%.</p>}<Link to="/portfolio" className="mt-4 inline-block text-sm font-semibold underline">Ver distribuição completa</Link></div>
    <div className="rounded-3xl border border-slate-200 bg-white p-7 shadow-sm"><div className="flex items-center justify-between"><div><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">O que mudou recentemente</p><h2 className="mt-2 text-2xl font-semibold">Acompanhamento da carteira</h2></div><Link to="/radar" className="text-sm font-semibold underline">Ver Radar</Link></div>{data.recentUpdates?.length ? <div className="mt-5 grid gap-3 md:grid-cols-3">{data.recentUpdates.map(item => <div key={item.assessmentId} className="rounded-2xl bg-stone-50 p-4"><strong>{item.asset}</strong><p className="mt-2 text-sm">{item.eventSummary}</p><p className="mt-2 text-xs text-slate-600">{item.suggestedBehavior}</p></div>)}</div> : <p className="mt-5 text-sm text-slate-500">Nenhuma atualização relevante recente.</p>}</div>
  </section>
}

function DashboardContributionForm({ privacyHidden, amount, setAmount, guidance, error, mutation }: { privacyHidden: boolean; amount: string; setAmount: (value: string) => void; guidance: DashboardGuidance | null; error: string | null; mutation: any }) {
  return <div className="rounded-3xl border border-slate-200 bg-white p-7 shadow-sm"><p className="text-sm font-semibold uppercase tracking-[0.2em] text-amber-700">Gerar orientação</p><div className="mt-3 flex flex-wrap gap-3"><input aria-label="Valor do aporte no Dashboard" inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} placeholder="R$ 5.000,00" className="min-w-[12rem] flex-1 rounded-xl border border-slate-300 px-4 py-3" /><button type="button" disabled={mutation.isPending || !amount} onClick={() => mutation.mutate()} className="rounded-xl bg-amber-600 px-5 py-3 text-sm font-semibold text-white disabled:opacity-50">{mutation.isPending ? 'Calculando...' : 'Gerar orientação'}</button></div>{error && <div className="mt-3 text-sm text-rose-700">{error} <button type="button" className="underline" onClick={() => mutation.mutate()}>Tentar novamente</button></div>}{guidance && <div className="mt-5 rounded-2xl bg-stone-50 p-5"><p className="text-lg font-semibold">{guidance.allocationStyle === 'Concentrated' ? 'Aporte concentrado' : guidance.allocationStyle === 'Distributed' ? 'Aporte distribuído' : 'Sem alocação sugerida'}</p>{guidance.allocations.slice(0, 3).map(item => <p key={item.assetId} className="mt-2 text-sm"><strong>{formatCurrency(item.amount, privacyHidden)} → {item.ticker}</strong><br /><span className="text-slate-600">{item.reason}</span></p>)}{guidance.unallocatedAmount > 0 && <p className="mt-3 text-sm text-slate-700">{formatCurrency(guidance.unallocatedAmount, privacyHidden)} permaneceram sem alocação.</p>}<Link to="/contributions" className="mt-4 inline-block text-sm font-semibold underline">Ver orientação completa</Link></div>}</div>
}

function App() {
  const location = useLocation()
  const route = location.pathname
  void formatCurrency
  const queryClient = useQueryClient()
  const [selectedPortfolioId, setSelectedPortfolioId] = useState<string | null>(null)
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null)
  const [editingPositionId, setEditingPositionId] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)
  const [privacyHidden, setPrivacyHidden] = useState(false)
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const [showBackToTop, setShowBackToTop] = useState(false)
  const [positionPage, setPositionPage] = useState(1)
  useEffect(() => {
    const onScroll = () => setShowBackToTop(window.scrollY > 500)
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])
  const [portfolioName, setPortfolioName] = useState('Minha carteira')
  const [editingPortfolioId, setEditingPortfolioId] = useState<string | null>(null)
  const [editingPortfolioName, setEditingPortfolioName] = useState('')
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
    queryKey: ['dashboard', activePortfolio?.id, positionPage],
    queryFn: () => getJson<Dashboard>(`/api/portfolios/${activePortfolio!.id}/dashboard?page=${positionPage}&pageSize=10`),
    enabled: Boolean(activePortfolio),
  })

  const selectedAsset = dashboard.data?.positions.find((position) => position.assetId === selectedAssetId) ?? null
  const createPortfolio = useMutation({
    mutationFn: (request: CreatePortfolioRequest) => sendJson<PortfolioSummary, CreatePortfolioRequest>('/api/portfolios', 'POST', request),
    onSuccess: async (created) => {
      setSelectedPortfolioId(created.id)
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

  const deletePortfolio = useMutation({
    mutationFn: (portfolioId: string) => sendVoid(`/api/portfolios/${portfolioId}`, 'DELETE'),
    onSuccess: async (_result, portfolioId) => {
      if (activePortfolio?.id === portfolioId) setSelectedPortfolioId(null)
      setFeedback({ tone: 'success', message: 'Carteira excluída com sucesso.' })
      await queryClient.invalidateQueries({ queryKey: ['portfolios'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      await queryClient.invalidateQueries({ queryKey: ['documents'] })
    },
    onError: (error: Error) => setFeedback({ tone: 'error', message: error.message }),
  })

  const renamePortfolio = useMutation({
    mutationFn: ({ portfolioId, name }: { portfolioId: string; name: string }) => sendJson<void, { name: string }>(`/api/portfolios/${portfolioId}`, 'PUT', { name }),
    onSuccess: async () => {
      setEditingPortfolioId(null)
      setEditingPortfolioName('')
      setFeedback({ tone: 'success', message: 'Carteira renomeada com sucesso.' })
      await queryClient.invalidateQueries({ queryKey: ['portfolios'] })
    },
    onError: (error: Error) => setFeedback({ tone: 'error', message: error.message }),
  })

  const createPosition = useMutation({
    mutationFn: (request: CreatePositionRequest) => sendJson<Position, CreatePositionRequest>(`/api/portfolios/${activePortfolio!.id}/positions`, 'POST', request),
    onSuccess: async (created) => {
      setFeedback({ tone: 'success', message: `Posição ${created.ticker} adicionada com sucesso.` })
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
      setFeedback({ tone: 'success', message: `Posição ${updated.ticker} atualizada com sucesso.` })
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
      setFeedback({ tone: 'success', message: 'Posição removida com sucesso.' })
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
      setFeedback({ tone: 'error', message: 'Crie ou selecione uma carteira antes de adicionar posições.' })
      return
    }

    const quantity = Number(positionForm.quantity.replace(',', '.'))
    const averagePrice = Number(positionForm.averagePrice.replace(',', '.'))
    const currentPrice = parseOptionalNumber(positionForm.currentPrice)

    if (!positionForm.ticker.trim() || !positionForm.name.trim()) {
      setFeedback({ tone: 'error', message: 'Ticker e nome do ativo são obrigatórios.' })
      return
    }

    if (Number.isNaN(quantity) || Number.isNaN(averagePrice) || (currentPrice !== null && Number.isNaN(currentPrice))) {
      setFeedback({ tone: 'error', message: 'Preencha quantidade, preço médio e cotação com números válidos.' })
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
      setFeedback({ tone: 'error', message: 'Preencha os campos da edição com números válidos.' })
      return
    }

    updatePosition.mutate({ positionId, request: { quantity, averagePrice, currentPrice } })
  }

  return (
      <main className="min-h-screen bg-stone-50 text-slate-900">
        <nav className="sticky top-0 z-20 border-b border-slate-200 bg-stone-50/95 px-4 py-4 backdrop-blur md:px-6">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4">
            <Link to="/" className="mr-auto inline-flex items-center gap-0 text-xl font-semibold tracking-tight">lodira<span className="text-amber-600">.</span></Link>
            <div className="order-3 flex w-full gap-4 overflow-x-auto text-sm md:order-2 md:w-auto"><Link to="/" className="font-semibold text-slate-700 hover:text-amber-700">Visão geral</Link><Link to="/portfolio" className="font-semibold text-slate-700 hover:text-amber-700">Carteira</Link><Link to="/radar" className="font-semibold text-slate-700 hover:text-amber-700">Radar</Link><Link to="/contributions" className="font-semibold text-slate-700 hover:text-amber-700">Aportes</Link><Link to="/documents" className="font-semibold text-slate-700 hover:text-amber-700">Documentos</Link><Link to="/settings" className="font-semibold text-slate-700 hover:text-amber-700">Configurações</Link></div>
            <div className="order-2 flex items-center gap-2 md:order-3"><button type="button" aria-label={privacyHidden ? "Exibir valores" : "Ocultar valores"} onClick={() => setPrivacyHidden((current) => !current)} className="rounded-full border border-slate-300 bg-white px-3 py-2 text-sm" title={privacyHidden ? "Exibir valores" : "Ocultar valores"}>{privacyHidden ? <FontAwesomeIcon icon={faEyeSlash} /> : <FontAwesomeIcon icon={faEye} />}</button></div>
          </div>
        </nav>
        {route === '/' && <DashboardOverview dashboard={dashboard} activePortfolio={activePortfolio} privacyHidden={privacyHidden} />}
        {route === '/portfolio' && activePortfolio && <><ObjectiveSection portfolioId={activePortfolio.id} /><PortfolioHealthSection portfolioId={activePortfolio.id} /><StrategyPreferencesSection portfolioId={activePortfolio.id} /></>}
        {route === '/contributions' && activePortfolio && <ContributionSection portfolioId={activePortfolio.id} positions={dashboard.data?.positions ?? []} privacyHidden={privacyHidden} />}
        <section className="mx-auto max-w-[89.5rem] px-6 pb-12">
          {feedback && (
            <div className={`mb-6 rounded-2xl border px-4 py-3 text-sm ${feedback.tone === 'success' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-rose-200 bg-rose-50 text-rose-900'}`}>
              {feedback.message}
            </div>
          )}
          {portfolios.isLoading && <p className="text-slate-500">Carregando carteiras...</p>}
          {portfolios.isError && <p className="text-rose-700">API indisponível. Inicie o backend em `http://localhost:5041`.</p>}

          {route === '/portfolio' && !portfolios.isError && <div className="grid min-w-0 gap-3 xl:grid-cols-[minmax(0,1.7fr)_minmax(22rem,1fr)] xl:items-start [&>*]:min-w-0">
            <PortfolioSection portfolios={portfolios} activePortfolio={activePortfolio} portfolioName={portfolioName} setPortfolioName={setPortfolioName} handleCreatePortfolio={handleCreatePortfolio} createPortfolio={createPortfolio} deletePortfolio={deletePortfolio} renamePortfolio={renamePortfolio} editingPortfolioId={editingPortfolioId} setEditingPortfolioId={setEditingPortfolioId} editingPortfolioName={editingPortfolioName} setEditingPortfolioName={setEditingPortfolioName} setSelectedPortfolioId={setSelectedPortfolioId} setSelectedDocumentId={setSelectedDocumentId} setPositionPage={setPositionPage} setFeedback={setFeedback} privacyHidden={privacyHidden} positionForm={positionForm} setPositionForm={setPositionForm} assetTypeOptions={assetTypeOptions} handleCreatePosition={handleCreatePosition} createPosition={createPosition} dashboard={dashboard} editingPositionId={editingPositionId} startEditing={startEditing} handleUpdatePosition={handleUpdatePosition} editForm={editForm} setEditForm={setEditForm} updatePosition={updatePosition} setEditingPositionId={setEditingPositionId} deletePosition={deletePosition} selectedAsset={selectedAsset} setSelectedAssetId={setSelectedAssetId} />
            {activePortfolio && <div className="order-2 self-stretch xl:col-start-2 xl:row-start-1 [&>section]:!h-full [&>section]:!px-0 [&>section]:!pb-0 [&>section>div]:!h-full"><B3ImportSection portfolioId={activePortfolio.id} /></div>}
            {activePortfolio && <DocumentsSection portfolioId={activePortfolio.id} selectedDocumentId={selectedDocumentId} setSelectedDocumentId={setSelectedDocumentId} privacyHidden={privacyHidden} />}
          </div>}
        </section>
        {route === '/radar' && <div className="mx-auto grid max-w-[89.5rem] gap-8 px-6 pb-24"><RadarSection portfolioId={activePortfolio?.id ?? null} /></div>}
        {route === '/documents' && activePortfolio && <div className="mx-auto max-w-6xl px-6 pb-24"><DocumentsSection portfolioId={activePortfolio.id} selectedDocumentId={selectedDocumentId} setSelectedDocumentId={setSelectedDocumentId} privacyHidden={privacyHidden} /></div>}
        {route === '/settings' && <section className="mx-auto max-w-6xl px-6 py-12"><div className="rounded-3xl border border-slate-200 bg-white p-7"><h1 className="text-3xl font-semibold">Configurações</h1><p className="mt-3 text-slate-600">As configurações de objetivo e estratégia ficam na área da Carteira.</p><Link to="/portfolio" className="mt-5 inline-flex rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Abrir Carteira</Link></div></section>}
        {showBackToTop && <button type="button" aria-label="Voltar ao topo" title="Voltar ao topo" onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })} className="fixed bottom-5 right-5 z-30 h-11 w-11 rounded-full bg-slate-900 text-sm font-semibold text-white shadow-lg transition hover:bg-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500"><FontAwesomeIcon icon={faArrowUp} /></button>}
      </main>
  )
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
