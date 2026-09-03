import { useState, type Dispatch, type FormEvent, type SetStateAction } from 'react'
import type { UseMutationResult, UseQueryResult } from '@tanstack/react-query'
import { Pagination } from '../components/Pagination'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faEye, faEyeSlash, faPen, faTrash, faPlus } from '@fortawesome/free-solid-svg-icons'
import { AppDialog } from '../components/AppDialog'
type AssetType = 0 | 1 | 2 | 3
type PortfolioItem = { id: string; name: string; totalValue: number; positionCount: number }
type Position = { id: string; assetId: string; assetClass: number; ticker: string; name: string; assetType: AssetType; sector: string | null; quantity: number; averagePrice: number; currentPrice: number | null; currentValue: number | null; portfolioWeight: number }
type Feedback = { tone: 'success' | 'error'; message: string } | null
type PositionForm = { ticker: string; name: string; assetType: AssetType; sector: string; quantity: string; averagePrice: string; currentPrice: string }
type EditForm = { quantity: string; averagePrice: string; currentPrice: string }
type Mutation<TVariables> = Pick<UseMutationResult<unknown, Error, TVariables>, 'isPending' | 'mutate'>
type DashboardData = { positions: Position[]; page: number; totalPages: number }
type PortfolioSectionProps = {
  portfolios: UseQueryResult<PortfolioItem[], Error>; activePortfolio: PortfolioItem | null; portfolioName: string; setPortfolioName: (value: string) => void; handleCreatePortfolio: (event: FormEvent<HTMLFormElement>) => void
  createPortfolio: Mutation<{ name: string }>; deletePortfolio: Mutation<string>; renamePortfolio: Mutation<{ portfolioId: string; name: string }>; editingPortfolioId: string | null; setEditingPortfolioId: (id: string | null) => void; editingPortfolioName: string; setEditingPortfolioName: (value: string) => void
  setSelectedPortfolioId: (id: string | null) => void; setSelectedDocumentId: (id: string | null) => void; setPositionPage: (page: number) => void; setFeedback: (value: Feedback) => void; privacyHidden: boolean; togglePrivacy: () => void; positionForm: PositionForm; setPositionForm: Dispatch<SetStateAction<PositionForm>>; assetTypeOptions: Array<{ value: AssetType; label: string }>; handleCreatePosition: (event: FormEvent<HTMLFormElement>) => void
  createPosition: Mutation<{ ticker: string; name: string; assetType: AssetType; sector: string | null; quantity: number; averagePrice: number; currentPrice: number | null }>; dashboard: UseQueryResult<DashboardData, Error>; editingPositionId: string | null; startEditing: (position: Position) => void; handleUpdatePosition: (event: FormEvent<HTMLFormElement>, positionId: string) => void; editForm: EditForm; setEditForm: Dispatch<SetStateAction<EditForm>>
  updatePosition: Mutation<{ positionId: string; request: { quantity: number; averagePrice: number; currentPrice: number | null } }>; setEditingPositionId: (id: string | null) => void; deletePosition: Mutation<string>; selectedAsset: Position | null; setSelectedAssetId: (id: string | null) => void
}
function formatCurrency(value: number | null | undefined, hidden = false) { if (hidden) return 'R$ ••••••'; if (value == null) return 'Sem valor'; return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value) }
function formatNumber(value: number | null | undefined) { if (value == null) return '-'; return new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 }).format(value) }
const assetTypeLabels: Record<number, string> = { 0: 'A\u00e7\u00e3o', 1: 'FII', 2: 'ETF', 3: 'Outro' }
export function PortfolioSection({ portfolios, activePortfolio, portfolioName, setPortfolioName, handleCreatePortfolio, createPortfolio, deletePortfolio, renamePortfolio, editingPortfolioId, setEditingPortfolioId, editingPortfolioName, setEditingPortfolioName, setSelectedPortfolioId, setSelectedDocumentId, setPositionPage, setFeedback, privacyHidden, togglePrivacy, positionForm, setPositionForm, assetTypeOptions, handleCreatePosition, createPosition, dashboard, editingPositionId, startEditing, handleUpdatePosition, editForm, setEditForm, updatePosition, setEditingPositionId, deletePosition, selectedAsset, setSelectedAssetId }: PortfolioSectionProps) {
  const [showCreateForm, setShowCreateForm] = useState(false)
  const [portfolioToDelete, setPortfolioToDelete] = useState<PortfolioItem | null>(null)
  return <>
      <div className="contents">
        <section id="carteira" className="order-1 self-stretch rounded-[1.75rem] border border-slate-200 bg-white p-5 shadow-sm xl:col-start-1 xl:row-start-1">
          <div className="flex items-center justify-between gap-4">
            <h2 className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Carteiras</h2>
            <button type="button" aria-label={privacyHidden ? 'Exibir valores' : 'Ocultar valores'} title={privacyHidden ? 'Exibir valores' : 'Ocultar valores'} onClick={togglePrivacy} className="inline-flex h-10 w-10 items-center justify-center rounded-xl border border-slate-300 bg-white text-sm text-slate-700 transition hover:border-amber-400 hover:text-amber-700">{privacyHidden ? <FontAwesomeIcon icon={faEyeSlash} /> : <FontAwesomeIcon icon={faEye} />}</button>
          </div>

          <div className="mt-3 grid grid-cols-[repeat(auto-fit,minmax(13rem,1fr))] gap-2">
            {portfolios.data?.map((portfolio: PortfolioItem) => {
              const isActive = portfolio.id === activePortfolio?.id
              return (
                <div key={portfolio.id} className={`relative flex min-h-24 rounded-xl border bg-white p-3 transition ${isActive ? 'border-slate-900 ring-1 ring-slate-900' : 'border-slate-200 hover:border-amber-300 hover:bg-amber-50/40'}`}>
                  {editingPortfolioId === portfolio.id ? <form className="flex min-w-0 flex-1 flex-col justify-center" onSubmit={(event) => { event.preventDefault(); const name = editingPortfolioName.trim(); if (name) renamePortfolio.mutate({ portfolioId: portfolio.id, name }) }}><input autoFocus value={editingPortfolioName} onChange={(event) => setEditingPortfolioName(event.target.value)} aria-label="Nome da carteira" className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm" /><div className="mt-2 flex gap-2"><button type="submit" disabled={renamePortfolio.isPending} className="rounded-full bg-slate-900 px-3 py-1 text-xs font-semibold text-white">Salvar</button><button type="button" onClick={() => setEditingPortfolioId(null)} className="rounded-full border border-slate-300 px-3 py-1 text-xs">Cancelar</button></div></form> : <>
                    <button type="button" title="Duplo clique para renomear" onClick={() => { setSelectedPortfolioId(portfolio.id); setSelectedDocumentId(null); setPositionPage(1); setFeedback(null) }} onDoubleClick={() => { setEditingPortfolioId(portfolio.id); setEditingPortfolioName(portfolio.name) }} className="min-w-0 flex-1 pr-12 text-left">
                      <div className="flex flex-wrap items-center gap-2"><strong className="truncate text-sm">{portfolio.name}</strong>{isActive && <span className="rounded-full bg-slate-900 px-2 py-1 text-[10px] font-bold uppercase tracking-wide text-white">Ativa</span>}</div>
                      <div className="mt-3 flex flex-wrap items-center gap-2"><span className="text-sm text-slate-500">{formatCurrency(portfolio.totalValue, privacyHidden)}</span><span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs text-slate-600">{portfolio.positionCount} posi&ccedil;&otilde;es</span></div>
                    </button>
                    <button type="button" aria-label={`Excluir carteira ${portfolio.name}`} title="Excluir carteira" onClick={() => setPortfolioToDelete(portfolio)} disabled={deletePortfolio.isPending} className="absolute right-3 top-1/2 h-10 w-10 -translate-y-1/2 rounded-xl border border-rose-200 text-sm text-rose-700 transition hover:border-rose-300 hover:bg-rose-50 disabled:opacity-50"><FontAwesomeIcon icon={faTrash} /></button>
                  </>}
                </div>
              )
            })}
            {!showCreateForm && <button type="button" onClick={() => setShowCreateForm(true)} className="flex min-h-24 items-center justify-center gap-3 rounded-xl border border-dashed border-slate-300 px-4 text-sm font-semibold text-slate-600 transition hover:border-amber-400 hover:text-amber-700"><span className="text-2xl font-light">+</span> Nova carteira</button>}
            {showCreateForm && <form onSubmit={(event) => { handleCreatePortfolio(event); setShowCreateForm(false) }} className="flex min-h-24 flex-col justify-center rounded-xl border border-dashed border-amber-400 bg-amber-50/40 p-3"><label htmlFor="portfolio-name" className="text-xs font-medium text-slate-700">Nome da carteira</label><input id="portfolio-name" autoFocus value={portfolioName} onChange={(event) => setPortfolioName(event.target.value)} className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm outline-none focus:border-amber-500" placeholder="Minha carteira" /><div className="mt-2 flex gap-2"><button type="submit" disabled={createPortfolio.isPending} className="rounded-full bg-slate-900 px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-60">{createPortfolio.isPending ? 'Criando...' : 'Criar'}</button><button type="button" onClick={() => setShowCreateForm(false)} className="rounded-full border border-slate-300 px-3 py-1.5 text-xs">Cancelar</button></div></form>}
          </div>
        </section>

        <section className="order-3 self-start rounded-[1.75rem] border border-slate-200 bg-white p-5 shadow-sm xl:col-start-1 xl:row-start-2">
          <div className="flex items-center justify-between gap-4">
            <h2 className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Posi&ccedil;&otilde;es</h2>
            <span className="text-xs text-slate-500">{activePortfolio?.name ?? 'Nenhuma carteira selecionada'}</span>
          </div>

          {!activePortfolio && (
            <div className="mt-5 rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">
              Crie uma carteira para come&ccedil;ar a cadastrar ativos.
            </div>
          )}

          {activePortfolio && (
            <>
              <form onSubmit={handleCreatePosition} className="mt-4 grid gap-3 md:grid-cols-5 md:items-end">
                <div>
                  <label htmlFor="ticker" className="text-sm font-medium text-slate-700">Ticker</label>
                  <input id="ticker" value={positionForm.ticker} onChange={(event) => setPositionForm((current: PositionForm) => ({ ...current, ticker: event.target.value.toUpperCase() }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="BBSE3" />
                </div>
                <div>
                  <label htmlFor="asset-type" className="text-sm font-medium text-slate-700">Tipo do ativo</label>
                  <select id="asset-type" value={positionForm.assetType} onChange={(event) => setPositionForm((current: PositionForm) => ({ ...current, assetType: Number(event.target.value) as AssetType }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white">
                    {assetTypeOptions.map((option: { value: AssetType; label: string }) => <option key={option.value} value={option.value}>{option.label}</option>)}
                  </select>
                </div>
                <div>
                  <label htmlFor="quantity" className="text-sm font-medium text-slate-700">Quantidade</label>
                  <input id="quantity" inputMode="decimal" value={positionForm.quantity} onChange={(event) => setPositionForm((current: PositionForm) => ({ ...current, quantity: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="100" />
                </div>
                <div>
                  <label htmlFor="current-price" className="text-sm font-medium text-slate-700">Pre&ccedil;o de refer&ecirc;ncia</label>
                  <input id="current-price" inputMode="decimal" value={positionForm.currentPrice} onChange={(event) => setPositionForm((current: PositionForm) => ({ ...current, currentPrice: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="31,40" />
                </div>
                <div className="flex items-end">
                  <button type="submit" disabled={createPosition.isPending} className="h-11.5 w-full rounded-xl bg-slate-900 px-5 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:cursor-not-allowed disabled:opacity-60">
                    <FontAwesomeIcon icon={faPlus} className="mr-2" />{createPosition.isPending ? 'Salvando...' : 'Adicionar'}
                  </button>
                </div>
              </form>

              {dashboard.isLoading && <p className="mt-6 text-sm text-slate-500">Carregando posi&ccedil;&otilde;es...</p>}
              {dashboard.isError && <p className="mt-6 text-sm text-rose-700">N&atilde;o foi poss&iacute;vel carregar o dashboard desta carteira.</p>}
              {!dashboard.isLoading && dashboard.data?.positions.length === 0 && (
                <div className="mt-6 rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">
                  Nenhuma posi&ccedil;&atilde;o cadastrada ainda para esta carteira.
                </div>
              )}

              {dashboard.data && dashboard.data.positions.length > 0 && (
                <div className="mt-4 overflow-hidden rounded-xl border border-slate-200">
                  <div className="hidden grid-cols-[minmax(0,1.5fr)_minmax(3rem,.55fr)_minmax(3.5rem,.6fr)_minmax(5rem,.85fr)_minmax(6rem,1fr)_minmax(3rem,.55fr)_5.5rem] gap-3 bg-slate-50 px-4 py-3 text-xs font-semibold uppercase tracking-[0.14em] text-slate-500 md:grid">
                    <span>Ativo</span>
                    <span>Tipo</span>
                    <span className="text-right">Qtde</span>
                    <span className="text-right" title="Preço informado na última atualização da carteira.">Pre&ccedil;o ref.</span>
                    <span className="text-right">Valor atual</span>
                    <span className="text-right">Peso</span>
                    <span className="text-center">A&ccedil;&otilde;es</span>
                  </div>
                  <div className="divide-y divide-slate-200 bg-white">
                    {dashboard.data.positions.map((position: Position) => (
                      <div key={position.id} className="px-4 py-3">
                        {editingPositionId === position.id ? (
                          <form onSubmit={(event) => handleUpdatePosition(event, position.id)} className="grid gap-3 md:grid-cols-[minmax(0,1.5fr)_minmax(3rem,.55fr)_minmax(3.5rem,.6fr)_minmax(5rem,.85fr)_minmax(6rem,1fr)_minmax(3rem,.55fr)_5.5rem] md:items-end">
                            <div>
                              <p className="text-sm font-semibold">{position.ticker}</p>
                              <p className="text-xs text-slate-500">{position.name}</p>
                            </div>
                            <div className="text-sm text-slate-600">{assetTypeLabels[position.assetType]}</div>
                            <input value={editForm.quantity} onChange={(event) => setEditForm((current: EditForm) => ({ ...current, quantity: event.target.value }))} className="min-w-0 rounded-xl border border-slate-300 px-2 py-2 text-right text-sm" />
                            <input value={editForm.currentPrice} onChange={(event) => setEditForm((current: EditForm) => ({ ...current, currentPrice: event.target.value }))} className="min-w-0 rounded-xl border border-slate-300 px-2 py-2 text-right text-sm" />
                            <div className="text-right text-sm text-slate-500">{formatCurrency(position.currentValue, privacyHidden)}</div>
                            <div className="text-right text-sm text-slate-500">{position.portfolioWeight.toFixed(2)}%</div>
                            <div className="flex flex-col items-stretch gap-1">
                              <button type="submit" disabled={updatePosition.isPending} className="rounded-full bg-slate-900 px-3 py-2 text-xs font-semibold text-white hover:bg-amber-700 disabled:opacity-60">Salvar</button>
                              <button type="button" onClick={() => setEditingPositionId(null)} className="rounded-full border border-slate-300 px-3 py-2 text-xs font-semibold text-slate-700">Cancelar</button>
                            </div>
                          </form>
                        ) : (
                          <div className="grid gap-3 md:grid-cols-[minmax(0,1.5fr)_minmax(3rem,.55fr)_minmax(3.5rem,.6fr)_minmax(5rem,.85fr)_minmax(6rem,1fr)_minmax(3rem,.55fr)_5.5rem] md:items-center">
                            <div>
                              <p className="text-sm font-semibold">{position.ticker}</p>
                              <p className="text-xs text-slate-500">{position.name}</p>
                            </div>
                            <div className="text-sm text-slate-600">{assetTypeLabels[position.assetType]}</div>
                            <div className="text-right text-sm text-slate-600">{formatNumber(position.quantity)}</div>
                            <div className="text-right text-sm text-slate-600">{formatCurrency(position.currentPrice, privacyHidden)}</div>
                            <div className="text-right text-sm text-slate-900">{formatCurrency(position.currentValue, privacyHidden)}</div>
                            <div className="text-right text-sm text-slate-600">{position.portfolioWeight.toFixed(2)}%</div>
                            <div className="flex justify-center gap-2">
                              <button type="button" aria-label="Editar posição" title="Editar posição" onClick={() => startEditing(position)} className="h-9 w-9 rounded-full border border-slate-300 text-slate-700 hover:border-amber-400 hover:text-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500"><FontAwesomeIcon icon={faPen} /></button>
                              <button type="button" aria-label="Excluir posição" title="Excluir posição" onClick={() => deletePosition.mutate(position.id)} disabled={deletePosition.isPending} className="h-9 w-9 rounded-full border border-rose-200 text-rose-700 hover:bg-rose-50 focus:outline-none focus:ring-2 focus:ring-rose-500 disabled:opacity-60"><FontAwesomeIcon icon={faTrash} /></button>
                            </div>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
              {dashboard.data && <Pagination page={dashboard.data.page} totalPages={dashboard.data.totalPages} onPageChange={setPositionPage} />}
            </>
          )}
        </section>
      </div>
      {portfolioToDelete && <AppDialog title={`Excluir a carteira "${portfolioToDelete.name}"?`} description="Todas as posições e dados associados serão removidos. Esta ação não poderá ser desfeita." confirmLabel="Excluir carteira" tone="danger" onClose={() => setPortfolioToDelete(null)} onConfirm={() => { deletePortfolio.mutate(portfolioToDelete.id); setPortfolioToDelete(null) }} />}
    {selectedAsset && <section className="mt-8 rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm" aria-labelledby="asset-detail-title"><div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-xs font-semibold uppercase tracking-[0.16em] text-amber-700">Detalhe do ativo</p><h2 id="asset-detail-title" className="mt-2 text-3xl font-semibold">{selectedAsset.ticker}</h2><p className="mt-1 text-sm text-slate-500">{selectedAsset.name} · {selectedAsset.assetType === 1 ? 'FII' : selectedAsset.assetType === 2 ? 'ETF' : selectedAsset.assetType === 0 ? 'Ação' : 'Outro'}</p></div><button type="button" onClick={() => setSelectedAssetId(null)} className="rounded-full border border-slate-300 px-3 py-2 text-xs font-semibold">Fechar</button></div><div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4"><div><p className="text-xs text-slate-500">Quantidade</p><p className="mt-1 font-semibold">{formatNumber(selectedAsset.quantity)}</p></div><div><p className="text-xs text-slate-500">Preço de referência</p><p className="mt-1 font-semibold">{formatCurrency(selectedAsset.currentPrice, privacyHidden)}</p></div><div><p className="text-xs text-slate-500">Valor da posição</p><p className="mt-1 font-semibold">{formatCurrency(selectedAsset.currentValue, privacyHidden)}</p></div><div><p className="text-xs text-slate-500">Peso na carteira</p><p className="mt-1 font-semibold">{selectedAsset.portfolioWeight.toFixed(2)}%</p></div></div><div className="mt-6 border-t border-slate-200 pt-5"><p className="text-sm font-semibold">Eventos e documentos</p><p className="mt-2 text-sm text-slate-600">Consulte a lista paginada de documentos para abrir os eventos deste ativo.</p></div></section>}
  </>
}
