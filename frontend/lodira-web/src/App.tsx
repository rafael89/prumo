import { useEffect, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import './App.css'
import { B3ImportSection, ContributionSection, RadarSection, TargetAllocationSection } from './sections/FeatureSections'
import { PortfolioSection } from './sections/PortfolioSection'
import { DocumentsSection } from './sections/DocumentsSection'
import { DashboardOverview, type Dashboard, type DashboardGuidance } from './sections/DashboardOverview'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faEye, faEyeSlash, faArrowUp } from '@fortawesome/free-solid-svg-icons'

const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5041'

type AssetType = 0 | 1 | 2 | 3
type Feedback = { tone: 'success' | 'error'; message: string } | null

type PortfolioSummary = { id: string; name: string; totalValue: number; positionCount: number }
type ProcessingStatus = { status: 'Pending' | 'Processing' | 'Completed' | 'Failed'; activeCount: number; failedCount: number; message: string }
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

function parseOptionalNumber(value: string) {
  const normalized = value.trim().replace(',', '.')
  return normalized.length === 0 ? null : Number(normalized)
}

function navigationLinkClass(active: boolean, secondary = false) {
  return `shrink-0 border-b-2 pb-2 transition ${active ? 'border-amber-500 font-semibold text-slate-950' : `border-transparent hover:border-amber-200 hover:text-amber-700 ${secondary ? 'font-medium text-slate-500' : 'font-semibold text-slate-700'}`}`
}

