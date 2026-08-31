import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowRight, faCircleCheck, faCircleInfo, faSpinner, faTriangleExclamation, faWandMagicSparkles, faXmark } from '@fortawesome/free-solid-svg-icons'

import { apiBaseUrl } from '../api/client'
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

type DashboardAssessment = {
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
  privacyHidden: boolean
  onGenerateGuidance: (amount: number) => Promise<DashboardGuidance>
}

const assetClassLabels: Record<number, string> = { 0: 'Ações', 1: 'FIIs', 2: 'Renda fixa', 3: 'Ativos internacionais', 4: 'Outros ativos' }
const priorityLabels = ['Baixa', 'Média', 'Alta', 'Crítica']
const actionLabels = ['Sem ação', 'Acompanhar', 'Revisar', 'Agir']
const capitalImpactLabels = ['Favorável a aumentar exposição', 'Sem impacto relevante', 'Cautela ao aumentar exposição', 'Evitar aumentar exposição agora']
const importanceLabels: Record<string, string> = { Critical: 'Crítico', Relevant: 'Relevante', Informative: 'Informativo', NoAction: 'Sem ação' }
const classificationLabels: Record<string, string> = { CorporateAction: 'Ação corporativa', FinancialResult: 'Resultado financeiro', Distribution: 'Distribuição', CapitalRaise: 'Captação', AssetTransaction: 'Transação de ativos', Governance: 'Governança', Operational: 'Operacional', Liquidation: 'Liquidação', Other: 'Outro' }

void actionLabels
void capitalImpactLabels

function formatCurrency(value: number | null | undefined, hidden = false) {
  if (hidden) return 'R$ ••••••'
  if (value == null) return 'Sem valor'
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

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

function parseContributionAmount(value: string) {
  const cleaned = value.trim().replace(/[^\d,.-]/g, '')
  if (!cleaned) return Number.NaN
  if (cleaned.includes(',')) return Number(cleaned.replace(/\./g, '').replace(',', '.'))
  if (/^\d{1,3}(\.\d{3})+$/.test(cleaned)) return Number(cleaned.replace(/\./g, ''))
  return Number(cleaned)
}

function matchesEnum(value: number | string | undefined, numericValue: number, stringValue: string) {
  return value === numericValue || value === stringValue
}

function CardHeader({ eyebrow, linkLabel, to }: { eyebrow: string; linkLabel: string; to: string }) {
  return <div className="flex items-center justify-between gap-4">
    <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">{eyebrow}</p>
    <Link to={to} className="inline-flex shrink-0 items-center gap-2 text-xs font-semibold text-sky-700 transition hover:text-sky-900">{linkLabel}<FontAwesomeIcon icon={faArrowRight} /></Link>
  </div>
}

function PortfolioHealthCard({ data }: { data: Dashboard }) {
  const health = data.portfolioHealthSummary
  const overview = data.portfolioOverview
  const deviations = health?.allocationDeviations.slice(0, 2) ?? []
  const notConfigured = health?.status === 'NotConfigured'
  const aligned = health?.status === 'Aligned'

  return <article className="order-2 min-w-0 self-start rounded-3xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
    <CardHeader eyebrow="Saúde da carteira" linkLabel="Ver detalhes" to="/portfolio" />
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
      Configure as alocações-alvo para que o Lodira possa avaliar a distribuição da carteira e orientar seus aportes.
      <Link to="/portfolio" className="mt-3 flex w-fit items-center gap-2 font-semibold text-slate-900">Configurar alocação-alvo <FontAwesomeIcon icon={faArrowRight} /></Link>
    </div>}
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
    <CardHeader eyebrow="Radar" linkLabel="Ver Radar" to="/radar" />
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
    </div>}{items.length > visibleCount && <button type="button" onClick={() => setVisibleCount(current => Math.min(current + 3, items.length))} className="mt-4 inline-flex cursor-pointer items-center gap-2 text-sm font-semibold text-sky-700 hover:text-sky-900">Ver mais <FontAwesomeIcon icon={faArrowRight} /></button>}{selectedItem && <RecentAnalysisModal item={selectedItem} portfolioId={portfolioId} onClose={() => setSelectedItem(null)} onMarkSeen={() => statusMutation.mutate({ id: selectedItem.assessmentId, resolved: false })} onResolve={() => statusMutation.mutate({ id: selectedItem.assessmentId, resolved: true })} actionPending={statusMutation.isPending} />}
  </article>
}

