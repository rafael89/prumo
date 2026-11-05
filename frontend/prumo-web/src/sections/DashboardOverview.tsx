import { LoadingState } from '../components/LoadingState'
import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowDown, faArrowRight, faArrowUpRightFromSquare, faCircleCheck, faCircleExclamation, faCircleInfo, faClock, faTriangleExclamation, faRotate, faXmark } from '@fortawesome/free-solid-svg-icons'

import { apiBaseUrl, sendJson } from '../api/client'
import { analysisTone } from '../utils/documentAnalysis'
import { DocumentAnalysisContent } from '../components/DocumentAnalysisContent'
import { MonthSwitcher } from '../components/MonthSwitcher'
const contributionApiUrl = apiBaseUrl

type Position = {
  id: string
  assetId: string
  assetClass: number
  ticker: string
  name: string
  assetType: 0 | 1 | 2 | 3
  sector: string | null
  quantity: number
  averagePrice: number
  currentPrice: number | null
  currentValue: number | null
  portfolioWeight: number
}

export type DashboardAssessment = {
  assessmentId: string
  documentId: string | null
  asset: string
  eventSummary: string
  whyItMatters: string
  suggestedBehavior: string
  occurredAt: string | null
  attentionPriority: number
  actionRequirement: number
  capitalDecisionImpact: number
  status: number | string
  documentImportance?: string
  events?: Array<{ investmentEventId: string; eventType: number; occurredAt: string | null; summary: string }>
  hasRadarAssessment?: boolean
}

type RecentDocumentAnalysis = {
  importance: string
  classification: string
  summary: string
  impact: string
  portfolioImpact: string
  portfolioAssessment: { priority: string; currentWeight: number; currentValue: number | null; requiresAttention: boolean; portfolioImpactText: string } | null
  suggestedAction: string
  requiresAction: boolean
  confidence: number
  model: string
  promptVersion: string
  affectedAssets: string[]
  createdAt: string
  publishedAt: string
}

type RecentDocument = {
  sourceUrl: string
  documentType: number
  sourceType: string | null
}

const documentTypeLabels = ['Fato relevante', 'Comunicado', 'Relatório gerencial', 'Informe mensal', 'Informe trimestral', 'Outro']

function documentTypeLabel(document: RecentDocument) {
  if (document.documentType === 5 && document.sourceType) return document.sourceType
  return documentTypeLabels[document.documentType] ?? document.sourceType ?? 'Documento'
}

export type Dashboard = {
  id: string
  name: string
  totalValue: number
  positionCount: number
  positions: Position[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
  state?: number | string
  portfolioOverview?: {
    currentValue: number
    healthStatus: string
    healthSummary: string
    mainDeviationSummary: string | null
    lastPortfolioUpdate: string | null
  }
  attentionNow?: DashboardAssessment[]
  contributionPreview?: { status: number | string; primaryCapitalNeed: number | null; summary: string }
  portfolioHealthSummary?: {
    status: string
    summary: string
    allocationDeviations: Array<{ assetClass: number; currentPercentage: number; targetPercentage: number; deviationPercentagePoints: number }>
    mainDeviation: { assetClass: number; currentPercentage: number; targetPercentage: number; deviationPercentagePoints: number } | null
  }
  recentUpdates?: DashboardAssessment[]
}

export type DashboardGuidance = {
  guidanceId?: string
  jobStatus?: string
  status: number | string
  allocationStyle: number | string
  contributionAmount: number
  allocatedAmount: number
  allocations: Array<{ assetId: string; ticker: string; amount: number; reason: string }>
  avoidedCandidates: Array<{ assetId: string; ticker: string; reasonType: number; reason: string }>
  unallocatedAmount: number
  reasons: string[]
}

type DashboardOverviewProps = {
  dashboard: { data?: Dashboard; isLoading: boolean; isError: boolean }
  activePortfolio: { id: string; positionCount: number } | null
  portfoliosLoading?: boolean
  portfoliosError?: boolean
  retryPortfolios?: () => void
  privacyHidden?: boolean
}

const assetClassLabels: Record<number, string> = { 0: 'Ações', 1: 'FIIs', 2: 'Renda fixa', 3: 'ETF', 4: 'Outros ativos' }
const priorityLabels = ['Baixa', 'Média', 'Alta', 'Crítica']

function officialDocumentUrl(url: string) {
  return url.replace('/fnet/publico/downloadDocumento', '/fnet/publico/exibirDocumento')
}

function formatPercentage(value: number) {
  return new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value)
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium' }).format(new Date(value))
}

