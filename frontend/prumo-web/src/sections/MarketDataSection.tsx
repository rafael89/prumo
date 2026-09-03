import { useQueries } from '@tanstack/react-query'
import { getJson } from '../api/client'

type Position = { assetId: string; ticker: string }
type Price = { priceDate: string; close: number; currency: string; source: string; fetchedAt: string }
const money = (value: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)

export function MarketDataSection({ positions }: { positions: Position[] }) {
  const queries = useQueries({ queries: positions.map(position => ({ queryKey: ['latest-price', position.assetId], queryFn: () => getJson<Price>(`/api/assets/${position.assetId}/prices/latest`) })) })
  return <section className="mx-auto max-w-6xl px-4 pb-24 pt-8 sm:px-6"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Mercado</p><h1 className="mt-2 text-3xl font-semibold tracking-tight">Preços de mercado</h1><p className="mt-2 text-sm leading-6 text-slate-600">Último fechamento persistido, com data do mercado e data da consulta.</p><div className="mt-6 divide-y divide-slate-200 rounded-3xl border border-slate-200 bg-white shadow-sm">{positions.map((position, index) => { const price = queries[index].data; return <div key={position.assetId} className="flex flex-wrap items-center justify-between gap-3 px-5 py-4"><div><span className="font-semibold">{position.ticker}</span><p className="text-xs text-slate-500">{price ? `${price.source} · ${price.priceDate} · buscado ${new Date(price.fetchedAt).toLocaleString('pt-BR')}` : 'Market data unavailable'}</p></div><strong>{price ? money(price.close) : 'Indisponível'}</strong></div> })}</div></section>
}