function ContributionCard({ preview: rawPreview, portfolioId, privacyHidden, onGenerateGuidance }: { preview: Dashboard['contributionPreview']; portfolioId: string; privacyHidden: boolean; onGenerateGuidance: (amount: number) => Promise<DashboardGuidance> }) {
  const [amount, setAmount] = useState('')
  const [validationError, setValidationError] = useState<string | null>(null)
  const mutation = useMutation({ mutationFn: onGenerateGuidance })
  const guidance = mutation.data
  const persistedGuidance = useQuery({
    queryKey: ['dashboard-contribution-guidance', portfolioId, guidance?.guidanceId],
    queryFn: async () => {
      const response = await fetch(`${contributionApiUrl}/api/portfolios/${portfolioId}/contribution-guidance/${guidance?.guidanceId}`)
      if (!response.ok) throw new Error('Não foi possível atualizar a orientação.')
      return response.json() as Promise<DashboardGuidance>
    },
    enabled: Boolean(guidance?.guidanceId && guidance.jobStatus && guidance.jobStatus !== 'Completed' && guidance.jobStatus !== 'Failed'),
    refetchInterval: query => query.state.data?.jobStatus === 'Completed' || query.state.data?.jobStatus === 'Failed' ? false : 1500
  })
  const currentGuidance = persistedGuidance.data ?? guidance
  const visibleGuidance = currentGuidance?.jobStatus === 'Completed' ? currentGuidance : undefined
  const enriching = Boolean(currentGuidance?.jobStatus && currentGuidance.jobStatus !== 'Completed' && currentGuidance.jobStatus !== 'Failed')
  const analysisFailed = currentGuidance?.jobStatus === 'Failed'
  const hasNoNeeds = matchesEnum(rawPreview?.status, 2, 'NoNeeds')
  const preview = hasNoNeeds && rawPreview ? { ...rawPreview, summary: 'Sua carteira está alinhada; o próximo aporte buscará preservar suas alocações-alvo.' } : rawPreview
  const isNoNeeds = false
  const isAvailable = matchesEnum(preview?.status, 0, 'Available') || hasNoNeeds
  const isNotConfigured = matchesEnum(preview?.status, 1, 'NotConfigured')

  const submit = () => {
    const parsedAmount = parseContributionAmount(amount)
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setValidationError('Informe um valor de aporte válido e maior que zero.')
      return
    }
    setValidationError(null)
    mutation.mutate(parsedAmount)
  }

  return <article className="min-w-0 rounded-3xl border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
    <CardHeader eyebrow="Próximo aporte" linkLabel="Ver aportes" to="/contributions" />
    <div className={`mt-5 grid min-w-0 gap-6 lg:gap-8 ${visibleGuidance ? 'lg:grid-cols-[minmax(0,.75fr)_minmax(0,1.35fr)_minmax(0,1fr)]' : 'lg:grid-cols-[minmax(17rem,.7fr)_minmax(0,1.3fr)]'}`}>
      <div className="min-w-0 lg:border-r lg:border-slate-100 lg:pr-8">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Necessidade atual</p>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight text-slate-950">{preview?.summary ?? 'A orientação de aporte ainda não está disponível.'}</h2>
        {isAvailable && <form className="mt-5" onSubmit={event => { event.preventDefault(); submit() }}>
          <label htmlFor="dashboard-contribution" className="text-sm font-medium text-slate-700">Quanto pretende aportar?</label>
          <div className="mt-2 flex min-w-0 flex-wrap gap-2">
            <input id="dashboard-contribution" inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} placeholder="R$ 0,00" className="w-36 min-w-0 flex-[1.15] basis-24 rounded-xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" />
            <button type="submit" disabled={mutation.isPending} className="shrink-0 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:opacity-50"><FontAwesomeIcon icon={faWandMagicSparkles} className="mr-2" />{mutation.isPending ? 'Calculando...' : 'Gerar orientação'}</button>
          </div>
          {validationError && <p className="mt-2 text-xs text-rose-700">{validationError}</p>}
          {mutation.isError && <p className="mt-2 text-xs text-rose-700">Não foi possível gerar a orientação. Confira o valor e tente novamente.</p>}
        </form>}
        {isNotConfigured && <Link to="/portfolio" className="mt-5 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white">Configurar estratégia <FontAwesomeIcon icon={faArrowRight} /></Link>}
        {isNoNeeds && <p className="mt-4 flex items-center gap-2 text-sm font-medium text-emerald-700"><FontAwesomeIcon icon={faCircleCheck} /> Nenhuma classe precisa ser priorizada agora.</p>}
        {enriching && <p role="status" className="mt-4 flex items-center gap-2 text-sm font-medium text-amber-700"><FontAwesomeIcon icon={faSpinner} spin /> A IA está analisando os ativos e definindo a distribuição...</p>}
        {analysisFailed && <p className="mt-4 text-sm font-medium text-rose-700">Não foi possível concluir a análise da IA. Tente novamente.</p>}
      </div>

      <div className="min-w-0">
        {!visibleGuidance ? <div className="flex min-h-32 items-center rounded-2xl border border-dashed border-slate-200 bg-stone-50 p-5 text-sm leading-6 text-slate-500">
          {enriching ? 'A distribuição será exibida somente após a conclusão da análise da IA.' : isAvailable ? 'Informe um valor para iniciar a análise.' : isNotConfigured ? 'A orientação ficará disponível depois que a estratégia for configurada.' : 'Não há orientação de compra necessária para o estado atual da carteira.'}
        </div> : <GuidanceResult guidance={visibleGuidance} privacyHidden={privacyHidden} />}
      </div>
      {visibleGuidance && <div className="min-w-0 lg:border-l lg:border-slate-100 lg:pl-8"><ContributionSummary guidance={visibleGuidance} /></div>}
    </div>
  </article>
}