function formatRelativeDate(value: string | null) {
  if (!value) return 'Sem data informada'
  const date = new Date(value)
  const elapsedDays = Math.floor((Date.now() - date.getTime()) / 86_400_000)
  if (elapsedDays <= 0) return 'Hoje'
  if (elapsedDays === 1) return 'Ontem'
  if (elapsedDays < 7) return `${elapsedDays} dias atrás`
  return new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: 'short' }).format(date)
}

function RadarPriorityIcon({ priority }: { priority: number }) {
  const level = priority >= 3 ? 3 : priority === 2 ? 2 : priority === 1 ? 1 : 0
  const icons = [faCircleInfo, faClock, faTriangleExclamation, faCircleExclamation]
  const tones = ['level-low', 'level-medium', 'level-high', 'level-critical']
  const label = `Prioridade: ${priorityLabels[level]}`
  return <span role="img" aria-label={label} title={label} className={`radar-priority-icon inline-flex h-7 w-7 shrink-0 items-center justify-center text-lg ${tones[level]}`}><FontAwesomeIcon icon={icons[level]} aria-hidden="true" /></span>
}

function canManageSituation() {
  // Any Radar assessment can be closed or reopened. Attention is derived from
  // the complete assessment (priority, action and capital impact), so using
  // only CapitalDecisionImpact here made the action disappear for valid items.
  return true
}

