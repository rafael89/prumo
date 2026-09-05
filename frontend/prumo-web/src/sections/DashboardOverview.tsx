import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowDown, faArrowRight, faCircleCheck, faCircleInfo, faEye, faTriangleExclamation, faXmark } from '@fortawesome/free-solid-svg-icons'

import { apiBaseUrl } from '../api/client'
import { TransactionReconciliationSection } from './TransactionReconciliationSection'
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
}

const assetClassLabels: Record<number, string> = { 0: 'Ações', 1: 'FIIs', 2: 'Renda fixa', 3: 'ETF', 4: 'Outros ativos' }
const priorityLabels = ['Baixa', 'Média', 'Alta', 'Crítica']
const suggestedActionLabels: Record<string, string> = { Maintain: 'Manter', IncreasePosition: 'Aumentar posição', ReduceExposure: 'Reduzir exposição', EvaluateSale: 'Avaliar venda', Monitor: 'Acompanhar', NoAction: 'Sem ação', OpenPosition: 'Abrir posição' }
const capitalImpactLabels = ['Favorável a aumentar exposição', 'Sem impacto relevante', 'Cautela ao aumentar exposição', 'Evitar aumentar exposição agora']
const importanceLabels: Record<string, string> = { Critical: 'Crítico', Relevant: 'Relevante', Informative: 'Informativo', NoAction: 'Sem ação' }
const classificationLabels: Record<string, string> = { CorporateAction: 'Ação corporativa', FinancialResult: 'Resultado financeiro', Distribution: 'Distribuição', CapitalRaise: 'Captação', AssetTransaction: 'Transação de ativos', Governance: 'Governança', Operational: 'Operacional', Liquidation: 'Liquidação', Other: 'Outro' }

void capitalImpactLabels

function formatPercentage(value: number) {
  return new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value)
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
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

function PortfolioHealthCardLegacy({ data }: { data: Dashboard }) {
  const health = data.portfolioHealthSummary
  const overview = data.portfolioOverview
  const deviations = health?.allocationDeviations.slice(0, 2) ?? []
  const notConfigured = health?.status === 'NotConfigured'
  const aligned = health?.status === 'Aligned'

  return <article className="order-2 min-w-0 self-start rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Saúde da carteira</p>
    <h2 className="mt-4 flex items-center justify-between gap-4 text-xl font-semibold tracking-tight text-slate-950">
      <span>{health?.summary ?? overview?.healthSummary ?? 'Estado da carteira indisponível.'}</span>
      <span aria-hidden="true" className={`inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border text-base shadow-sm ${aligned ? 'border-emerald-200 bg-emerald-50 text-emerald-600' : notConfigured ? 'border-slate-200 bg-slate-100 text-slate-600' : 'border-amber-300 bg-amber-100 text-amber-700 shadow-amber-100'}`}>
        <FontAwesomeIcon icon={aligned ? faCircleCheck : notConfigured ? faCircleInfo : faTriangleExclamation} />
      </span>
    </h2>
    {overview?.mainDeviationSummary && <p className="mt-2 text-sm leading-6 text-slate-600">{overview.mainDeviationSummary}</p>}

    {deviations.length > 0 && <div className="mt-5 divide-y divide-slate-100 border-t border-slate-100">
      {deviations.map(item => <div key={item.assetClass} className="flex flex-wrap items-center justify-between gap-2 py-3 text-sm">
        <strong>{assetClassLabels[item.assetClass] ?? 'Outros ativos'}</strong>
        <span className="text-slate-500">Atual {formatPercentage(item.currentPercentage)}% · alvo {formatPercentage(item.targetPercentage)}%</span>
      </div>)}
    </div>}

    {notConfigured && <div className="mt-5 rounded-2xl bg-stone-50 p-4 text-sm leading-6 text-slate-600">
      Configure as alocações-alvo para que o Prumo possa avaliar a distribuição da carteira e orientar seus aportes.
      <Link to="/portfolio" className="mt-3 flex w-fit items-center gap-2 font-semibold text-slate-900">Configurar alocação-alvo <FontAwesomeIcon icon={faArrowRight} /></Link>
    </div>}
  </article>
}

void PortfolioHealthCardLegacy