function ContributionSummary({ guidance }: { guidance: DashboardGuidance }) {
  const reasons = guidance.reasons.slice(0, 4)
  return <div className="min-w-0 lg:border-l lg:border-slate-100 lg:pl-8">
    <p className="text-sm font-semibold text-slate-900">Resumo</p>
    <div className="mt-4 space-y-3 text-sm text-slate-600">
      {reasons.length > 0 ? reasons.map((reason, index) => <div key={`${reason}-${index}`} className="flex min-w-0 items-start gap-3"><FontAwesomeIcon icon={index === 2 ? faTriangleExclamation : faCircleCheck} className={`mt-1 shrink-0 ${index === 2 ? 'text-amber-500' : 'text-emerald-500'}`} /><span className="min-w-0 wrap-break-word">{reason}</span></div>) : <div className="flex min-w-0 items-start gap-3"><FontAwesomeIcon icon={faCircleCheck} className="mt-1 shrink-0 text-emerald-500" /><span className="min-w-0 wrap-break-word">Aporte calculado com base na estratégia da carteira.</span></div>}
    </div>
  </div>
}

function GuidanceResult({ guidance, privacyHidden }: { guidance: DashboardGuidance; privacyHidden: boolean }) {
  const allocatedAmount = guidance.allocatedAmount ?? guidance.allocations.reduce((total, item) => total + item.amount, 0)
  const styleLabel = matchesEnum(guidance.allocationStyle, 0, 'Concentrated') ? 'Aporte concentrado' : matchesEnum(guidance.allocationStyle, 1, 'Distributed') ? 'Aporte distribuído' : 'Sem alocação sugerida'
  const importantAvoided = guidance.avoidedCandidates.filter(item => item.reasonType <= 2).slice(0, 3)

  return <div className="min-w-0">
    <div className="flex flex-wrap items-end justify-between gap-3">
      <div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Onde aportar</p><h3 className="mt-1 text-lg font-semibold">{styleLabel}</h3></div>
      <div className="text-right text-xs text-slate-500"><span className="block">Alocado</span><strong className="text-sm text-slate-900">{formatCurrency(allocatedAmount, privacyHidden)}</strong></div>
    </div>
    {guidance.allocations.length > 0 ? <div className="mt-4 space-y-4">
      {guidance.allocations.map(item => {
        const share = guidance.contributionAmount > 0 ? item.amount / guidance.contributionAmount * 100 : 0
        return <div key={item.assetId} className="min-w-0">
          <div className="flex items-center justify-between gap-3 text-sm"><strong>{item.ticker}</strong><span className="shrink-0 font-semibold">{formatCurrency(item.amount, privacyHidden)}</span></div>
          <div className="mt-2 h-2 overflow-hidden rounded-full bg-slate-100"><div className="h-full rounded-full bg-emerald-500" style={{ width: `${Math.min(share, 100)}%` }} /></div>
          <p className="mt-1 text-xs leading-5 text-slate-500">{formatPercentage(share)}% do valor informado · {item.reason}</p>
        </div>
      })}
    </div> : <div className="mt-4 rounded-2xl bg-amber-50 p-4 text-sm text-amber-900"><strong>Nenhum candidato elegível.</strong><p className="mt-1 leading-6">{guidance.reasons[0] ?? 'As restrições atuais impedem uma alocação responsável.'}</p></div>}

    {guidance.unallocatedAmount > 0 && <div className="mt-5 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950">
      <strong>{formatCurrency(guidance.unallocatedAmount, privacyHidden)} sem alocação</strong>
      <p className="mt-1 text-xs leading-5 text-amber-800">O valor não foi redistribuído para preservar as necessidades e restrições calculadas pelo Lodira.</p>
    </div>}
    {importantAvoided.length > 0 && <div className="mt-4"><p className="text-xs font-semibold uppercase tracking-[0.12em] text-slate-500">Restrições relevantes</p>{importantAvoided.map(item => <p key={item.assetId} className="mt-2 text-xs leading-5 text-slate-600"><strong>{item.ticker}:</strong> {item.reason}</p>)}</div>}
  </div>
}