function RadarCard({ items, portfolioId }: { items: DashboardAssessment[]; portfolioId: string }) {
  const [selectedItem, setSelectedItem] = useState<DashboardAssessment | null>(null)
  const [visibleCount, setVisibleCount] = useState(3)
  const queryClient = useQueryClient()
  const statusMutation = useMutation({ mutationFn: (id: string) => fetch(`${contributionApiUrl}/api/radar/${id}/status?portfolioId=${portfolioId}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ status: 'Closed' }) }).then(response => { if (!response.ok) throw new Error('Não foi possível atualizar o Radar.'); return response }), onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ['dashboard', portfolioId] }); void queryClient.invalidateQueries({ queryKey: ['radar', portfolioId] }); setSelectedItem(null) } })
  return <article className="order-2 min-w-0 self-start rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Radar</p>
    {items.length === 0 ? <div className="mt-4 rounded-2xl bg-emerald-50 p-4 text-emerald-950">
      <div className="flex items-start gap-3"><FontAwesomeIcon icon={faCircleCheck} className="mt-1 text-emerald-600" /><div><strong>Tudo tranquilo por aqui.</strong><p className="mt-1 text-sm leading-6 text-emerald-800">Nenhum acontecimento recente exige sua atenção.</p></div></div>
    </div> : <div className="mt-4 divide-y divide-slate-100">
      {items.slice(0, visibleCount).map(item => item.documentId ? <div key={item.assessmentId} role="button" tabIndex={0} onClick={() => setSelectedItem(item)} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setSelectedItem(item) } }} className="flex w-full min-w-0 items-center gap-2 py-3 text-left first:pt-0 transition hover:bg-stone-50">
        <div className="shrink-0"><RadarPriorityIcon priority={item.attentionPriority} /></div>
        <div className="min-w-0 flex-1">
          <h3 className="line-clamp-2 text-sm font-semibold leading-5 text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}{(item.events?.length ?? 0) > 1 && <span className="ml-2 text-xs font-medium text-slate-500">· {item.events!.length} acontecimentos</span>}</h3>
        </div>
        {formatRelativeDate(item.occurredAt) && <span className="shrink-0 text-xs text-slate-500">{formatRelativeDate(item.occurredAt)}</span>}
      </div> : <div key={item.assessmentId} className="grid min-w-0 grid-cols-2 gap-3 py-3 first:pt-0 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:items-start">
        <div className="order-1 flex flex-wrap gap-1.5 sm:order-1"><RadarPriorityIcon priority={item.attentionPriority} /></div><div className="order-3 col-span-2 min-w-0 sm:order-2 sm:col-span-1"><h3 className="text-sm font-semibold text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}</h3></div>{formatRelativeDate(item.occurredAt) && <span className="order-2 justify-self-end text-xs text-slate-500 sm:order-3 sm:text-right">{formatRelativeDate(item.occurredAt)}</span>}
      </div>)}
    </div>}{items.length > visibleCount && <button type="button" onClick={() => setVisibleCount(current => Math.min(current + 3, items.length))} className="mt-4 inline-flex cursor-pointer items-center gap-1.5 font-semibold text-sky-700 hover:text-sky-900"><span className="text-[11px] leading-none">Mostrar mais</span> <FontAwesomeIcon icon={faArrowDown} className="text-[0.55rem]" /></button>}{selectedItem && <RecentAnalysisModal item={selectedItem} portfolioId={portfolioId} isResolved={selectedItem.status === 3 || selectedItem.status === 'Closed'} onClose={() => setSelectedItem(null)} onResolve={() => statusMutation.mutate(selectedItem.assessmentId)} actionPending={statusMutation.isPending} />}
  </article>
}

export function RecentAnalysisModal({ item, portfolioId, isResolved = false, onClose, onResolve, onReopen, actionPending = false }: { item: DashboardAssessment; portfolioId: string; isResolved?: boolean; onClose: () => void; onResolve?: () => void; onReopen?: () => void; actionPending?: boolean }) {
  const queryClient = useQueryClient()
  const readAttemptedFor = useRef<string | null>(null)
  const { mutate: markRead, isError: markReadFailed } = useMutation({
    mutationFn: ({ assessmentId, portfolioId: id }: { assessmentId: string; portfolioId: string }) => sendJson<unknown, object>(`/api/radar/${assessmentId}/dismiss-notification?portfolioId=${id}`, 'PATCH', {}),
    onSuccess: (_, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['radar-notifications', variables.portfolioId] })
      void queryClient.invalidateQueries({ queryKey: ['radar', variables.portfolioId] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard', variables.portfolioId] })
    },
  })
  useEffect(() => {
    if (!portfolioId || !item.assessmentId || item.hasRadarAssessment === false) return
    const key = `${portfolioId}:${item.assessmentId}`
    if (readAttemptedFor.current === key) return
    readAttemptedFor.current = key
    markRead({ assessmentId: item.assessmentId, portfolioId })
  }, [item.assessmentId, item.hasRadarAssessment, portfolioId, markRead])

  useEffect(() => {
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.body.style.overflow = previousOverflow }
  }, [])

  const analysis = useQuery({
    queryKey: ['dashboard-document-analysis', item.documentId, portfolioId],
    queryFn: async () => {
      const response = await fetch(`${contributionApiUrl}/api/documents/${item.documentId}/analysis?portfolioId=${portfolioId}`)
      if (response.status === 404) return null
      if (!response.ok) throw new Error('Não foi possível carregar a análise deste documento.')
      return response.json() as Promise<RecentDocumentAnalysis>
    },
    enabled: Boolean(item.documentId),
  })
  const documentDetails = useQuery({
    queryKey: ['dashboard-document', item.documentId],
    queryFn: async () => {
      const response = await fetch(`${contributionApiUrl}/api/documents/${item.documentId}`)
      if (!response.ok) throw new Error('Não foi possível carregar o documento.')
      return response.json() as Promise<RecentDocument>
    },
    enabled: Boolean(item.documentId),
  })
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])
  const hasVisibleAction = canManageSituation() && ((!isResolved && Boolean(onResolve)) || (isResolved && Boolean(onReopen)))

  return <div className="fixed inset-0 z-40 flex items-center justify-center bg-slate-950/50 p-4" role="dialog" aria-modal="true" aria-labelledby="recent-analysis-title" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <div className={`level-card flex max-h-[90vh] w-full max-w-3xl flex-col overflow-hidden rounded-3xl border border-slate-200 bg-white p-5 shadow-2xl sm:p-7 ${analysisTone(analysis.data?.importance ?? item.documentImportance ?? '')}`}>
      <div className="flex items-start justify-between gap-4 border-b border-slate-200 pb-4"><div><p className="text-xs font-bold uppercase tracking-[0.18em] text-amber-700">Resultado da análise</p><div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1"><h2 id="recent-analysis-title" className="text-xl font-semibold text-slate-950">Análise de <span className="text-amber-700">{item.asset}</span></h2>{documentDetails.data?.sourceUrl && <a href={officialDocumentUrl(documentDetails.data.sourceUrl)} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-xs font-normal text-slate-500 transition hover:text-amber-700"><span>{documentTypeLabel(documentDetails.data)}</span><FontAwesomeIcon icon={faArrowUpRightFromSquare} className="text-[10px]" /></a>}</div></div><button type="button" aria-label="Fechar análise" onClick={onClose} className="rounded-full p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-900"><FontAwesomeIcon icon={faXmark} /></button></div>
      <div className="min-h-0 flex-1 overflow-y-auto pr-1">
      {markReadFailed && <p role="alert" className="mt-5 rounded-xl bg-rose-50 p-4 text-sm text-rose-800">Não foi possível marcar o documento como lido. Feche e abra a análise para tentar novamente.</p>}
      {analysis.data && <div className="mb-5 grid grid-cols-2 gap-1 border-b border-slate-200 pb-4 pt-4 text-[11px] leading-4 text-slate-600"><span className="min-w-0"><strong>Publicado:</strong> {formatDate(analysis.data.publishedAt)}</span><span className="min-w-0"><strong>Analisado:</strong> {formatDate(analysis.data.createdAt)}</span></div>}
      {analysis.isLoading && <LoadingState message="Carregando análise…" className="my-4" />}
      {analysis.isError && <p className="mt-5 rounded-xl bg-rose-50 p-4 text-sm text-rose-800">Não foi possível carregar a análise deste documento.</p>}
      {!analysis.isLoading && !analysis.isError && !analysis.data && <p className="mt-5 rounded-xl border border-dashed border-slate-300 bg-stone-50 p-4 text-sm text-slate-600">Nenhuma análise validada foi encontrada para este documento ainda.</p>}
      {analysis.data && <DocumentAnalysisContent analysis={analysis.data} framed={false} />}
      </div>
      {hasVisibleAction && <div data-radar-actions="true" className="mt-6 grid shrink-0 grid-cols-2 gap-2 border-t border-slate-200 bg-white pt-4 sm:flex sm:flex-wrap">
        {canManageSituation() && !isResolved && onResolve && <button type="button" onClick={onResolve} disabled={actionPending} className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 transition hover:border-amber-500 hover:bg-current/10 hover:text-amber-700 disabled:opacity-50"><FontAwesomeIcon icon={faCircleCheck} />Fechar situação</button>}
        {canManageSituation() && isResolved && onReopen && <button type="button" onClick={onReopen} disabled={actionPending} className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 transition hover:border-amber-500 hover:bg-current/10 hover:text-amber-700 disabled:opacity-50"><FontAwesomeIcon icon={faRotate} />Reabrir</button>}
      </div>}
    </div>
  </div>
}

function newsPreview(summary: string) {
  const text = summary.replace(/\s+/g, ' ').trim()
  const sentenceEnd = text.search(/[.!?](?=\s+[A-ZÀ-Ú]|$)/)
  const firstSentence = sentenceEnd >= 0 ? text.slice(0, sentenceEnd + 1) : text
  if (firstSentence.length <= 160) return firstSentence
  const excerpt = firstSentence.slice(0, 159)
  const lastSpace = excerpt.lastIndexOf(' ')
  return `${(lastSpace > 0 ? excerpt.slice(0, lastSpace) : excerpt).replace(/[,;:]+$/, '')}…`
}

function RecentUpdatesCard({ items, portfolioId }: { items: DashboardAssessment[]; portfolioId: string }) {
  const [visibleCount, setVisibleCount] = useState(5)
  const [selectedItem, setSelectedItem] = useState<DashboardAssessment | null>(null)
  return <article className="order-3 min-w-0 rounded-3xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Últimas notícias</p>
    {items.length === 0 ? <p className="mt-5 rounded-2xl bg-stone-50 p-5 text-sm text-slate-500">Nenhum documento recente para consultar.</p> : <div className="mt-4 divide-y divide-slate-100">
      {items.slice(0, visibleCount).map(item => item.documentId ? <button key={item.assessmentId} type="button" onClick={() => setSelectedItem(item)} className="flex w-full min-w-0 items-center gap-2 py-3 text-left first:pt-0 transition hover:bg-stone-50">
        <div className="min-w-0 flex-1"><h3 className="line-clamp-2 sm:line-clamp-1 text-sm font-semibold leading-5 text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{newsPreview(item.eventSummary)}</h3></div>
        {formatRelativeDate(item.occurredAt) && <span className="shrink-0 text-xs text-slate-500">{formatRelativeDate(item.occurredAt)}</span>}
      </button> : <div key={item.assessmentId} className="flex min-w-0 items-center gap-2 py-3 first:pt-0">
        <div className="min-w-0 flex-1"><h3 className="line-clamp-2 sm:line-clamp-1 text-sm font-semibold leading-5 text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{newsPreview(item.eventSummary)}</h3></div>
        {formatRelativeDate(item.occurredAt) && <span className="shrink-0 text-xs text-slate-500">{formatRelativeDate(item.occurredAt)}</span>}
      </div>)}
    </div>}{items.length > visibleCount && <button type="button" onClick={() => setVisibleCount(current => Math.min(current + 3, items.length))} className="mt-4 inline-flex cursor-pointer items-center gap-1.5 font-semibold text-sky-700 hover:text-sky-900"><span className="text-[11px] leading-none">Mostrar mais</span> <FontAwesomeIcon icon={faArrowDown} className="text-[0.55rem]" /></button>}{selectedItem && <RecentAnalysisModal item={selectedItem} portfolioId={portfolioId} onClose={() => setSelectedItem(null)} />}
  </article>
}

type DividendForecast = {
  estimatedMonthlyAmount: number
  referenceMonth: string
  items: Array<{ assetId: string; ticker: string; estimatedMonthlyAmount: number | null; source: string; documentUrl: string | null; referenceDate: string | null; paymentDate: string | null }>
}

function DividendPaymentDate({ value }: { value: string }) {
  const [today] = useState(() => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date()))
  const daysUntilPayment = Math.round((Date.parse(value) - Date.parse(today)) / 86_400_000)
  const upcoming = daysUntilPayment >= 0 && daysUntilPayment <= 7
  const label = new Intl.DateTimeFormat('pt-BR').format(new Date(`${value}T12:00:00`))
  const status = daysUntilPayment < 0 ? 'Data de pagamento já passou' : daysUntilPayment === 0 ? 'Pagamento previsto para hoje' : upcoming ? 'Pagamento previsto nos próximos 7 dias' : 'Pagamento futuro'
  return <time dateTime={value} title={status} aria-label={`${label}: ${status}`} className={`inline-flex items-center gap-1 whitespace-nowrap ${upcoming ? 'font-semibold text-emerald-700' : daysUntilPayment < 0 ? 'text-slate-500' : 'text-slate-700'}`}>
    {upcoming && <FontAwesomeIcon icon={faClock} className="text-[10px]" aria-hidden="true" />}{label}
  </time>
}

function DividendForecastWidget({ portfolioId, privacyHidden }: { portfolioId: string; privacyHidden: boolean }) {
  const initialVisibleCount = 5
  const [selectedMonth, setSelectedMonth] = useState<string | null>(null)
  const [allDividendsVisible, setAllDividendsVisible] = useState(false)
  const forecast = useQuery({
    queryKey: ['dividend-forecast', portfolioId, selectedMonth],
    queryFn: async () => {
      const response = await fetch(`${apiBaseUrl}/api/portfolios/${portfolioId}/dividend-forecast${selectedMonth ? `?month=${selectedMonth}` : ''}`)
      if (!response.ok) throw new Error('Não foi possível carregar a previsão de rendimentos.')
      return response.json() as Promise<DividendForecast>
    },
  })
  const currency = (amount: number) => privacyHidden ? 'R$ ••••••' : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(amount)
  const orderedItems = (forecast.data?.items ?? [])
    .filter(item => item.paymentDate !== null && item.estimatedMonthlyAmount !== null)
    .sort((left, right) =>
      (left.paymentDate ?? '9999-12-31').localeCompare(right.paymentDate ?? '9999-12-31')
      || left.ticker.localeCompare(right.ticker, 'pt-BR', { sensitivity: 'base' }))
  const referenceMonth = selectedMonth ?? forecast.data?.referenceMonth
  const total = currency(forecast.data?.estimatedMonthlyAmount ?? 0)
  return <div className="relative order-1 min-w-0 lg:self-stretch">
    <article className="dividend-forecast flex min-h-0 min-w-0 flex-col overflow-hidden rounded-3xl border border-slate-200 bg-white p-4 shadow-sm lg:absolute lg:inset-0 lg:py-2">
    <div className="flex shrink-0 flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
      <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Rendimentos</p>
      {referenceMonth && <MonthSwitcher month={referenceMonth} onMonthChange={setSelectedMonth} />}
    </div>
    <div role="region" aria-label="Lista de rendimentos" tabIndex={0} className="dividend-forecast-scroll min-h-0 lg:flex-1 lg:overflow-auto lg:overscroll-contain lg:pr-2">
    {forecast.isLoading ? <LoadingState message="Calculando previsão…" className="mt-4" /> : forecast.isError ? <p className="mt-4 text-sm text-rose-700">Não foi possível carregar a previsão.</p> : orderedItems.length ? <>
      <div className="mt-5 divide-y divide-slate-200 sm:hidden">
        {orderedItems.map((item, index) => <div key={item.assetId} className={`grid min-w-0 grid-cols-[1fr_auto_1fr] items-center gap-2 py-2 ${index >= initialVisibleCount && !allDividendsVisible ? 'hidden' : ''}`}>
          {item.documentUrl ? <a href={officialDocumentUrl(item.documentUrl)} target="_blank" rel="noreferrer" className="min-w-0 truncate text-sm font-bold text-slate-900 hover:underline" aria-label={`${item.source || 'Documento'} de ${item.ticker} (abre em nova aba)`}>{item.ticker}</a> : <strong className="min-w-0 truncate text-sm text-slate-900">{item.ticker}</strong>}
          <span className="text-xs text-slate-600"><DividendPaymentDate value={item.paymentDate!} /></span>
          <strong className="whitespace-nowrap text-right text-sm text-slate-900">{currency(item.estimatedMonthlyAmount!)}</strong>
        </div>)}
      </div>
      <div className="hidden sm:block"><table className="w-full table-fixed border-collapse text-[13px] leading-[18px]"><colgroup><col style={{ width: '17%' }} /><col style={{ width: '25%' }} /><col style={{ width: '38%' }} /><col style={{ width: '20%' }} /></colgroup><thead className="sticky top-0 z-10 bg-white"><tr className="border-b border-slate-200 text-left text-[11px] leading-4 font-semibold uppercase tracking-wide text-slate-500"><th scope="col" className="pb-1.5 pr-3 pt-2">Ativo</th><th scope="col" className="whitespace-nowrap pb-1.5 pr-3 pt-2">Pagamento</th><th scope="col" className="pb-1.5 pr-3 pt-2">Documento</th><th scope="col" className="pb-1.5 pt-2 text-right">Valor</th></tr></thead><tbody>{orderedItems.map(item => <tr key={item.assetId} className="border-b border-slate-100"><td className="py-px pr-3"><strong className="text-slate-900">{item.ticker}</strong></td><td className="whitespace-nowrap py-px pr-3 text-slate-700"><DividendPaymentDate value={item.paymentDate!} /></td><td className="py-px pr-3">{item.documentUrl ? <a href={officialDocumentUrl(item.documentUrl)} target="_blank" rel="noreferrer" title={item.source} aria-label={`${item.source || 'Documento'} de ${item.ticker} (abre em nova aba)`} className="flex min-w-0 items-center gap-1 font-semibold text-sky-700 hover:underline"><span className="truncate">{item.source || 'Documento'}</span></a> : <span className="text-slate-500">—</span>}</td><td className="whitespace-nowrap py-px text-right font-semibold text-slate-900">{currency(item.estimatedMonthlyAmount!)}</td></tr>)}</tbody></table></div>
    </> : <p className="mt-4 text-sm leading-6 text-slate-600">Nenhum pagamento com valor e data informados neste mês.</p>}
    </div>
    {orderedItems.length > initialVisibleCount && <button type="button" aria-expanded={allDividendsVisible} onClick={() => setAllDividendsVisible(current => !current)} className="mt-2 min-h-11 rounded-xl border border-slate-200 px-3 text-sm font-semibold text-slate-700 transition hover:border-amber-400 sm:hidden">{allDividendsVisible ? 'Mostrar menos' : `Ver todos os ${orderedItems.length} rendimentos`}</button>}
    <footer className="mt-1 flex shrink-0 items-baseline justify-between gap-3 border-t border-slate-200 pt-1 text-slate-950"><strong className="text-sm">Total</strong><strong className="text-lg">{forecast.isLoading || forecast.isError ? '—' : total}</strong></footer>
  </article>
  </div>
}

function PortfolioAllocationCard({ data, privacyHidden = false }: { data: Dashboard; privacyHidden?: boolean }) {
  const deviations = data.portfolioHealthSummary?.allocationDeviations ?? []
  const money = privacyHidden ? 'R$ ••••••' : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(data.totalValue)

  return <article className="order-1 min-w-0 self-start overflow-hidden rounded-[1.75rem] border border-slate-200 bg-white shadow-sm">
    <header className="portfolio-overview-header px-5 py-4 text-white sm:px-6 sm:py-5">
      <div className="flex items-center justify-between gap-4"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-400">Carteira</p><div className="flex items-center gap-3"><span className="hidden text-xs text-[#a9bfdf] sm:inline">{data.portfolioOverview?.lastPortfolioUpdate ? `Carteira atualizada em ${formatDateTime(data.portfolioOverview.lastPortfolioUpdate)}` : ''}</span></div></div>
      <div className="mt-7 grid gap-5 sm:grid-cols-[3fr_1.1fr]"><div><p className="text-sm text-[#8ca9cd]">Patrimônio da carteira</p><p className="mt-2 text-3xl font-bold tracking-tight sm:text-4xl">{money}</p></div><div className="border-t border-slate-700 pt-4 sm:border-l sm:border-t-0 sm:pl-6 sm:pt-0"><p className="text-sm text-[#8ca9cd]">Ativos na carteira</p><p className="mt-2 text-3xl font-bold sm:text-4xl">{privacyHidden ? '••••' : data.positionCount}</p></div></div>
    </header>
    <div className="px-5 py-6 sm:px-6"><div className="flex items-center justify-between gap-4"><h2 className="text-xs font-bold uppercase tracking-[0.2em] text-orange-700">Alocação por classe</h2></div>
      {deviations.length > 0 ? <><div className="mt-4 flex justify-end gap-4 text-xs text-slate-500"><span><i className="mr-1 inline-block h-2 w-2 rounded-full bg-sky-600" />Atual</span><span><i className="mr-1 inline-block h-2 w-2 rounded-full bg-orange-500" />Excesso</span><span><i className="portfolio-allocation-target mr-1 inline-block h-4 w-1 rounded-full align-middle" />Meta</span></div><div className="mt-2 space-y-1">{deviations.map(item => { const current = Math.min(Math.max(item.currentPercentage, 0), 100); const target = Math.min(Math.max(item.targetPercentage, 0), 100); const base = Math.min(current, target); const gap = Math.abs(target - current); const underweight = current < target; return <div key={item.assetClass} className="grid min-h-10 grid-cols-[5.5rem_minmax(0,1fr)] items-center gap-3 sm:grid-cols-[6.5rem_minmax(0,1fr)_7.5rem]"><strong className="text-sm text-slate-900">{assetClassLabels[item.assetClass] ?? 'Outros ativos'}</strong><div className="relative h-3 overflow-visible rounded-full bg-slate-100"><span className="absolute inset-y-0 left-0 bg-sky-600" style={{ width: `${base}%` }} />{gap > 0 && <span className={`absolute inset-y-0 ${underweight ? 'bg-[repeating-linear-gradient(-45deg,#d7e0ea_0,#d7e0ea_3px,#f3f6f9_3px,#f3f6f9_6px)]' : 'bg-orange-500'}`} style={{ left: `${base}%`, width: `${gap}%` }} />}<span aria-label={`Meta de ${formatPercentage(target)}%`} className="portfolio-allocation-target absolute -inset-y-1 z-10 w-1 -translate-x-1/2 rounded-full" style={{ left: `${target}%` }} /></div><div className="col-start-2 flex gap-1 text-xs sm:col-start-3 sm:justify-end sm:text-sm"><strong className="text-slate-900">{formatPercentage(current)}%</strong><span className="text-slate-400">/</span><span className="text-slate-500">{formatPercentage(target)}%</span></div></div> })}</div></> : <div className="mt-5 rounded-2xl bg-stone-50 p-4 text-sm leading-6 text-slate-600">{data.portfolioOverview?.healthSummary ?? 'Configure as alocações-alvo para acompanhar a distribuição da carteira.'}<Link to="/portfolio" className="mt-2 flex w-fit items-center gap-2 font-semibold text-slate-900">Configurar alocação-alvo <FontAwesomeIcon icon={faArrowRight} /></Link></div>}
    </div>
  </article>
}

export function DashboardOverview({ dashboard, activePortfolio, portfoliosLoading = false, portfoliosError = false, retryPortfolios, privacyHidden = false }: DashboardOverviewProps) {
  if (portfoliosLoading) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><LoadingState message="Carregando sua visão geral…" className="my-4" /></section>
  if (portfoliosError) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-rose-200 bg-rose-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-rose-700">Serviço temporariamente indisponível</p><h1 className="mt-3 text-3xl font-semibold tracking-tight text-slate-950">Não conseguimos carregar sua carteira.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">O Prumo está com dificuldade para se conectar ao serviço neste momento. Seus dados não foram apagados. Tente novamente em alguns instantes.</p>{retryPortfolios && <button type="button" onClick={retryPortfolios} className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Tentar novamente</button>}</div></section>
  if (!activePortfolio) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiros passos</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Sua carteira começa aqui.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">Crie uma carteira e importe seu relatório consolidado da B3 para o Prumo entender seus investimentos.</p><Link to="/portfolio" className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Criar carteira <FontAwesomeIcon icon={faArrowRight} /></Link></div></section>
  if (dashboard.isLoading) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><LoadingState message="Carregando sua visão geral…" className="my-4" /></section>
  if (dashboard.isError || !dashboard.data) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-rose-50 p-6 text-rose-800">Não foi possível carregar a visão geral.</p></section>

  const data = dashboard.data
  if (data.positionCount === 0) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiros passos</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Sua carteira começa aqui.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">Importe seu relatório consolidado da B3 para o Prumo entender seus investimentos.</p><Link to="/portfolio" className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Importar carteira <FontAwesomeIcon icon={faArrowRight} /></Link></div></section>

  return <section className="dashboard-overview mx-auto w-full basis-full max-w-358 px-6 pb-12 pt-6">
    <header className="mb-7 flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
      <div><h1 className="text-3xl font-semibold tracking-tight text-slate-950">Visão geral</h1></div>
    </header>
    <div className="grid w-full min-w-0 items-start gap-4 lg:grid-cols-2">
      <PortfolioAllocationCard data={data} privacyHidden={privacyHidden} />
      <DividendForecastWidget key={activePortfolio.id} portfolioId={activePortfolio.id} privacyHidden={privacyHidden} />
      <div className="order-2 min-w-0 lg:col-span-2"><RadarCard items={data.attentionNow ?? []} portfolioId={activePortfolio.id} /></div>
      <div className="order-3 min-w-0 self-start lg:col-span-2"><RecentUpdatesCard items={data.recentUpdates ?? []} portfolioId={activePortfolio.id} /></div>
    </div>
  </section>
}
