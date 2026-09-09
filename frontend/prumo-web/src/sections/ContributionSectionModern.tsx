import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCircleCheck, faCircleInfo, faCopy, faSpinner, faTriangleExclamation, faWandMagicSparkles } from '@fortawesome/free-solid-svg-icons'
import { AppDialog } from '../components/AppDialog'
import { apiBaseUrl } from '../api/client'

type Allocation = { assetId?: string | null; origin?: string; candidateKey?: string | null; ticker: string; assetClass: number; amount: number; reason: string }
type AvoidedCandidate = { assetId: string; ticker: string; assetClass: number; reason: string }
type AnalysisTrace = { generatedAt?: string; engineVersion?: string; provider?: string; model?: string; policyVersion?: string; assessmentVersions?: string[]; evidenceCount?: number; criteriaJson?: string; fallbackReason?: string | null; selectionMode?: string | null; selectedScenarioKey?: string | null; retryCount?: number }
type ClassAllocation = { assetClass: number; amount: number; reason: string }
type DecisionNeed = { type: string; dimension: string; key?: string | null; severity: number; confidence: string; coverage: string; reasons: string[] }
type FinancialEvaluation = { assetId: string; quality: { category: string; freshness: string; signals: string[]; concerns: string[] }; valuation: { assessment: string; metrics: { metric: string; value: number }[] } }
type Decision = { decisionType: 'ReinforceExisting' | 'AddNewAsset' | 'SplitContribution' | 'NoAction'; primaryNeed?: { assetClass: string; gapPercentagePoints: number } | null; recommendedScenario?: { deviationBefore: number; deviationAfter: number; allocations?: { assetId: string }[] } | null; decisionEvidence?: { primaryNeed: string; decisiveFactor: string; concentrationEffect: string; qualityContext: string; valuationContext: string } | null; secondaryNeeds?: DecisionNeed[]; financialEvaluations?: FinancialEvaluation[]; missingData: string[]; reasons: string[] }
type Guidance = { guidanceId: string; requestedAmount: number; allocations: Allocation[]; classAllocations?: ClassAllocation[]; avoidedCandidates?: AvoidedCandidate[]; allocatedAmount: number; unallocatedAmount: number; reasons: string[]; evidence?: string[]; analysis?: AnalysisTrace; intelligenceAvailability?: string; jobStatus?: string; decision?: Decision | null }
type Position = { id: string }

const money = (value: number, hidden: boolean) => hidden ? 'R$ ••••••' : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
const parse = (value: string) => Number(value.replace(/\./g, '').replace(',', '.'))
const classLabels: Record<number, string> = { 0: 'Ações', 1: 'FIIs', 2: 'Renda fixa', 3: 'ETF', 4: 'Outros' }

function CopyCriteriaJsonButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    await navigator.clipboard.writeText(value)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 2000)
  }
  return <button type="button" onClick={() => void copy()} className="ml-3 inline-flex items-center gap-1 rounded-lg border border-slate-500 px-2 py-1 text-[11px] font-semibold text-slate-100 hover:bg-slate-800"><FontAwesomeIcon icon={faCopy} />{copied ? 'Copiado' : 'Copiar JSON'}</button>
}

async function api<T>(path: string, body?: unknown) {
  const response = await fetch(apiBaseUrl + path, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : undefined)
  if (!response.ok) { const message = await response.text(); if (message.includes('GuidanceAlreadyConfirmed')) throw new Error('Este aporte já foi confirmado. Consulte o histórico.'); throw new Error(message || 'Não foi possível concluir a operação.') }
  return response.json() as Promise<T>
}

async function apiOptional<T>(path: string) {
  const response = await fetch(apiBaseUrl + path)
  if (response.status === 404) return null
  if (!response.ok) throw new Error(await response.text() || 'Não foi possível carregar o cenário persistido.')
  return response.json() as Promise<T>
}

