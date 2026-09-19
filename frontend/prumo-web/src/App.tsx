import { lazy, Suspense, useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import './App.css'
import type { Dashboard, DashboardAssessment } from './sections/DashboardOverview'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUp, faBell, faCheckDouble, faMoon, faSun } from '@fortawesome/free-solid-svg-icons'
import { getJson, sendJson, sendVoid } from './api/client'
import { Pagination } from './components/Pagination'
import { AppDialog } from './components/AppDialog'

const B3ImportSection = lazy(() => import('./sections/FeatureSections').then(module => ({ default: module.B3ImportSection })))
const RadarSection = lazy(() => import('./sections/FeatureSections').then(module => ({ default: module.RadarSection })))
const TargetAllocationSection = lazy(() => import('./sections/FeatureSections').then(module => ({ default: module.TargetAllocationSection })))
const ContributionSection = lazy(() => import('./sections/ContributionSectionModern').then(module => ({ default: module.ContributionSectionModern })))
const PortfolioSection = lazy(() => import('./sections/PortfolioSection').then(module => ({ default: module.PortfolioSection })))
const DocumentsSection = lazy(() => import('./sections/DocumentsSection').then(module => ({ default: module.DocumentsSection })))
const AboutSection = lazy(() => import('./sections/AboutSection').then(module => ({ default: module.AboutSection })))
const HistorySection = lazy(() => import('./sections/HistorySection').then(module => ({ default: module.HistorySection })))
const DashboardOverview = lazy(() => import('./sections/DashboardOverview').then(module => ({ default: module.DashboardOverview })))
const RecentAnalysisModal = lazy(() => import('./sections/DashboardOverview').then(module => ({ default: module.RecentAnalysisModal })))

type AssetType = 0 | 1 | 2 | 3
type Feedback = { tone: 'success' | 'error'; message: string } | null
type ThemePreference = 'system' | 'light' | 'dark'
type RadarNotificationItem = {
  assessmentId: string
  documentAnalysisId: string
  documentId?: string | null
  evidence?: Array<{ documentId: string }>
  attentionPriority: number | string
  actionRequirement: number
  capitalDecisionImpact: number
  status: number | string
  userState: number
  summary: string
  whyItMatters: string
  suggestedBehavior: string
  occurredAt: string | null
  eventSummary: string
  affectedAssets: string[]
  documentImportance?: string
}
type RadarNotificationResponse = {
  attentionNow: RadarNotificationItem[]
  unread: number
}

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
  registerPurchase?: boolean
}
type UpdatePositionRequest = {
  quantity: number
  averagePrice: number
  currentPrice: number | null
  registerPurchase?: boolean
}
type PurchaseConfirmation =
  | { kind: 'create'; request: CreatePositionRequest; ticker: string }
  | { kind: 'update'; positionId: string; request: UpdatePositionRequest; ticker: string; addedQuantity: number }