function RecentAnalysisModal({ item, portfolioId, onClose, onMarkSeen, onResolve, actionPending = false }: { item: DashboardAssessment; portfolioId: string; onClose: () => void; onMarkSeen?: () => void; onResolve?: () => void; actionPending?: boolean }) {
  const analysis = useQuery({
    queryKey: ['dashboard-document-analysis', item.documentId, portfolioId],
    queryFn: async () => {
      const response = await fetch(`${contributionApiUrl}/api/documents/${item.documentId}/analysis?portfolioId=${portfolioId}`)
      if (response.status === 404) return null
      if (!response.ok) throw new Error('Não foi possível carregar a análise deste documento.')
      const data = await response.json() as RecentDocumentAnalysis
      return { ...data, importance: importanceLabels[data.importance] ?? data.importance, classification: classificationLabels[data.classification] ?? data.classification }
    },
    enabled: Boolean(item.documentId),
  })
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
  useEffect(() => {
    if ((!onMarkSeen && !onResolve) || !analysis.data) return
    const dialog = document.querySelector('[aria-labelledby="recent-analysis-title"]')
    const content = dialog?.firstElementChild
    if (!content || content.querySelector('[data-radar-actions]')) return
    const actions = document.createElement('div')
    actions.dataset.radarActions = 'true'
    actions.className = 'mt-6 flex flex-wrap gap-2 border-t border-slate-200 pt-5'
    if (onMarkSeen) {
      const seen = document.createElement('button')
      seen.type = 'button'; seen.className = 'cursor-pointer rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white hover:bg-slate-700 disabled:opacity-50'; seen.textContent = 'Marcar como visto'; seen.disabled = actionPending; seen.onclick = onMarkSeen; actions.appendChild(seen)
    }
    if (onResolve) {
      const resolve = document.createElement('button')
      resolve.type = 'button'; resolve.className = 'cursor-pointer rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 hover:border-amber-500 hover:text-amber-700 disabled:opacity-50'; resolve.textContent = 'Fechar situação'; resolve.disabled = actionPending; resolve.onclick = onResolve; actions.appendChild(resolve)
    }
    content.appendChild(actions)
    return () => actions.remove()
  }, [analysis.data, onMarkSeen, onResolve, actionPending])

  return <div className="fixed inset-0 z-40 flex items-center justify-center bg-slate-950/50 p-4" role="dialog" aria-modal="true" aria-labelledby="recent-analysis-title" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <div className="max-h-[90vh] w-full max-w-3xl overflow-y-auto rounded-3xl bg-white p-5 shadow-2xl sm:p-7">
      <div className="flex items-start justify-between gap-4 border-b border-slate-200 pb-4"><div><p className="text-xs font-bold uppercase tracking-[0.18em] text-amber-700">Resultado da análise</p><h2 id="recent-analysis-title" className="mt-2 text-xl font-semibold text-slate-950"><span className="mr-2 text-amber-700">{item.asset}</span>{item.eventSummary}</h2></div><button type="button" aria-label="Fechar análise" onClick={onClose} className="rounded-full p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-900"><FontAwesomeIcon icon={faXmark} /></button></div>
      {analysis.isLoading && <p className="py-8 text-sm text-slate-500">Carregando análise...</p>}
      {analysis.isError && <p className="mt-5 rounded-xl bg-rose-50 p-4 text-sm text-rose-800">Não foi possível carregar a análise deste documento.</p>}
      {!analysis.isLoading && !analysis.isError && !analysis.data && <p className="mt-5 rounded-xl border border-dashed border-slate-300 bg-stone-50 p-4 text-sm text-slate-600">Nenhuma análise validada foi encontrada para este documento ainda.</p>}
      {analysis.data && <div className="mt-5 space-y-5 text-sm text-slate-700"><div className="grid gap-4 sm:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Importância</p><p className="mt-1 text-lg font-semibold text-slate-950">{analysis.data.importance}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Classificação</p><p className="mt-1 text-lg font-semibold text-slate-950">{analysis.data.classification}</p></div></div><article><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Resumo</p><p className="mt-2 leading-7">{analysis.data.summary}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Impacto</p><p className="mt-2 leading-7">{analysis.data.impact}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Impacto na minha carteira</p><p className="mt-2 leading-7">{analysis.data.portfolioAssessment?.portfolioImpactText ?? analysis.data.portfolioImpact}</p></article><div className="grid gap-4 border-t border-slate-200 pt-5 sm:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Ação sugerida</p><p className="mt-1 font-semibold text-slate-950">{analysis.data.suggestedAction}{analysis.data.requiresAction ? ' · Requer atenção' : ''}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Confiança</p><p className="mt-1 font-semibold text-slate-950">{Math.round(analysis.data.confidence * 100)}%</p></div></div><div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Ativos afetados</p><div className="mt-2 flex flex-wrap gap-2">{analysis.data.affectedAssets.length > 0 ? analysis.data.affectedAssets.map(asset => <span key={asset} className="rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold">{asset}</span>) : <span>Nenhum ativo listado.</span>}</div></div></div>}
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
    </div>}{items.length > visibleCount && <button type="button" onClick={() => setVisibleCount(current => Math.min(current + 3, items.length))} className="mt-4 inline-flex cursor-pointer items-center gap-2 text-sm font-semibold text-sky-700 hover:text-sky-900">Ver mais <FontAwesomeIcon icon={faArrowRight} /></button>}{selectedItem && <RecentAnalysisModal item={selectedItem} portfolioId={portfolioId} onClose={() => setSelectedItem(null)} />}
  </article>
}