export function ContributionSectionModern({ portfolioId, positions }: { portfolioId: string; positions: Position[] }) {
  const queryClient = useQueryClient()
  const [amount, setAmount] = useState('')
  const [guidance, setGuidance] = useState<Guidance | null>(null)
  const [selected, setSelected] = useState<Allocation | null>(null)
  const [confirmedGuidanceId, setConfirmedGuidanceId] = useState<string | null>(null)
  const generate = useMutation({
    mutationFn: () => api<Guidance>(`/api/portfolios/${portfolioId}/contribution-guidance`, { amount: parse(amount) }),
    onMutate: () => setGuidance(null),
    onSuccess: setGuidance
  })
  const latest = useQuery({
    queryKey: ['latest-contribution', portfolioId],
    queryFn: () => apiOptional<Guidance>(`/api/portfolios/${portfolioId}/contribution-guidance/latest`),
  })
  const activeGuidance = guidance ?? latest.data
  const polling = useQuery({
    queryKey: ['standalone-contribution', portfolioId, activeGuidance?.guidanceId],
    queryFn: () => api<Guidance>(`/api/portfolios/${portfolioId}/contribution-guidance/${activeGuidance?.guidanceId}`),
    enabled: Boolean(activeGuidance?.guidanceId && activeGuidance.jobStatus && !['Completed', 'Failed'].includes(activeGuidance.jobStatus)),
    refetchInterval: query => ['Completed', 'Failed'].includes(query.state.data?.jobStatus ?? '') ? false : 1500
  })
  const current = polling.data ?? activeGuidance
  const pending = generate.isPending || Boolean(current?.jobStatus && !['Completed', 'Failed'].includes(current.jobStatus))
  const completed = current?.jobStatus === 'Completed' && current.guidanceId !== confirmedGuidanceId ? current : null
  const isFallback = completed?.intelligenceAvailability === 'InsufficientIntelligence'
  const isAiAssisted = completed?.intelligenceAvailability === 'IntelligenceAvailable' && completed.analysis?.provider !== 'deterministic' && !['scenario:no-action', 'scenario:baseline-partial'].includes(completed.analysis?.selectedScenarioKey ?? '')
  const isNoAction = Boolean(completed && completed.allocations.length === 0 && completed.classAllocations?.length === 0)
  const isPartial = Boolean(completed && completed.allocatedAmount > 0 && completed.allocatedAmount < completed.requestedAmount)
  const contextMessage = isFallback
    ? completed?.analysis?.fallbackReason ?? 'A análise por IA não foi concluída; o motor determinístico preservou a distribuição segura.'
    : isNoAction
      ? 'A IA analisou os candidatos e concluiu que nenhum aporte em ativo atende aos limites atuais com segurança.'
      : isPartial
        ? 'A IA analisou a distribuição parcial segura; o valor restante permaneceu sem alocação porque não havia outra alternativa elegível.'
        : completed?.analysis?.selectionMode === 'single-safe-scenario'
          ? 'Havia apenas um cenário seguro pré-validado; ele foi aplicado diretamente pelo motor de regras.'
          : isAiAssisted
            ? 'O Prumo analisou os cenários válidos e selecionou esta alternativa com assistência de IA.'
            : 'A recomendação foi calculada pelo motor determinístico do Prumo.'
  const failed = current?.jobStatus === 'Failed'
  void contextMessage

  const confirm = useMutation({
    mutationFn: (value: { quantity: number; unitPrice: number }) => api(`/api/portfolios/${portfolioId}/contributions`, { guidanceId: completed?.guidanceId, items: completed?.decision?.decisionType === 'SplitContribution' ? completed.allocations.map(allocation => ({ assetId: allocation.assetId ?? '00000000-0000-0000-0000-000000000000', candidateKey: allocation.candidateKey, quantity: allocation.amount / value.unitPrice, unitPrice: value.unitPrice })) : [{ assetId: selected?.assetId ?? '00000000-0000-0000-0000-000000000000', candidateKey: selected?.candidateKey, ...value }] }),
    onSuccess: async () => { setConfirmedGuidanceId(current?.guidanceId ?? null); setSelected(null); setGuidance(null); await queryClient.invalidateQueries({ queryKey: ['history-timeline', portfolioId] }); await queryClient.invalidateQueries({ queryKey: ['dashboard', portfolioId] }); await queryClient.invalidateQueries({ queryKey: ['latest-contribution', portfolioId] }) }
  })

  return <section className="mx-auto max-w-358 px-6 pb-24 pt-3">
    <div className="rounded-[1.75rem] border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
      <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Próximo aporte</p>
      <div className="mt-5 grid gap-6 lg:grid-cols-[.75fr_1.35fr_1fr] lg:gap-8">
        <div className="lg:border-r lg:border-slate-100 lg:pr-8">
          <p className="text-xs font-semibold uppercase tracking-[.14em] text-slate-500">Simulação de aporte</p>
          <h1 className="mt-2 text-2xl font-semibold">Explore um cenário para o próximo aporte.</h1>
          <p className="mt-2 text-xs leading-5 text-slate-500">Os candidatos são definidos automaticamente a partir dos dados da carteira, da estratégia cadastrada e dos eventos relevantes.</p>
          <form className="mt-5" onSubmit={event => { event.preventDefault(); generate.mutate() }}>
            <label className="text-sm font-medium">Qual valor deseja simular?</label>
            <div className="mt-2 flex flex-col gap-2 sm:flex-row">
              <input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} placeholder="R$ 0,00" className="min-w-0 w-full flex-[1.15] rounded-xl border border-slate-300 bg-stone-50 px-4 py-3 sm:w-auto" />
              <button disabled={pending || !amount || !positions.length} className="inline-flex w-full cursor-pointer items-center justify-center rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white disabled:opacity-50 sm:w-auto">
                <FontAwesomeIcon icon={faWandMagicSparkles} className="mr-2" />{generate.isPending ? 'Enviando...' : 'Gerar cenário'}
              </button>
            </div>
          </form>
          {pending && <p role="status" className="mt-4 flex items-center gap-2 text-sm text-amber-700"><FontAwesomeIcon icon={faSpinner} spin /> Análise em andamento: o Prumo está avaliando os ativos e definindo a distribuição...</p>}
          {failed && <p className="mt-4 text-sm text-rose-700">Não foi possível concluir a análise. Tente novamente.</p>}
        </div>

        <div>{completed ? <>
          {completed.decision && <div className="mb-4 rounded-2xl border border-amber-200 bg-amber-50 p-4"><p className="text-xs font-bold uppercase tracking-[.14em] text-amber-700">{completed.decision.decisionType === 'NoAction' ? 'Resultado da análise' : 'Recomendação'}</p><h2 className="mt-1 text-lg font-semibold">{{ ReinforceExisting: 'Reforçar posição existente', AddNewAsset: 'Abrir nova posição', SplitContribution: 'Dividir o aporte', NoAction: 'Nenhum cenário elegível' }[completed.decision.decisionType]}</h2>{completed.decision.primaryNeed && <p className="mt-2 text-sm text-amber-950"><strong>Necessidade principal:</strong> a classe {completed.decision.primaryNeed.assetClass} está {completed.decision.primaryNeed.gapPercentagePoints.toFixed(1)} p.p. abaixo do target.</p>}{completed.decision.recommendedScenario && <p className="mt-2 text-xs text-amber-900">Desvio da carteira: {completed.decision.recommendedScenario.deviationBefore.toFixed(1)} → {completed.decision.recommendedScenario.deviationAfter.toFixed(1)} p.p.</p>}{completed.decision.secondaryNeeds?.slice(0, 2).map(need => <p key={`${need.type}-${need.key}`} className="mt-2 text-xs text-amber-900">Sinal estrutural: {need.reasons[0]}</p>)}{completed.decision.financialEvaluations?.find(item => completed.decision?.recommendedScenario?.allocations?.some(allocation => allocation.assetId === item.assetId) && (item.quality.signals.length || item.quality.concerns.length)) && <p className="mt-2 text-xs text-amber-900">Evidência financeira disponível para o cenário recomendado.</p>}{completed.decision.missingData.slice(0, 1).map(reason => <p key={reason} className="mt-2 text-xs text-slate-600">{reason}</p>)}{completed.decision.reasons.slice(0, 1).map(reason => <p key={reason} className="mt-2 text-xs text-slate-600">{reason}</p>)}</div>}
          <div className="flex justify-between"><div><p className="text-xs font-semibold uppercase tracking-[.14em] text-slate-500">Alocação calculada</p><h2 className="mt-1 text-lg font-semibold">Cenário de distribuição</h2><p className="mt-1 max-w-xl text-xs leading-5 text-slate-500">O cálculo considera necessidade da classe, peso atual, diversificação, concentração e aportes recentes. Altere o valor acima para simular outro cenário.</p></div><strong>{money(completed.allocatedAmount, false)}</strong></div>
          <div className="mt-4 space-y-4">{completed.classAllocations?.map(item => <div key={`class-${item.assetClass}`} className="rounded-xl border border-dashed border-amber-300 p-3">
            <div className="flex justify-between text-sm"><strong>{classLabels[item.assetClass] ?? 'Classe de ativos'}</strong><span>{money(item.amount, false)}</span></div>
            <div className="mt-2 h-2 rounded-full bg-slate-100"><div className="h-full rounded-full bg-amber-400" style={{ width: `${Math.min(item.amount / completed.requestedAmount * 100, 100)}%` }} /></div>
            <p className="mt-1 text-xs text-slate-500">{item.reason} Escolha um ativo dessa classe antes de realizar o aporte.</p>
          </div>)}{completed.allocations.map(item => <div key={item.candidateKey ?? item.assetId ?? item.ticker}>
            <div className="flex justify-between text-sm"><strong>{item.ticker}</strong><span>{money(item.amount, false)}</span></div>
            <div className="mt-2 h-2 rounded-full bg-slate-100"><div className="h-full rounded-full bg-emerald-500" style={{ width: `${Math.min(item.amount / completed.requestedAmount * 100, 100)}%` }} /></div>
            {item.reason && item.reason !== item.ticker && <p className="mt-1 text-xs text-slate-500">{item.reason}</p>}
            <button type="button" onClick={() => setSelected(item)} className="mt-2 cursor-pointer rounded-full border border-slate-300 px-3 py-1.5 text-xs font-semibold hover:border-amber-500">Registrar este aporte</button>
          </div>)}</div>
        </> : <div className="flex min-h-32 items-center rounded-2xl border border-dashed border-slate-200 bg-stone-50 p-5 text-sm text-slate-500">
          {pending ? 'A distribuição será exibida somente após a conclusão da análise.' : 'Informe um valor para iniciar a análise.'}
        </div>}</div>

        <div className="lg:border-l lg:border-slate-100 lg:pl-8">
          <p className="text-sm font-semibold">{pending ? 'Análise em andamento' : 'Contexto do cenário'}</p>
          {!completed && !pending ? <p className="mt-4 text-sm leading-6 text-slate-500">Gere um cenário para ver os critérios e o contexto usados pelo Prumo.</p> : pending ? <div className="mt-4 space-y-3"><div className="h-3 animate-pulse rounded bg-slate-200" /><div className="h-3 animate-pulse rounded bg-slate-200" /><div className="h-3 w-3/5 animate-pulse rounded bg-slate-200" /></div>
            : <div className="mt-4 space-y-4 text-sm text-slate-600"><p className="text-xs font-semibold text-slate-500">Influência da IA: {isAiAssisted ? 'Sim, selecionou o cenário' : 'Não, cálculo determinístico'}.</p><p className={`flex gap-3 font-semibold ${isFallback ? 'text-amber-700' : 'text-emerald-700'}`}><FontAwesomeIcon icon={isFallback ? faTriangleExclamation : faCircleCheck} className="mt-1" /><span>{isAiAssisted ? 'Prumo analisou os cenários válidos e selecionou esta alternativa com assistência de IA.' : completed?.analysis?.fallbackReason ?? 'A recomendação foi calculada pelo motor determinístico do Prumo porque a análise por IA não estava disponível.'}</span></p><div className="space-y-3">{completed?.reasons.slice(0, 4).map((reason, index) => <div key={`${reason}-${index}`} className="flex gap-3"><FontAwesomeIcon icon={isFallback ? faTriangleExclamation : faCircleCheck} className={`mt-1 ${isFallback ? 'text-amber-500' : 'text-emerald-500'}`} /><span>{reason}</span></div>)}</div>{completed?.avoidedCandidates?.length ? <div className="border-t border-slate-100 pt-4"><p className="mb-2 flex items-center gap-2 text-xs font-semibold uppercase tracking-[.14em] text-slate-500"><FontAwesomeIcon icon={faCircleInfo} />Alternativas consideradas</p><div className="space-y-2 text-xs">{completed.avoidedCandidates.slice(0, 3).map(candidate => <p key={candidate.assetId}><strong>{candidate.ticker}:</strong> {candidate.reason}</p>)}</div></div> : null}</div>}
        </div>
      </div>
      {completed?.analysis && <details className="mt-6 border-t border-slate-100 pt-4 text-xs text-slate-500"><summary className="cursor-pointer font-semibold text-slate-600">Dados e critérios usados nesta análise</summary><div className="mt-3 grid gap-2 sm:grid-cols-2"><span>Gerado em: {completed.analysis.generatedAt ? new Date(completed.analysis.generatedAt).toLocaleString('pt-BR') : 'não informado'}</span><span>Motor: {completed.analysis.engineVersion ?? 'não informado'}</span><span>Provedor/modelo: {[completed.analysis.provider, completed.analysis.model].filter(Boolean).join(' · ') || 'não informado'}</span><span>Política: {completed.analysis.policyVersion ?? 'não informado'}</span><span>Versões dos assessments: {completed.analysis.assessmentVersions?.join(', ') || 'não informado'}</span><span>Evidências consideradas: {completed.analysis.evidenceCount ?? completed.evidence?.length ?? 0}</span></div><p className="mt-3 leading-5">O resultado é um cenário calculado com os dados disponíveis no momento. Confira os documentos e informações oficiais antes de decidir.</p>{completed.analysis.criteriaJson && <details className="mt-4 rounded-xl border border-slate-200 bg-slate-950/95 p-3 text-slate-200"><summary className="cursor-pointer font-semibold text-slate-100">Exibir JSON utilizado</summary><CopyCriteriaJsonButton value={completed.analysis.criteriaJson} /><pre className="mt-3 max-h-96 overflow-auto whitespace-pre-wrap break-words text-[11px] leading-5">{completed.analysis.criteriaJson}</pre></details>}</details>}
      <p role="note" className="mt-6 border-t border-slate-100 pt-4 text-xs leading-5 text-slate-500">As informações apresentadas pelo Prumo têm caráter exclusivamente informativo e educacional. Os cenários calculados não constituem recomendação ou aconselhamento financeiro. Considere seus objetivos e perfil de risco e valide as informações antes de investir. Você é responsável pela decisão final.</p>
    </div>
    {selected && <AppDialog title={`Registrar aporte em ${selected.ticker}`} description={`Cenário calculado: ${money(selected.amount, false)}. Informe o que foi efetivamente realizado.`} fields={[{ name: 'quantity', label: 'Quantidade comprada', defaultValue: '1', inputMode: 'decimal' }, { name: 'unitPrice', label: 'Preço unitário realizado', defaultValue: '0', inputMode: 'decimal' }, { name: 'totalPrice', label: 'Preço total', readOnly: true, getValue: values => money(parse(values.quantity) * parse(values.unitPrice), false) }]} confirmLabel="Confirmar aporte" onClose={() => setSelected(null)} onConfirm={values => { const quantity = parse(values.quantity), unitPrice = parse(values.unitPrice); if (quantity > 0 && unitPrice >= 0) confirm.mutate({ quantity, unitPrice }) }} />}
  </section>
}