function App() {
  const location = useLocation()
  const route = location.pathname
  const queryClient = useQueryClient()
  const [selectedPortfolioId, setSelectedPortfolioId] = useState<string | null>(null)
  const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null)
  const [editingPositionId, setEditingPositionId] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)
  const [privacyHidden, setPrivacyHidden] = useState(false)
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const [showBackToTop, setShowBackToTop] = useState(false)
  const [importProcessing, setImportProcessing] = useState(false)
  const [positionPage, setPositionPage] = useState(1)
  const documentIdFromUrl = new URLSearchParams(location.search).get('documentId') ?? (route.startsWith('/documents/') ? route.slice('/documents/'.length) : null)
  useEffect(() => {
    if (route === '/documents' || route.startsWith('/documents/')) setSelectedDocumentId(documentIdFromUrl)
  }, [route, documentIdFromUrl])
  useEffect(() => {
    if (feedback?.tone !== 'success') return
    const timeout = window.setTimeout(() => setFeedback(null), 4000)
    return () => window.clearTimeout(timeout)
  }, [feedback])
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

  const processingStatus = useQuery({
    queryKey: ['document-processing-status', activePortfolio?.id],
    queryFn: () => getJson<ProcessingStatus>(`/api/portfolios/${activePortfolio!.id}/document-processing-status`),
    enabled: Boolean(activePortfolio),
    refetchInterval: query => query.state.data?.status === 'Pending' || query.state.data?.status === 'Processing' ? 2000 : false,
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
    const averagePrice = 0
    const currentPrice = parseOptionalNumber(positionForm.currentPrice)

    if (!positionForm.ticker.trim()) {
      setFeedback({ tone: 'error', message: 'O ticker do ativo é obrigatório.' })
      return
    }

    if (Number.isNaN(quantity) || (currentPrice !== null && Number.isNaN(currentPrice))) {
      setFeedback({ tone: 'error', message: 'Preencha quantidade e cotação com números válidos.' })
      return
    }

    createPosition.mutate({
      ticker: positionForm.ticker.trim(),
      name: positionForm.ticker.trim(),
      assetType: positionForm.assetType,
      sector: null,
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
          <div className="mx-auto flex max-w-[89.5rem] flex-wrap items-center gap-4">
            <Link to="/" className="mr-auto inline-flex items-center gap-0 text-xl font-semibold tracking-tight">lodira<span className="text-amber-600">.</span></Link>
            <div className="order-3 flex w-full gap-5 overflow-x-auto pt-2 text-sm md:order-2 md:w-auto md:pt-2"><Link to="/" aria-current={route === '/' ? 'page' : undefined} className={navigationLinkClass(route === '/')}>Visão geral</Link><Link to="/portfolio" aria-current={route === '/portfolio' ? 'page' : undefined} className={navigationLinkClass(route === '/portfolio')}>Carteira</Link><Link to="/radar" aria-current={route === '/radar' ? 'page' : undefined} className={navigationLinkClass(route === '/radar')}>Radar</Link><Link to="/contributions" aria-current={route === '/contributions' ? 'page' : undefined} className={navigationLinkClass(route === '/contributions')}>Aportes</Link><Link to="/documents" aria-current={route === '/documents' ? 'page' : undefined} className={navigationLinkClass(route === '/documents', true)}>Documentos</Link></div>
            <div className="order-2 flex items-center gap-2 md:order-3">{importProcessing && <span role="status" className="rounded-full bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-800">Importando carteira...</span>}{processingStatus.data && processingStatus.data.status !== 'Completed' && <span role="status" className={`rounded-full px-3 py-2 text-xs font-semibold ${processingStatus.data.status === 'Failed' ? 'bg-rose-50 text-rose-800' : 'bg-amber-50 text-amber-800'}`}>{processingStatus.data.status === 'Processing' ? 'Processando documentos...' : processingStatus.data.status === 'Pending' ? 'Documentos aguardando processamento...' : processingStatus.data.message}</span>}<button type="button" aria-label={privacyHidden ? "Exibir valores" : "Ocultar valores"} onClick={() => setPrivacyHidden((current) => !current)} className="rounded-full border border-slate-300 bg-white px-3 py-2 text-sm" title={privacyHidden ? "Exibir valores" : "Ocultar valores"}>{privacyHidden ? <FontAwesomeIcon icon={faEyeSlash} /> : <FontAwesomeIcon icon={faEye} />}</button></div>
          </div>
        </nav>
        {route === '/' && <DashboardOverview key={activePortfolio?.id ?? 'empty'} dashboard={dashboard} activePortfolio={activePortfolio} privacyHidden={privacyHidden} onGenerateGuidance={(amount) => sendJson<DashboardGuidance, { amount: number }>(`/api/portfolios/${activePortfolio?.id ?? ''}/contribution-guidance`, 'POST', { amount })} />}
        {route === '/portfolio' && activePortfolio && <TargetAllocationSection portfolioId={activePortfolio.id} />}
        {route === '/contributions' && activePortfolio && <ContributionSection portfolioId={activePortfolio.id} positions={dashboard.data?.positions ?? []} privacyHidden={privacyHidden} />}
        <section className="mx-auto max-w-[89.5rem] px-6 pb-12">
          {feedback && (
            <div className={`mb-6 rounded-2xl border px-4 py-3 text-sm ${feedback.tone === 'success' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-rose-200 bg-rose-50 text-rose-900'}`}>
              {feedback.message}
            </div>
          )}
          {portfolios.isLoading && <p className="text-slate-500">Carregando carteiras...</p>}
          {portfolios.isError && <p className="text-rose-700">API indisponível. Inicie o backend em `http://localhost:5041`.</p>}

          {route === '/portfolio' && !portfolios.isError && <div className="mt-4 grid min-w-0 gap-3 xl:grid-cols-[minmax(0,1.7fr)_minmax(22rem,1fr)] xl:items-start [&>*]:min-w-0">
            <PortfolioSection portfolios={portfolios} activePortfolio={activePortfolio} portfolioName={portfolioName} setPortfolioName={setPortfolioName} handleCreatePortfolio={handleCreatePortfolio} createPortfolio={createPortfolio} deletePortfolio={deletePortfolio} renamePortfolio={renamePortfolio} editingPortfolioId={editingPortfolioId} setEditingPortfolioId={setEditingPortfolioId} editingPortfolioName={editingPortfolioName} setEditingPortfolioName={setEditingPortfolioName} setSelectedPortfolioId={setSelectedPortfolioId} setSelectedDocumentId={setSelectedDocumentId} setPositionPage={setPositionPage} setFeedback={setFeedback} privacyHidden={privacyHidden} positionForm={positionForm} setPositionForm={setPositionForm} assetTypeOptions={assetTypeOptions} handleCreatePosition={handleCreatePosition} createPosition={createPosition} dashboard={dashboard} editingPositionId={editingPositionId} startEditing={startEditing} handleUpdatePosition={handleUpdatePosition} editForm={editForm} setEditForm={setEditForm} updatePosition={updatePosition} setEditingPositionId={setEditingPositionId} deletePosition={deletePosition} selectedAsset={selectedAsset} setSelectedAssetId={setSelectedAssetId} />
            {activePortfolio && <div className="order-2 self-stretch xl:col-start-2 xl:row-start-1 [&>section]:!h-full [&>section]:!px-0 [&>section]:!pb-0 [&>section>div]:!h-full"><B3ImportSection portfolioId={activePortfolio.id} onImportStateChange={setImportProcessing} /></div>}
            {activePortfolio && <DocumentsSection portfolioId={activePortfolio.id} selectedDocumentId={selectedDocumentId} initialDocumentId={documentIdFromUrl} setSelectedDocumentId={setSelectedDocumentId} privacyHidden={privacyHidden} />}
          </div>}
        </section>
        {route === '/radar' && <div className="mx-auto grid max-w-[89.5rem] gap-8 px-6 pb-24"><RadarSection portfolioId={activePortfolio?.id ?? null} /></div>}
        {(route === '/documents' || route.startsWith('/documents/')) && activePortfolio && <div className="mx-auto max-w-6xl px-6 pb-24"><DocumentsSection portfolioId={activePortfolio.id} selectedDocumentId={selectedDocumentId} initialDocumentId={documentIdFromUrl} setSelectedDocumentId={setSelectedDocumentId} privacyHidden={privacyHidden} /></div>}
        {showBackToTop && <button type="button" aria-label="Voltar ao topo" title="Voltar ao topo" onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })} className="fixed bottom-5 right-5 z-30 h-11 w-11 rounded-full bg-slate-900 text-sm font-semibold text-white shadow-lg transition hover:bg-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500"><FontAwesomeIcon icon={faArrowUp} /></button>}
      </main>
  )
}


async function refreshPortfolioData(queryClient: QueryClient, portfolioId: string | undefined) {
  await queryClient.invalidateQueries({ queryKey: ['portfolios'] })
  if (portfolioId) {
    await queryClient.invalidateQueries({ queryKey: ['dashboard', portfolioId] })
    await queryClient.invalidateQueries({ queryKey: ['documents', portfolioId] })
    await queryClient.invalidateQueries({ queryKey: ['health', portfolioId] })
    await queryClient.invalidateQueries({ queryKey: ['targets', portfolioId] })
    await queryClient.invalidateQueries({ queryKey: ['radar', portfolioId] })
  } else {
    await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    await queryClient.invalidateQueries({ queryKey: ['documents'] })
    await queryClient.invalidateQueries({ queryKey: ['health'] })
    await queryClient.invalidateQueries({ queryKey: ['targets'] })
    await queryClient.invalidateQueries({ queryKey: ['radar'] })
  }
}

export default App