export function DashboardOverview({ dashboard, activePortfolio, portfoliosLoading = false, privacyHidden, onGenerateGuidance }: DashboardOverviewProps) {
  if (portfoliosLoading) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-white p-6 text-slate-500">Carregando sua visão geral...</p></section>
  if (!activePortfolio) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiros passos</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Sua carteira começa aqui.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">Crie uma carteira e importe seu relatório consolidado da B3 para o Lodira entender seus investimentos.</p><Link to="/portfolio" className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Criar carteira <FontAwesomeIcon icon={faArrowRight} /></Link></div></section>
  if (dashboard.isLoading) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-white p-6 text-slate-500">Carregando sua visão geral...</p></section>
  if (dashboard.isError || !dashboard.data) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><p className="rounded-2xl bg-rose-50 p-6 text-rose-800">Não foi possível carregar a visão geral.</p></section>

  const data = dashboard.data
  if (data.positionCount === 0) return <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6"><div className="rounded-[1.75rem] border border-amber-200 bg-amber-50 p-7 sm:p-9"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Primeiros passos</p><h1 className="mt-3 text-3xl font-semibold tracking-tight">Sua carteira começa aqui.</h1><p className="mt-3 max-w-xl text-sm leading-6 text-slate-700">Importe seu relatório consolidado da B3 para o Lodira entender seus investimentos.</p><Link to="/portfolio" className="mt-6 inline-flex items-center gap-2 rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white">Importar carteira <FontAwesomeIcon icon={faArrowRight} /></Link></div></section>

  return <section className="mx-auto max-w-358 px-4 pb-16 pt-8 sm:px-6 sm:pt-10">
    <header className="mb-7 flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
      <div><h1 className="text-3xl font-semibold tracking-tight text-slate-950">Visão geral</h1><p className="mt-2 text-sm text-slate-600">Aqui está o que importa sobre sua carteira agora.</p></div>
      {data.portfolioOverview?.lastPortfolioUpdate && <p className="text-xs text-slate-500">Carteira atualizada em {formatDateTime(data.portfolioOverview.lastPortfolioUpdate)}</p>}
    </header>
    <div className="grid min-w-0 items-start gap-4 lg:grid-cols-[7fr_3fr]">
      <PortfolioHealthCard data={data} />
      <RadarCard items={data.attentionNow ?? []} portfolioId={activePortfolio.id} />
      <div className="order-2 min-w-0 self-start lg:col-span-2"><RecentUpdatesCard items={data.recentUpdates ?? []} portfolioId={activePortfolio.id} /></div>
      <div className="order-3 min-w-0 self-start lg:col-span-2"><ContributionCard preview={data.contributionPreview} portfolioId={activePortfolio.id} privacyHidden={privacyHidden} onGenerateGuidance={onGenerateGuidance} /></div>
    </div>
  </section>
}