function healthScore(status: string | undefined) {
  if (status === 'Aligned') return 100
  if (status === 'SlightlyUnbalanced') return 80
  if (status === 'Unbalanced') return 60
  if (status === 'StronglyUnbalanced') return 35
  return 0
}

function healthLabel(status: string | undefined) {
  if (status === 'Aligned' || status === 'SlightlyUnbalanced') return 'Boa'
  if (status === 'Unbalanced') return 'Atencao'
  if (status === 'StronglyUnbalanced') return 'Desbalanceada'
  if (status === 'EmptyPortfolio') return 'Sem posicoes'
  return 'Nao configurada'
}

function PortfolioHealthCard({ data }: { data: Dashboard }) {
  const health = data.portfolioHealthSummary
  const overview = data.portfolioOverview
  const deviations = [...(health?.allocationDeviations ?? [])]
    .sort((left, right) => Math.abs(right.deviationPercentagePoints) - Math.abs(left.deviationPercentagePoints))
    .slice(0, 3)
  const score = healthScore(health?.status)
  const scoreColor = score >= 80 ? '#f59e0b' : score >= 60 ? '#f97316' : '#ef4444'

  return <article className="order-2 min-w-0 self-start rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Saude da carteira</p>
    <div className="mt-5 grid gap-5 md:grid-cols-[minmax(150px,0.72fr)_minmax(0,1.28fr)] md:gap-7">
      <div className="flex flex-col items-center justify-center border-b border-slate-100 pb-5 md:border-b-0 md:border-r md:pb-0 md:pr-7">
        <div className="relative grid h-32 w-32 place-items-center rounded-full" style={{ background: `conic-gradient(${scoreColor} ${score * 3.6}deg, #f1f5f9 0deg)` }}>
          <div className="grid h-24 w-24 place-items-center rounded-full bg-white text-center">
            <div><strong className="block text-3xl leading-none text-slate-950">{score}</strong><span className="text-xs font-semibold text-slate-500">/100</span></div>
          </div>
        </div>
        <span className={`mt-4 rounded-xl px-6 py-2 text-sm font-semibold ${score >= 80 ? 'bg-amber-50 text-amber-700' : score >= 60 ? 'bg-orange-50 text-orange-700' : score > 0 ? 'bg-rose-50 text-rose-700' : 'bg-slate-100 text-slate-600'}`}>{healthLabel(health?.status)}</span>
      </div>
      <div className="min-w-0">
        <h2 className="text-sm font-semibold text-slate-950">Resumo</h2>
        {deviations.length > 0 ? <div className="mt-3 space-y-3">
          {deviations.map(item => {
            const aligned = Math.abs(item.deviationPercentagePoints) <= 2
            const above = item.deviationPercentagePoints > 0
            return <div key={item.assetClass} className="flex items-start gap-3">
              <span aria-hidden="true" className={`mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-xl ${aligned ? 'bg-emerald-50 text-emerald-600' : above ? 'bg-amber-50 text-amber-600' : 'bg-sky-50 text-sky-600'}`}><FontAwesomeIcon icon={aligned ? faCircleCheck : above ? faTriangleExclamation : faCircleInfo} /></span>
              <div className="min-w-0"><p className="text-sm font-semibold leading-5 text-slate-800">{aligned ? `${assetClassLabels[item.assetClass] ?? 'Classe'} alinhada ao ideal` : `${assetClassLabels[item.assetClass] ?? 'Classe'} ${above ? 'acima' : 'abaixo'} do ideal`}</p><p className="text-xs leading-5 text-slate-500">Atual {formatPercentage(item.currentPercentage)}% · meta {formatPercentage(item.targetPercentage)}%</p></div>
            </div>
          })}
        </div> : <div className="mt-3 flex items-start gap-3"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-xl bg-slate-100 text-slate-600"><FontAwesomeIcon icon={faCircleInfo} /></span><div><p className="text-sm font-semibold text-slate-800">Defina uma alocacao-alvo</p><p className="text-xs leading-5 text-slate-500">{overview?.healthSummary ?? 'Configure sua carteira para acompanhar este indicador.'}</p></div></div>}
      </div>
    </div>
  </article>
}

function priorityClasses(priority: number) {
  if (priority >= 3) return 'bg-rose-100 text-rose-700'
  if (priority === 2) return 'bg-orange-100 text-orange-700'
  if (priority === 1) return 'bg-amber-50 text-amber-700'
  return 'bg-sky-50 text-sky-700'
}

function RadarCard({ items, portfolioId }: { items: DashboardAssessment[]; portfolioId: string }) {
  const [selectedItem, setSelectedItem] = useState<DashboardAssessment | null>(null)
  const [visibleCount, setVisibleCount] = useState(3)
  const queryClient = useQueryClient()
  const statusMutation = useMutation({ mutationFn: ({ id, resolved }: { id: string; resolved: boolean }) => fetch(`${contributionApiUrl}/api/radar/${id}/${resolved ? 'status' : 'seen'}?portfolioId=${portfolioId}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(resolved ? { status: 'Closed' } : { status: 'Seen' }) }).then(response => { if (!response.ok) throw new Error('Não foi possível atualizar o Radar.'); return response }), onSuccess: (_, variables) => { void queryClient.invalidateQueries({ queryKey: ['dashboard', portfolioId] }); void queryClient.invalidateQueries({ queryKey: ['radar', portfolioId] }); if (variables.resolved) setSelectedItem(null) } })
  return <article className="order-1 min-w-0 self-start rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5 lg:col-span-1">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Radar</p>
    {items.length === 0 ? <div className="mt-4 rounded-2xl bg-emerald-50 p-4 text-emerald-950">
      <div className="flex items-start gap-3"><FontAwesomeIcon icon={faCircleCheck} className="mt-1 text-emerald-600" /><div><strong>Tudo tranquilo por aqui.</strong><p className="mt-1 text-sm leading-6 text-emerald-800">Nenhum acontecimento recente exige sua atenção.</p></div></div>
    </div> : <div className="mt-4 divide-y divide-slate-100">
      {items.slice(0, visibleCount).map(item => item.documentId ? <button key={item.assessmentId} type="button" onClick={() => setSelectedItem(item)} className="grid w-full min-w-0 cursor-pointer gap-3 py-3 text-left first:pt-0 transition hover:bg-stone-50 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:items-start">
        <span className={`w-fit rounded-full px-3 py-1.5 text-xs font-semibold ${priorityClasses(item.attentionPriority)}`}>{priorityLabels[item.attentionPriority] ?? 'Baixa'}</span>
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}</h3>
          {item.whyItMatters && <p className="mt-1 text-xs leading-5 text-slate-600">{item.whyItMatters}</p>}
        </div>
        {formatRelativeDate(item.occurredAt) && <span className="text-xs text-slate-500 sm:text-right">{formatRelativeDate(item.occurredAt)}</span>}
      </button> : <div key={item.assessmentId} className="grid min-w-0 gap-3 py-3 first:pt-0 sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:items-start">
        <span className={`w-fit rounded-full px-3 py-1.5 text-xs font-semibold ${priorityClasses(item.attentionPriority)}`}>{priorityLabels[item.attentionPriority] ?? 'Baixa'}</span><div className="min-w-0"><h3 className="text-sm font-semibold text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}</h3>{item.whyItMatters && <p className="mt-1 text-xs leading-5 text-slate-600">{item.whyItMatters}</p>}</div>{formatRelativeDate(item.occurredAt) && <span className="text-xs text-slate-500 sm:text-right">{formatRelativeDate(item.occurredAt)}</span>}
      </div>)}
    </div>}{items.length > visibleCount && <button type="button" onClick={() => setVisibleCount(current => Math.min(current + 3, items.length))} className="mt-4 inline-flex cursor-pointer items-center gap-1.5 text-[0.7rem] font-semibold text-sky-700 hover:text-sky-900">Mostrar mais <FontAwesomeIcon icon={faArrowDown} className="text-[0.65rem]" /></button>}{selectedItem && <RecentAnalysisModal item={selectedItem} portfolioId={portfolioId} onClose={() => setSelectedItem(null)} onMarkSeen={() => statusMutation.mutate({ id: selectedItem.assessmentId, resolved: false })} onResolve={() => statusMutation.mutate({ id: selectedItem.assessmentId, resolved: true })} actionPending={statusMutation.isPending} />}
  </article>
}

export function RecentAnalysisModal({ item, portfolioId, onClose, onMarkSeen, onResolve, actionPending = false }: { item: DashboardAssessment; portfolioId: string; onClose: () => void; onMarkSeen?: () => void; onResolve?: () => void; actionPending?: boolean }) {
  const analysis = useQuery({
    queryKey: ['dashboard-document-analysis', item.documentId, portfolioId],
    queryFn: async () => {
      const response = await fetch(`${contributionApiUrl}/api/documents/${item.documentId}/analysis?portfolioId=${portfolioId}`)
      if (response.status === 404) return null
      if (!response.ok) throw new Error('Não foi possível carregar a análise deste documento.')
      const data = await response.json() as RecentDocumentAnalysis
      return { ...data, importance: importanceLabels[data.importance] ?? data.importance, classification: classificationLabels[data.classification] ?? data.classification, suggestedAction: suggestedActionLabels[data.suggestedAction] ?? data.suggestedAction }
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
  useEffect(() => {
    if (!analysis.data) return
    const dialog = document.querySelector('[aria-labelledby="recent-analysis-title"]')
    const confidenceLabel = Array.from(dialog?.querySelectorAll('p') ?? []).find(element => element.textContent === 'Confiança')
    const confidenceBlock = confidenceLabel?.parentElement?.parentElement
    if (confidenceLabel && !confidenceLabel.querySelector('[data-confidence-tooltip]')) {
      const wrapper = document.createElement('span')
      wrapper.dataset.confidenceTooltip = 'true'
      wrapper.className = 'group relative ml-1 inline-flex align-middle'
      wrapper.tabIndex = 0
      wrapper.setAttribute('aria-label', 'Sobre a confiança da análise')
      wrapper.innerHTML = '<span class="inline-flex h-4 w-4 items-center justify-center rounded-full bg-slate-500 text-[10px] font-bold leading-none text-white">?</span><span role="tooltip" class="pointer-events-none invisible absolute bottom-full left-1/2 z-10 mb-2 w-64 -translate-x-1/2 rounded-xl bg-slate-900 px-3 py-2 text-left text-xs font-normal leading-5 text-white opacity-0 shadow-xl transition group-hover:visible group-hover:opacity-100 group-focus:visible group-focus:opacity-100">A confiança é uma estimativa da IA sobre a interpretação do documento. Não representa uma verificação factual independente.</span>'
      confidenceLabel.appendChild(wrapper)
    }
    const confidenceValue = Array.from(confidenceBlock?.querySelectorAll('p') ?? []).find(element => (element.textContent ?? '').trim().endsWith('%'))
    if (!confidenceValue || confidenceValue.querySelector('[data-analysis-metadata]')) return
    const metadata = document.createElement('span')
    metadata.dataset.analysisMetadata = 'true'
    metadata.className = 'ml-2 text-xs font-normal text-slate-500'
    metadata.append(document.createTextNode(`Modelo: ${analysis.data.model || 'Não informado'} · Prompt: ${analysis.data.promptVersion || 'Não informado'}`))
    confidenceValue.appendChild(metadata)
  }, [analysis.data])
  /*
  useEffect(() => {
    if ((!onMarkSeen && !onResolve) || !analysis.data) return
    const dialog = document.querySelector('[aria-labelledby="recent-analysis-title"]')
    const content = dialog?.firstElementChild!
    if (!content || content.querySelector('[data-radar-actions]')) return
    const actions = document.createElement('div')
    actions.dataset.radarActions = 'true'
    actions.className = 'mt-6 flex flex-wrap gap-2 border-t border-slate-200 pt-5'
    if (onMarkSeen) {
      const seen = document.createElement('button')
      seen.type = 'button'; seen.className = 'cursor-pointer rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white hover:bg-slate-700 disabled:opacity-50'; seen.textContent = 'Marcar como visto'; seen.disabled = actionPending; seen.onclick = onMarkSeen!; actions.appendChild(seen)
    }
    if (onResolve) {
      const resolve = document.createElement('button')
      // @ts-expect-error legacy DOM action builder is disabled; actions render in JSX below.
      resolve.type = 'button'; resolve.className = 'cursor-pointer rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 hover:border-amber-500 hover:text-amber-700 disabled:opacity-50'; resolve.textContent = 'Fechar situação'; resolve.disabled = actionPending; resolve.onclick = onResolve; actions.appendChild(resolve)
    }
    content!.appendChild(actions)
    return () => actions.remove()
  }, [analysis.data, onMarkSeen, onResolve, actionPending])
  */

  return <div className="fixed inset-0 z-40 flex items-center justify-center bg-slate-950/50 p-4" role="dialog" aria-modal="true" aria-labelledby="recent-analysis-title" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <div className="flex max-h-[90vh] w-full max-w-3xl flex-col overflow-hidden rounded-3xl bg-white p-5 shadow-2xl sm:p-7">
      <div className="flex items-start justify-between gap-4 border-b border-slate-200 pb-4"><div><p className="text-xs font-bold uppercase tracking-[0.18em] text-amber-700">Resultado da análise</p><h2 id="recent-analysis-title" className="mt-2 text-xl font-semibold text-slate-950">Análise de <span className="text-amber-700">{item.asset}</span></h2></div><button type="button" aria-label="Fechar análise" onClick={onClose} className="rounded-full p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-900"><FontAwesomeIcon icon={faXmark} /></button></div>
      <div className="min-h-0 flex-1 overflow-y-auto pr-1">
      {analysis.isLoading && <p className="py-8 text-sm text-slate-500">Carregando análise...</p>}
      {analysis.isError && <p className="mt-5 rounded-xl bg-rose-50 p-4 text-sm text-rose-800">Não foi possível carregar a análise deste documento.</p>}
      {!analysis.isLoading && !analysis.isError && !analysis.data && <p className="mt-5 rounded-xl border border-dashed border-slate-300 bg-stone-50 p-4 text-sm text-slate-600">Nenhuma análise validada foi encontrada para este documento ainda.</p>}
      {analysis.data && <div className="mt-5 space-y-5 text-sm text-slate-700"><div className="grid gap-4 sm:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Importância</p><p className="mt-1 text-lg font-semibold text-slate-950">{analysis.data.importance}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Classificação</p><p className="mt-1 text-lg font-semibold text-slate-950">{analysis.data.classification}</p></div></div><article><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Resumo</p><p className="mt-2 leading-7">{analysis.data.summary}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Impacto</p><p className="mt-2 leading-7">{analysis.data.impact}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Impacto na minha carteira</p><p className="mt-2 leading-7">{analysis.data.portfolioAssessment?.portfolioImpactText ?? analysis.data.portfolioImpact}</p></article><div className="grid gap-4 border-t border-slate-200 pt-5 sm:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Ação sugerida</p><p className="mt-1 font-semibold text-slate-950">{analysis.data.suggestedAction}{analysis.data.requiresAction ? ' · Requer atenção' : ''}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Confiança</p><p className="mt-1 font-semibold text-slate-950">{Math.round(analysis.data.confidence * 100)}%</p></div></div><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Ativos afetados</p><div className="mt-2 flex flex-wrap gap-2">{analysis.data.affectedAssets.length > 0 ? analysis.data.affectedAssets.map(asset => <span key={asset} className="rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold">{asset}</span>) : <span>Nenhum ativo listado.</span>}</div></div></div>}
      </div>
      <div data-radar-actions="true" className="mt-6 flex shrink-0 flex-wrap gap-2 border-t border-slate-200 bg-white pt-4">
        {onMarkSeen && <button type="button" onClick={onMarkSeen} disabled={actionPending} className="inline-flex cursor-pointer items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-700 disabled:opacity-50"><FontAwesomeIcon icon={faEye} />Marcar como visto</button>}
        {onResolve && <button type="button" onClick={onResolve} disabled={actionPending} className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 transition hover:border-amber-500 hover:bg-current/10 hover:text-amber-700 disabled:opacity-50"><FontAwesomeIcon icon={faCircleCheck} />Fechar situação</button>}
      </div>
    </div>
  </div>
}

function RecentUpdatesCard({ items, portfolioId }: { items: DashboardAssessment[]; portfolioId: string }) {
  const [visibleCount, setVisibleCount] = useState(5)
  const [selectedItem, setSelectedItem] = useState<DashboardAssessment | null>(null)
  return <article className="min-w-0 rounded-3xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Últimas notícias</p>
    {items.length === 0 ? <p className="mt-5 rounded-2xl bg-stone-50 p-5 text-sm text-slate-500">Nenhuma mudança relevante recente para consultar.</p> : <div className="mt-4 divide-y divide-slate-100">
      {items.slice(0, visibleCount).map(item => item.documentId ? <button key={item.assessmentId} type="button" onClick={() => setSelectedItem(item)} className="grid w-full min-w-0 cursor-pointer gap-2 py-3 text-left first:pt-0 transition hover:bg-stone-50 sm:grid-cols-[minmax(0,1fr)_auto]">
        <div className="min-w-0"><h3 className="text-sm font-semibold text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}</h3></div>
        {formatRelativeDate(item.occurredAt) && <span className="text-xs text-slate-500 sm:text-right">{formatRelativeDate(item.occurredAt)}</span>}
      </button> : <div key={item.assessmentId} className="grid min-w-0 gap-2 py-3 first:pt-0 sm:grid-cols-[minmax(0,1fr)_auto]">
        <div className="min-w-0"><h3 className="text-sm font-semibold text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}</h3></div>
        {formatRelativeDate(item.occurredAt) && <span className="text-xs text-slate-500 sm:text-right">{formatRelativeDate(item.occurredAt)}</span>}
      </div>)}
    </div>}{items.length > visibleCount && <button type="button" onClick={() => setVisibleCount(current => Math.min(current + 3, items.length))} className="mt-4 inline-flex cursor-pointer items-center gap-1.5 text-[0.7rem] font-semibold text-sky-700 hover:text-sky-900">Mostrar mais <FontAwesomeIcon icon={faArrowDown} className="text-[0.65rem]" /></button>}{selectedItem && <RecentAnalysisModal item={selectedItem} portfolioId={portfolioId} onClose={() => setSelectedItem(null)} />}
  </article>
}

export function DashboardOverview({ dashboard, activePortfolio, portfoliosLoading = false }: DashboardOverviewProps) {
  if (portfoliosLoading) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-white p-6 text-slate-500">Carregando sua visão geral...</p></section>
  if (!activePortfolio) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiros passos</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Sua carteira começa aqui.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">Crie uma carteira e importe seu relatório consolidado da B3 para o Prumo entender seus investimentos.</p><Link to="/portfolio" className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Criar carteira <FontAwesomeIcon icon={faArrowRight} /></Link></div></section>
  if (dashboard.isLoading) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-white p-6 text-slate-500">Carregando sua visão geral...</p></section>
  if (dashboard.isError || !dashboard.data) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-rose-50 p-6 text-rose-800">Não foi possível carregar a visão geral.</p></section>

  const data = dashboard.data
  if (data.positionCount === 0) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiros passos</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Sua carteira começa aqui.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">Importe seu relatório consolidado da B3 para o Prumo entender seus investimentos.</p><Link to="/portfolio" className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Importar carteira <FontAwesomeIcon icon={faArrowRight} /></Link></div></section>

  return <section className="mx-auto max-w-358 px-4 pb-16 pt-8 sm:px-6 sm:pt-10">
    <header className="mb-7 flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
      <div><h1 className="text-3xl font-semibold tracking-tight text-slate-950">Visão geral</h1><p className="mt-2 text-sm text-slate-600">Aqui está o que importa sobre sua carteira agora.</p></div>
      {data.portfolioOverview?.lastPortfolioUpdate && <p className="text-xs text-slate-500">Carteira atualizada em {formatDateTime(data.portfolioOverview.lastPortfolioUpdate)}</p>}
    </header>
    <div className="grid min-w-0 items-start gap-4 lg:grid-cols-[7fr_3fr]">
      <PortfolioHealthCard data={data} />
      <RadarCard items={data.attentionNow ?? []} portfolioId={activePortfolio.id} />
      <div className="order-2 min-w-0 self-start lg:col-span-2"><RecentUpdatesCard items={data.recentUpdates ?? []} portfolioId={activePortfolio.id} /></div>
    </div>
    <TransactionReconciliationSection portfolioId={activePortfolio.id} />
  </section>
}