const assetTypeOptions: Array<{ value: AssetType; label: string }> = [
  { value: 0, label: 'Ação' },
  { value: 1, label: 'FII' },
  { value: 2, label: 'ETF' },
  { value: 3, label: 'Outro' },
]

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
  const documentIdFromUrl = new URLSearchParams(location.search).get('documentId') ?? (route.startsWith('/documents/') ? route.slice('/documents/'.length) : null)
  const documentSelectionKey = `${route}|${location.search}`
  const [selectedPortfolioId, setSelectedPortfolioId] = useState<string | null>(null)
  const [documentSelection, setDocumentSelection] = useState<{ key: string; id: string | null }>({ key: '', id: null })
  const [editingPositionId, setEditingPositionId] = useState<string | null>(null)
  const [purchaseConfirmation, setPurchaseConfirmation] = useState<PurchaseConfirmation | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)
  const [privacyHidden, setPrivacyHidden] = useState(false)
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const [showBackToTop, setShowBackToTop] = useState(false)
  const [historyTotalPages, setHistoryTotalPages] = useState(1)
  const [historyCursors, setHistoryCursors] = useState<Record<number, string | null>>({ 1: null })
  const [importProcessing, setImportProcessing] = useState(false)
  const [failedNoticeOverride, setFailedNoticeOverride] = useState<{ portfolioId: string; dismissed: boolean } | null>(null)
  const [themePreference, setThemePreference] = useState<ThemePreference>(() => {
    const saved = localStorage.getItem('prumo.theme.preference')
    return saved === 'light' || saved === 'dark' ? saved : 'system'
  })
  const [systemDarkMode, setSystemDarkMode] = useState(() => window.matchMedia('(prefers-color-scheme: dark)').matches)
  const darkMode = themePreference === 'dark' || (themePreference === 'system' && systemDarkMode)
  const [positionPage, setPositionPage] = useState(1)
  const handleHistoryPageInfo = useCallback((hasMore: boolean, nextBefore: string | null) => {
    setHistoryTotalPages(hasMore ? positionPage + 1 : positionPage)
    if (nextBefore) setHistoryCursors(current => ({ ...current, [positionPage + 1]: nextBefore }))
  }, [positionPage])
  useEffect(() => {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    const onSystemThemeChange = (event: MediaQueryListEvent) => setSystemDarkMode(event.matches)
    mediaQuery.addEventListener('change', onSystemThemeChange)
    return () => mediaQuery.removeEventListener('change', onSystemThemeChange)
  }, [])
  useEffect(() => {
    document.documentElement.dataset.theme = darkMode ? 'dark' : 'light'
    document.documentElement.style.colorScheme = darkMode ? 'dark' : 'light'
    if (themePreference === 'system') localStorage.removeItem('prumo.theme.preference')
    else localStorage.setItem('prumo.theme.preference', themePreference)
  }, [darkMode, themePreference])
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
  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' })
  }, [location.pathname, location.search])
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
  const activeDocumentSelectionKey = `${documentSelectionKey}|${activePortfolio?.id ?? ''}`
  const selectedDocumentId = documentSelection.key === activeDocumentSelectionKey ? documentSelection.id : documentIdFromUrl
  const setSelectedDocumentId = (id: string | null) => setDocumentSelection({ key: activeDocumentSelectionKey, id })
  const failedNoticeDismissed = activePortfolio
    ? failedNoticeOverride?.portfolioId === activePortfolio.id
      ? failedNoticeOverride.dismissed
      : localStorage.getItem(`prumo.failed-notice.${activePortfolio.id}`) === '1'
    : false

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
  const [notificationPanelOpen, setNotificationPanelOpen] = useState(false)
  const [notificationPanelMounted, setNotificationPanelMounted] = useState(false)
  const [notificationToast, setNotificationToast] = useState<RadarNotificationItem | null>(null)
  const [notificationToastVisible, setNotificationToastVisible] = useState(false)
  const [selectedNotification, setSelectedNotification] = useState<RadarNotificationItem | null>(null)
  const notificationPanelRef = useRef<HTMLDivElement>(null)
  const notificationCloseTimerRef = useRef<number | null>(null)
  const notificationToastCloseTimerRef = useRef<number | null>(null)
  const closeNotificationPanel = () => {
    setNotificationPanelOpen(false)
    if (notificationCloseTimerRef.current !== null) window.clearTimeout(notificationCloseTimerRef.current)
    notificationCloseTimerRef.current = window.setTimeout(() => setNotificationPanelMounted(false), 160)
  }
  const toggleNotificationPanel = () => {
    if (notificationPanelOpen) {
      closeNotificationPanel()
      return
    }
    if (notificationCloseTimerRef.current !== null) window.clearTimeout(notificationCloseTimerRef.current)
    setNotificationPanelMounted(true)
    setNotificationPanelOpen(true)
  }
  useEffect(() => {
    if (!notificationPanelOpen) return
    const handleOutsideClick = (event: MouseEvent) => {
      if (!notificationPanelRef.current?.contains(event.target as Node)) closeNotificationPanel()
    }
    document.addEventListener('mousedown', handleOutsideClick)
    return () => document.removeEventListener('mousedown', handleOutsideClick)
  })
  const radarNotifications = useQuery({
    queryKey: ['radar-notifications', activePortfolio?.id],
    queryFn: () => getJson<RadarNotificationResponse>(`/api/radar?portfolioId=${activePortfolio!.id}&page=1&pageSize=100&requiresAttention=true&userState=Unread`),
    enabled: Boolean(activePortfolio),
    refetchInterval: 20000,
  })
  const markNotificationSeen = useMutation({
    mutationFn: (assessmentId: string) => sendJson<unknown, object>(`/api/radar/${assessmentId}/seen?portfolioId=${activePortfolio!.id}`, 'PATCH', {}),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['radar-notifications', activePortfolio?.id] })
      await queryClient.invalidateQueries({ queryKey: ['radar', activePortfolio?.id] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard', activePortfolio?.id] })
    },
    onError: (error: Error) => setFeedback({ tone: 'error', message: error.message }),
  })
  const markAllNotificationsSeen = useMutation({
    mutationFn: () => sendJson<{ updatedCount: number }, object>(`/api/radar/seen-all?portfolioId=${activePortfolio!.id}`, 'PATCH', {}),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['radar-notifications', activePortfolio?.id] })
      await queryClient.invalidateQueries({ queryKey: ['radar', activePortfolio?.id] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard', activePortfolio?.id] })
    },
    onError: (error: Error) => setFeedback({ tone: 'error', message: error.message }),
  })
  const resolveNotification = useMutation({
    mutationFn: (assessmentId: string) => sendJson<unknown, { status: string }>(`/api/radar/${assessmentId}/status?portfolioId=${activePortfolio!.id}`, 'PATCH', { status: 'Closed' }),
    onSuccess: async () => {
      setSelectedNotification(null)
      await queryClient.invalidateQueries({ queryKey: ['radar-notifications', activePortfolio?.id] })
      await queryClient.invalidateQueries({ queryKey: ['radar', activePortfolio?.id] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard', activePortfolio?.id] })
    },
    onError: (error: Error) => setFeedback({ tone: 'error', message: error.message }),
  })
  const openNotification = (item: RadarNotificationItem) => {
    closeNotificationPanel()
    setSelectedNotification({ ...item, documentId: item.documentId ?? item.evidence?.[0]?.documentId ?? null, userState: 1 })
    if (item.userState === 0) markNotificationSeen.mutate(item.assessmentId)
  }
  const dismissNotificationToast = () => {
    setNotificationToastVisible(false)
    if (notificationToastCloseTimerRef.current !== null) window.clearTimeout(notificationToastCloseTimerRef.current)
    notificationToastCloseTimerRef.current = window.setTimeout(() => {
      setNotificationToast(null)
      notificationToastCloseTimerRef.current = null
    }, 160)
  }
  useEffect(() => {
    const items = radarNotifications.data?.attentionNow ?? []
    if (!activePortfolio || !radarNotifications.data) return
    const storageKey = `prumo.radar-notifications.${activePortfolio.id}`
    let notified = new Set<string>()
    try {
      const saved = JSON.parse(localStorage.getItem(storageKey) ?? '[]')
      if (Array.isArray(saved)) notified = new Set(saved.filter((value): value is string => typeof value === 'string'))
    } catch {
      notified = new Set()
    }
    // The assessment is versioned again when the portfolio or its target
    // allocation changes. The document is the user-facing notification unit,
    // so those technical replacements must not produce a new toast.
    const next = items.find(item => !notified.has(item.documentAnalysisId))
    if (!next) return
    notified.add(next.documentAnalysisId)
    try {
      localStorage.setItem(storageKey, JSON.stringify(Array.from(notified).slice(-200)))
    } catch {
      // The persistent Radar state remains authoritative when storage is unavailable.
    }
    const showTimeout = window.setTimeout(() => {
      if (notificationToastCloseTimerRef.current !== null) window.clearTimeout(notificationToastCloseTimerRef.current)
      setNotificationToast(next)
      setNotificationToastVisible(true)
    }, 0)
    const hideTimeout = window.setTimeout(dismissNotificationToast, 6000)
    return () => {
      window.clearTimeout(showTimeout)
      window.clearTimeout(hideTimeout)
    }
  }, [activePortfolio, radarNotifications.data])
  const reprocessFailed = useMutation({
    mutationFn: () => sendJson<{ requeuedCount: number }, { errorContains: string | null }>(`/api/portfolios/${activePortfolio!.id}/documents/reprocess-failed`, 'POST', { errorContains: null }),
    onSuccess: async () => { if (activePortfolio?.id) { setFailedNoticeOverride({ portfolioId: activePortfolio.id, dismissed: false }); localStorage.removeItem(`prumo.failed-notice.${activePortfolio.id}`) } await queryClient.invalidateQueries({ queryKey: ['document-processing-status'] }); await queryClient.invalidateQueries({ queryKey: ['documents'] }) },
    onError: (error: Error) => setFeedback({ tone: 'error', message: error.message }),
  })

  const selectedAsset = dashboard.data?.positions.find((position) => position.assetId === selectedAssetId) ?? null
  const selectedNotificationAssessment: DashboardAssessment | null = selectedNotification ? {
    assessmentId: selectedNotification.assessmentId,
    documentId: selectedNotification.documentId ?? null,
    asset: selectedNotification.affectedAssets[0] ?? 'Carteira',
    eventSummary: selectedNotification.eventSummary || selectedNotification.summary,
    whyItMatters: selectedNotification.whyItMatters,
    suggestedBehavior: selectedNotification.suggestedBehavior,
    occurredAt: selectedNotification.occurredAt,
    attentionPriority: typeof selectedNotification.attentionPriority === 'number' ? selectedNotification.attentionPriority : 0,
    actionRequirement: selectedNotification.actionRequirement,
    capitalDecisionImpact: selectedNotification.capitalDecisionImpact,
    status: selectedNotification.status,
    userState: selectedNotification.userState,
    documentImportance: selectedNotification.documentImportance,
  } : null
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
    mutationFn: (portfolioId: string) => sendVoid(`/api/portfolios/${portfolioId}`),
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
    mutationFn: (positionId: string) => sendVoid(`/api/portfolios/${activePortfolio!.id}/positions/${positionId}`),
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

    const request: CreatePositionRequest = {
      ticker: positionForm.ticker.trim(),
      name: positionForm.ticker.trim(),
      assetType: positionForm.assetType,
      sector: null,
      quantity,
      averagePrice,
      currentPrice,
    }
    setPurchaseConfirmation({ kind: 'create', request, ticker: request.ticker })
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

    const request: UpdatePositionRequest = { quantity, averagePrice, currentPrice }
    const previousQuantity = dashboard.data?.positions.find(position => position.id === positionId)?.quantity ?? quantity
    if (quantity > previousQuantity) {
      setPurchaseConfirmation({ kind: 'update', positionId, request, ticker: dashboard.data?.positions.find(position => position.id === positionId)?.ticker ?? 'ativo', addedQuantity: quantity - previousQuantity })
      return
    }
    updatePosition.mutate({ positionId, request })
  }

  function finishPurchaseConfirmation(registerPurchase: boolean, values?: Record<string, string>) {
    if (!purchaseConfirmation) return
    const unitPrice = values ? parseOptionalNumber(values.unitPrice) : null
    if (registerPurchase && (unitPrice === null || Number.isNaN(unitPrice) || unitPrice <= 0)) {
      setFeedback({ tone: 'error', message: 'Informe um preço unitário de compra positivo.' })
      return
    }
    if (purchaseConfirmation.kind === 'create') {
      createPosition.mutate({ ...purchaseConfirmation.request, averagePrice: registerPurchase ? unitPrice! : 0, registerPurchase })
    } else {
      updatePosition.mutate({ positionId: purchaseConfirmation.positionId, request: { ...purchaseConfirmation.request, averagePrice: registerPurchase ? unitPrice! : purchaseConfirmation.request.averagePrice, registerPurchase } })
    }
    setPurchaseConfirmation(null)
  }

  return (
      <main className={`flex min-h-screen flex-col bg-stone-50 text-slate-900 ${activePortfolio ? '' : 'no-portfolio'}`}>
        <nav className="sticky top-0 z-20 border-b border-slate-200 bg-stone-50/95 px-4 py-4 backdrop-blur md:px-6">
          <div className="mx-auto flex max-w-358 flex-wrap items-center gap-4">
            <Link to="/" className="mr-auto inline-flex items-center gap-0 text-xl font-semibold tracking-tight">prumo<span className="text-amber-600">.</span></Link>
            <div className="nav-scroll order-3 flex w-full gap-5 overflow-x-auto pt-2 text-sm md:order-2 md:w-auto md:pt-2"><Link to="/" aria-current={route === '/' ? 'page' : undefined} className={navigationLinkClass(route === '/')}>Dashboard</Link><Link to="/portfolio" aria-current={route === '/portfolio' ? 'page' : undefined} className={navigationLinkClass(route === '/portfolio')}>Carteira</Link><Link to="/radar" aria-current={route === '/radar' ? 'page' : undefined} className={navigationLinkClass(route === '/radar')}>Radar</Link><Link to="/contributions" aria-current={route === '/contributions' ? 'page' : undefined} className={navigationLinkClass(route === '/contributions')}>Aporte</Link><Link to="/documents" aria-current={route === '/documents' ? 'page' : undefined} className={navigationLinkClass(route === '/documents')}>Documentos</Link></div>
            <div className="order-2 flex items-center gap-2 md:order-3">{importProcessing && <span role="status" className="rounded-full bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-800">Importando carteira...</span>}{processingStatus.data && processingStatus.data.failedCount > 0 && !failedNoticeDismissed && <div role="status" className="flex items-center gap-2 rounded-full bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-800"><span>{processingStatus.data.failedCount} documento(s) com erro</span><button type="button" onClick={() => reprocessFailed.mutate()} disabled={reprocessFailed.isPending} className="underline">{reprocessFailed.isPending ? 'Reprocessando...' : 'Reprocessar'}</button><button type="button" aria-label="Marcar aviso como visto" onClick={() => { if (activePortfolio?.id) { setFailedNoticeOverride({ portfolioId: activePortfolio.id, dismissed: true }); localStorage.setItem(`prumo.failed-notice.${activePortfolio.id}`, '1') } }} className="text-base leading-none">×</button></div>}{processingStatus.data && processingStatus.data.status !== 'Completed' && processingStatus.data.status !== 'Failed' && <span role="status" className="rounded-full bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-800">{processingStatus.data.status === 'Processing' ? 'Analisando seus documentos...' : 'Seus documentos serão analisados em instantes.'}</span>}</div>
            {activePortfolio && <div ref={notificationPanelRef} className="relative order-2 md:order-3">
              <button type="button" aria-label={radarNotifications.data?.unread ? `${radarNotifications.data.unread} alertas não vistas` : 'Notificações do Radar'} aria-expanded={notificationPanelOpen} title="Notificações do Radar" onClick={toggleNotificationPanel} className={`relative inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-xl border bg-white text-slate-700 transition hover:border-amber-400 hover:text-amber-700 ${radarNotifications.data?.unread ? 'border-amber-400' : 'border-slate-300'}`}><FontAwesomeIcon icon={faBell} />{Boolean(radarNotifications.data?.unread) && <span aria-hidden="true" className="absolute -right-1 -top-1 flex min-h-4 min-w-4 items-center justify-center rounded-full bg-rose-600 px-1 text-[10px] font-bold leading-4 text-white">{(radarNotifications.data?.unread ?? 0) > 99 ? '99+' : radarNotifications.data?.unread}</span>}</button>
              {notificationPanelMounted && <div className={`absolute right-0 top-11 z-50 w-[min(24rem,calc(100vw-2rem))] max-w-[calc(100vw-1rem)] overflow-hidden rounded-2xl border border-slate-200 bg-white text-left shadow-xl max-sm:fixed max-sm:inset-x-2 max-sm:top-16 max-sm:w-auto ${notificationPanelOpen ? 'notification-panel-enter' : 'notification-panel-exit'}`}>
                <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-3"><div><p className="text-sm font-semibold text-slate-900">Notificações</p></div><div className="flex shrink-0 items-center gap-2"><button type="button" aria-label="Marcar todos os itens como lidos" title="Marcar todos como lidos" disabled={markAllNotificationsSeen.isPending || (radarNotifications.data?.unread ?? 0) === 0} onClick={() => markAllNotificationsSeen.mutate()} className="inline-flex items-center gap-1 rounded-full border border-slate-200 bg-stone-50 px-2.5 py-1 text-[11px] font-semibold text-slate-600 shadow-sm transition hover:border-amber-300 hover:bg-amber-50 hover:text-amber-800 disabled:cursor-not-allowed disabled:opacity-50"><FontAwesomeIcon icon={faCheckDouble} className="text-[9px]" />{markAllNotificationsSeen.isPending ? 'Lendo...' : 'Ler todos'}</button><Link to="/radar" onClick={closeNotificationPanel} className="rounded-full px-2 py-1.5 text-xs font-semibold text-amber-700 transition hover:bg-amber-50 hover:text-amber-800">Ver Radar</Link></div></div>
                {(radarNotifications.data?.attentionNow.length ?? 0) === 0 && <p className="px-4 py-6 text-sm text-slate-500">Nenhuma nova atenção no Radar.</p>}
                <div className="max-h-80 overflow-y-auto">{radarNotifications.data?.attentionNow.map(item => <button key={item.assessmentId} type="button" onClick={() => openNotification(item)} className="block w-full border-b border-slate-100 px-4 py-3 text-left transition hover:bg-stone-50 last:border-b-0"><p className="text-xs font-bold uppercase tracking-wide text-amber-700">{item.affectedAssets[0] ?? 'Carteira'}</p><p className="mt-1 text-sm font-semibold text-slate-900">{item.eventSummary || item.summary}</p><p className="mt-1 line-clamp-2 text-xs leading-5 text-slate-600">{item.summary}</p></button>)}</div>
              </div>}
            </div>}
            <button type="button" aria-label={themePreference === 'system' ? 'Personalizar tema' : 'Seguir tema do sistema'} title={themePreference === 'system' ? `Tema do sistema: ${darkMode ? 'dark' : 'claro'}` : 'Seguir tema do sistema'} onClick={() => setThemePreference(current => current === 'system' ? (darkMode ? 'light' : 'dark') : 'system')} className="theme-toggle order-2 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-xl border border-slate-300 bg-white text-slate-700 transition hover:border-amber-400 hover:text-amber-700 md:order-3"><FontAwesomeIcon icon={darkMode ? faSun : faMoon} /></button>
          </div>
        </nav>
        {notificationToast && <div role="status" className={`fixed right-4 top-4 z-50 w-[min(24rem,calc(100vw-2rem))] rounded-2xl border border-amber-300 bg-amber-50 p-4 text-amber-950 shadow-lg sm:right-6 sm:top-6 ${notificationToastVisible ? 'notification-toast-enter' : 'notification-toast-exit'}`}><div className="flex items-start gap-3"><FontAwesomeIcon icon={faBell} className="mt-1 text-amber-700" /><div className="min-w-0 flex-1"><p className="text-sm font-bold">Novo item no Radar</p><p className="mt-1 text-sm">{notificationToast.eventSummary || notificationToast.summary}</p><Link to="/radar" onClick={dismissNotificationToast} className="mt-2 inline-block text-xs font-bold underline">Abrir Radar</Link></div><button type="button" aria-label="Fechar notificação" onClick={dismissNotificationToast} className="text-lg leading-none text-amber-800">×</button></div></div>}
        <Suspense fallback={<p role="status" className="mx-auto max-w-6xl p-6 text-slate-500">Carregando tela…</p>}>
        {selectedNotificationAssessment && <RecentAnalysisModal item={selectedNotificationAssessment} portfolioId={activePortfolio?.id ?? ''} isSeen={selectedNotificationAssessment.userState === 1} isResolved={selectedNotificationAssessment.status === 3 || selectedNotificationAssessment.status === 'Closed'} onClose={() => setSelectedNotification(null)} onResolve={() => resolveNotification.mutate(selectedNotificationAssessment.assessmentId)} actionPending={resolveNotification.isPending} />}
        {route === '/dashboard' && <Navigate to="/" replace />}
        {route === '/' && <DashboardOverview key={activePortfolio?.id ?? 'empty'} dashboard={dashboard} activePortfolio={activePortfolio} portfoliosLoading={portfolios.isLoading} portfoliosError={portfolios.isError} retryPortfolios={() => void portfolios.refetch()} privacyHidden={privacyHidden} togglePrivacy={() => setPrivacyHidden((current) => !current)} />}
        {route === '/about' && <AboutSection />}
        {route === '/history' && activePortfolio && <><HistorySection portfolioId={activePortfolio.id} positions={dashboard.data?.positions ?? []} assetId={new URLSearchParams(location.search).get('assetId')} page={positionPage} before={historyCursors[positionPage]} onPageInfo={handleHistoryPageInfo} />{!new URLSearchParams(location.search).get('assetId') && <div className="mx-auto max-w-6xl px-4 pb-24 sm:px-6"><Pagination page={positionPage} totalPages={historyTotalPages} onPageChange={nextPage => { if (nextPage === 1 || historyCursors[nextPage] !== undefined) setPositionPage(nextPage) }} /></div>}</>}
        {route === '/contributions' && activePortfolio && <ContributionSection key={activePortfolio.id} portfolioId={activePortfolio.id} positions={dashboard.data?.positions ?? []} />}
        {(route === '/contributions' || route === '/documents' || route.startsWith('/documents/') || route === '/radar' || route === '/history') && !portfolios.isLoading && !portfolios.isError && !activePortfolio && <EmptyPortfolioRoute />}
        <section className={`mx-auto max-w-358 px-6 ${route === '/documents' || route.startsWith('/documents/') || route === '/radar' ? 'pt-0 pb-0' : 'pt-3 pb-12'}`}>
          {feedback && (
            <div className={`mb-6 rounded-2xl border px-4 py-3 text-sm ${feedback.tone === 'success' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-rose-200 bg-rose-50 text-rose-900'}`}>
              {feedback.message}
            </div>
          )}
          {portfolios.isLoading && <p className="text-slate-500">Carregando carteiras...</p>}
          {portfolios.isError && <p className="text-rose-700">Não foi possível conectar ao serviço do Prumo. Tente novamente em alguns instantes.</p>}

          {route === '/portfolio' && portfolios.isLoading && <p className="rounded-2xl bg-white p-6 text-slate-500">Carregando sua carteira...</p>}
          {route === '/portfolio' && !portfolios.isLoading && !portfolios.isError && <div className="grid min-w-0 gap-3 xl:grid-cols-[minmax(0,1.7fr)_minmax(22rem,1fr)] xl:items-start *:min-w-0">
            <PortfolioSection portfolios={portfolios} activePortfolio={activePortfolio} portfolioName={portfolioName} setPortfolioName={setPortfolioName} handleCreatePortfolio={handleCreatePortfolio} createPortfolio={createPortfolio} deletePortfolio={deletePortfolio} renamePortfolio={renamePortfolio} editingPortfolioId={editingPortfolioId} setEditingPortfolioId={setEditingPortfolioId} editingPortfolioName={editingPortfolioName} setEditingPortfolioName={setEditingPortfolioName} setSelectedPortfolioId={setSelectedPortfolioId} setSelectedDocumentId={setSelectedDocumentId} setPositionPage={setPositionPage} setFeedback={setFeedback} privacyHidden={privacyHidden} togglePrivacy={() => setPrivacyHidden((current) => !current)} positionForm={positionForm} setPositionForm={setPositionForm} assetTypeOptions={assetTypeOptions} handleCreatePosition={handleCreatePosition} createPosition={createPosition} dashboard={dashboard} editingPositionId={editingPositionId} startEditing={startEditing} handleUpdatePosition={handleUpdatePosition} editForm={editForm} setEditForm={setEditForm} updatePosition={updatePosition} setEditingPositionId={setEditingPositionId} deletePosition={deletePosition} selectedAsset={selectedAsset} setSelectedAssetId={setSelectedAssetId} />
            {activePortfolio && <div className="target-allocation-frame order-2 self-start xl:col-start-2 xl:row-start-2 [&>section]:px-0! [&>section]:pb-0!"><TargetAllocationSection portfolioId={activePortfolio.id} /></div>}
            {activePortfolio && <div className="order-2 self-start rounded-[1.75rem] border border-amber-200 bg-amber-50/40 xl:col-start-2 xl:row-start-1 [&>section]:px-0! [&>section]:pb-0!"><B3ImportSection portfolioId={activePortfolio.id} onImportStateChange={setImportProcessing} /></div>}
          </div>}
        </section>
        {purchaseConfirmation && <AppDialog
          title={`Registrar ${purchaseConfirmation.kind === 'create' ? 'a inclusão' : 'o aumento'} de ${purchaseConfirmation.ticker} como compra?`}
          description={purchaseConfirmation.kind === 'create' ? 'Se confirmar, essa posição também será registrada no histórico como um aporte de compra.' : `A quantidade aumentou em ${purchaseConfirmation.addedQuantity}. Se confirmar, esse acréscimo será registrado no histórico como um aporte de compra.`}
          fields={[{ name: 'unitPrice', label: 'Preço unitário de compra', defaultValue: purchaseConfirmation.kind === 'create' ? positionForm.currentPrice : purchaseConfirmation.request.averagePrice.toString(), inputMode: 'decimal', decimalPlaces: 2 }]}
          confirmLabel="Sim, registrar compra"
          cancelLabel="Não, apenas salvar"
          onClose={() => setPurchaseConfirmation(null)}
          onCancel={() => finishPurchaseConfirmation(false)}
          onConfirm={values => finishPurchaseConfirmation(true, values)}
        />}
        {route === '/radar' && activePortfolio && <div className="mx-auto grid w-full max-w-358 gap-8 px-6 pb-24 pt-3"><RadarSection portfolioId={activePortfolio.id} /></div>}
        {(route === '/documents' || route.startsWith('/documents/')) && activePortfolio && <div className="mx-auto w-full max-w-358 px-6 pb-24 pt-3"><DocumentsSection key={activePortfolio.id} portfolioId={activePortfolio.id} selectedDocumentId={selectedDocumentId} setSelectedDocumentId={setSelectedDocumentId} /></div>}
        <footer className="app-footer mt-auto h-14 border-t border-slate-200 bg-white/70 px-6 py-0">
          <div className="mx-auto flex h-full max-w-6xl items-center justify-between gap-3 text-sm text-slate-500">
            <p>prumo<span className="text-amber-600">.</span></p>
            <Link to="/about" aria-current={route === '/about' ? 'page' : undefined} className="font-semibold text-slate-700 transition hover:text-amber-700">Sobre</Link>
          </div>
        </footer>
        {showBackToTop && <button type="button" aria-label="Voltar ao topo" title="Voltar ao topo" onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })} className="fixed bottom-16 right-5 z-30 h-11 w-11 rounded-full bg-slate-900 text-sm font-semibold text-white shadow-lg transition hover:bg-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500 sm:bottom-5"><FontAwesomeIcon icon={faArrowUp} /></button>}
        </Suspense>
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

function EmptyPortfolioRoute() {
  return <div className="mx-auto max-w-3xl px-6 pb-24 pt-12"><div className="rounded-[1.75rem] border border-slate-200 bg-white p-8 text-center shadow-sm"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiro passo</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Crie uma carteira para continuar.</h1><p className="mx-auto mt-3 max-w-xl text-sm leading-6 text-slate-600">Aportes e documentos ficam disponíveis depois que uma carteira for criada.</p><Link to="/portfolio" className="mt-6 inline-flex rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Criar carteira</Link></div></div>
}

export default App
