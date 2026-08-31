import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCircleCheck, faSpinner, faWandMagicSparkles } from '@fortawesome/free-solid-svg-icons'
import { AppDialog } from '../components/AppDialog'
import { apiBaseUrl } from '../api/client'

type Allocation = { assetId: string; ticker: string; assetClass: number; amount: number; reason: string }
type Guidance = { guidanceId: string; requestedAmount: number; allocations: Allocation[]; allocatedAmount: number; unallocatedAmount: number; reasons: string[]; jobStatus?: string }
type Position = { id: string }

const money = (value: number, hidden: boolean) => hidden ? 'R$ ••••••' : new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
const parse = (value: string) => Number(value.replace(/\./g, '').replace(',', '.'))

async function api<T>(path: string, body?: unknown) {
  const response = await fetch(apiBaseUrl + path, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : undefined)
  if (!response.ok) throw new Error(await response.text() || 'Não foi possível concluir a operação.')
  return response.json() as Promise<T>
}

export function ContributionSectionModern({ portfolioId, positions, privacyHidden }: { portfolioId: string; positions: Position[]; privacyHidden: boolean }) {
  const [amount, setAmount] = useState('')
  const [guidance, setGuidance] = useState<Guidance | null>(null)
  const [selected, setSelected] = useState<Allocation | null>(null)
  const generate = useMutation({
    mutationFn: () => api<Guidance>(`/api/portfolios/${portfolioId}/contribution-guidance`, { amount: parse(amount) }),
    onMutate: () => setGuidance(null),
    onSuccess: setGuidance
  })
  const polling = useQuery({
    queryKey: ['standalone-contribution', portfolioId, guidance?.guidanceId],
    queryFn: () => api<Guidance>(`/api/portfolios/${portfolioId}/contribution-guidance/${guidance?.guidanceId}`),
    enabled: Boolean(guidance?.guidanceId && guidance.jobStatus && !['Completed', 'Failed'].includes(guidance.jobStatus)),
    refetchInterval: query => ['Completed', 'Failed'].includes(query.state.data?.jobStatus ?? '') ? false : 1500
  })
  const current = polling.data ?? guidance
  const pending = generate.isPending || Boolean(current?.jobStatus && !['Completed', 'Failed'].includes(current.jobStatus))
  const completed = current?.jobStatus === 'Completed' ? current : null
  const failed = current?.jobStatus === 'Failed'

  const confirm = useMutation({
    mutationFn: (value: { quantity: number; unitPrice: number }) => api(`/api/portfolios/${portfolioId}/contributions`, { guidanceId: completed?.guidanceId, items: [{ assetId: selected?.assetId, ...value }] }),
    onSuccess: () => { setSelected(null); setGuidance(null) }
  })

  return <section className="mx-auto max-w-[89.5rem] px-6 pb-24 pt-3">
    <div className="rounded-[1.75rem] border border-slate-200 bg-white p-5 shadow-sm sm:p-6">
      <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Próximo aporte</p>
      <div className="mt-5 grid gap-6 lg:grid-cols-[.75fr_1.35fr_1fr] lg:gap-8">
        <div className="lg:border-r lg:border-slate-100 lg:pr-8">
          <p className="text-xs font-semibold uppercase tracking-[.14em] text-slate-500">Necessidade atual</p>
          <h1 className="mt-2 text-2xl font-semibold">Oriente seu próximo aporte.</h1>
          <form className="mt-5" onSubmit={event => { event.preventDefault(); generate.mutate() }}>
            <label className="text-sm font-medium">Quanto pretende aportar?</label>
            <div className="mt-2 flex gap-2">
              <input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} placeholder="R$ 0,00" className="min-w-0 flex-[1.15] rounded-xl border border-slate-300 bg-stone-50 px-4 py-3" />
              <button disabled={generate.isPending || !amount || !positions.length} className="inline-flex cursor-pointer items-center rounded-xl bg-slate-900 px-5 py-3 text-sm font-semibold text-white disabled:opacity-50">
                <FontAwesomeIcon icon={faWandMagicSparkles} className="mr-2" />{generate.isPending ? 'Enviando...' : 'Gerar orientação'}
              </button>
            </div>
          </form>
          {pending && <p role="status" className="mt-4 flex items-center gap-2 text-sm text-amber-700"><FontAwesomeIcon icon={faSpinner} spin /> A IA está analisando os ativos e definindo a distribuição...</p>}
          {failed && <p className="mt-4 text-sm text-rose-700">Não foi possível concluir a análise da IA. Tente novamente.</p>}
        </div>

        <div>{completed ? <>
          <div className="flex justify-between"><div><p className="text-xs font-semibold uppercase tracking-[.14em] text-slate-500">Onde aportar</p><h2 className="mt-1 text-lg font-semibold">Distribuição sugerida</h2></div><strong>{money(completed.allocatedAmount, privacyHidden)}</strong></div>
          <div className="mt-4 space-y-4">{completed.allocations.map(item => <div key={item.assetId}>
            <div className="flex justify-between text-sm"><strong>{item.ticker}</strong><span>{money(item.amount, privacyHidden)}</span></div>
            <div className="mt-2 h-2 rounded-full bg-slate-100"><div className="h-full rounded-full bg-emerald-500" style={{ width: `${Math.min(item.amount / completed.requestedAmount * 100, 100)}%` }} /></div>
            <p className="mt-1 text-xs text-slate-500">{item.reason}</p>
            <button type="button" onClick={() => setSelected(item)} className="mt-2 cursor-pointer rounded-full border border-slate-300 px-3 py-1.5 text-xs font-semibold hover:border-amber-500">Registrar aporte</button>
          </div>)}</div>
        </> : <div className="flex min-h-32 items-center rounded-2xl border border-dashed border-slate-200 bg-stone-50 p-5 text-sm text-slate-500">
          {pending ? 'A distribuição será exibida somente após a conclusão da análise da IA.' : 'Informe um valor para iniciar a análise da IA.'}
        </div>}</div>

        <div className="lg:border-l lg:border-slate-100 lg:pl-8">
          <p className="text-sm font-semibold">{pending ? 'Análise em andamento' : 'Resumo'}</p>
          {pending ? <div className="mt-4 space-y-3"><div className="h-3 animate-pulse rounded bg-slate-200" /><div className="h-3 animate-pulse rounded bg-slate-200" /><div className="h-3 w-3/5 animate-pulse rounded bg-slate-200" /></div>
            : <div className="mt-4 space-y-3 text-sm text-slate-600">{completed?.reasons.slice(0, 4).map((reason, index) => <div key={`${reason}-${index}`} className="flex gap-3"><FontAwesomeIcon icon={faCircleCheck} className="mt-1 text-emerald-500" /><span>{reason}</span></div>)}</div>}
        </div>
      </div>
    </div>
    {selected && <AppDialog title={`Registrar aporte em ${selected.ticker}`} description={`Sugestão: ${money(selected.amount, privacyHidden)}. Informe o que foi realizado.`} fields={[{ name: 'quantity', label: 'Quantidade comprada', defaultValue: '1', inputMode: 'decimal' }, { name: 'unitPrice', label: 'Preço unitário realizado', defaultValue: '0', inputMode: 'decimal' }]} confirmLabel="Confirmar aporte" onClose={() => setSelected(null)} onConfirm={values => { const quantity = parse(values.quantity), unitPrice = parse(values.unitPrice); if (quantity > 0 && unitPrice >= 0) confirm.mutate({ quantity, unitPrice }) }} />}
  </section>
}
