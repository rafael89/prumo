// @ts-nocheck
import { useState, type FormEvent } from 'react'
import { Pagination } from '../components/Pagination'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faPen, faTrash } from '@fortawesome/free-solid-svg-icons'
 type PortfolioSectionProps = { portfolios: any; activePortfolio: any; portfolioName: string; setPortfolioName: (value: string) => void; handleCreatePortfolio: (event: FormEvent<HTMLFormElement>) => void; createPortfolio: any; deletePortfolio: any; renamePortfolio: any; editingPortfolioId: string | null; setEditingPortfolioId: (id: string | null) => void; editingPortfolioName: string; setEditingPortfolioName: (value: string) => void; setSelectedPortfolioId: (id: string | null) => void; setSelectedDocumentId: (id: string | null) => void; setPositionPage: (page: number) => void; setFeedback: (value: any) => void; privacyHidden: boolean; positionForm: any; setPositionForm: (value: any) => void; assetTypeOptions: any; handleCreatePosition: (event: FormEvent<HTMLFormElement>) => void; createPosition: any; dashboard: any; editingPositionId: string | null; startEditing: (position: any) => void; handleUpdatePosition: (event: FormEvent<HTMLFormElement>, positionId: string) => void; editForm: any; setEditForm: (value: any) => void; updatePosition: any; setEditingPositionId: (id: string | null) => void; deletePosition: any; selectedAsset: any; setSelectedAssetId: (id: string | null) => void }
function formatCurrency(value: number | null | undefined, hidden = false) { if (hidden) return 'R$ ••••••'; if (value == null) return 'Sem valor'; return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value) }
function formatNumber(value: number | null | undefined) { if (value == null) return '-'; return new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 }).format(value) }
const assetTypeLabels: Record<number, string> = { 0: 'A\u00e7\u00e3o', 1: 'FII', 2: 'ETF', 3: 'Outro' }
export function PortfolioSection({ portfolios, activePortfolio, portfolioName, setPortfolioName, handleCreatePortfolio, createPortfolio, deletePortfolio, renamePortfolio, editingPortfolioId, setEditingPortfolioId, editingPortfolioName, setEditingPortfolioName, setSelectedPortfolioId, setSelectedDocumentId, setPositionPage, setFeedback, privacyHidden, positionForm, setPositionForm, assetTypeOptions, handleCreatePosition, createPosition, dashboard, editingPositionId, startEditing, handleUpdatePosition, editForm, setEditForm, updatePosition, setEditingPositionId, deletePosition, selectedAsset, setSelectedAssetId }: PortfolioSectionProps) {
  const [showCreateForm, setShowCreateForm] = useState(false)
  return <>
      <div className="min-w-0 space-y-8">
        <section id="carteira" className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
          <div className="flex items-baseline justify-between border-b border-slate-200 pb-3">
            <h2 className="text-2xl font-semibold">Carteiras</h2>
            <span className="text-sm text-slate-500">{portfolios.data?.length ?? 0} cadastradas</span>
          </div>

          {!showCreateForm && <button type="button" onClick={() => setShowCreateForm(true)} className="mt-4 rounded-xl border border-dashed border-slate-300 px-4 py-3 text-sm font-semibold text-slate-600 hover:border-amber-400 hover:text-amber-700">+ Nova carteira</button>}
          {showCreateForm && <form onSubmit={(event) => { handleCreatePortfolio(event); setShowCreateForm(false) }} className="mt-4 flex items-end gap-3">
            <div>
              <label htmlFor="portfolio-name" className="text-sm font-medium text-slate-700">Nome da carteira</label>
              <input
                id="portfolio-name"
                value={portfolioName}
                onChange={(event) => setPortfolioName(event.target.value)}
                className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white"
                placeholder="Minha carteira"
              />
            </div>
            <button
              type="submit"
              disabled={createPortfolio.isPending}
              className="rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {createPortfolio.isPending ? 'Criando...' : 'Criar carteira'}
            </button>
          </form>}

          <div className="mt-6 space-y-3">
            {portfolios.data?.length === 0 && !portfolios.isLoading && (
              <div className="rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-4 text-sm text-slate-600">
                Nenhuma carteira cadastrada ainda. Crie a primeira para adicionar posi&ccedil;&otilde;es e acompanhar documentos.
              </div>
            )}

            {portfolios.data?.map((portfolio) => {
              const isActive = portfolio.id === activePortfolio?.id
              return (
                <div key={portfolio.id} className={`flex items-center gap-3 rounded-2xl border px-4 py-3 transition ${isActive ? 'border-slate-900 bg-slate-900 text-white' : 'border-slate-200 bg-white text-slate-900 hover:border-amber-300 hover:bg-amber-50'}`}>
                  {editingPortfolioId === portfolio.id ? <form className="min-w-0 flex-1" onSubmit={(event) => { event.preventDefault(); const name = editingPortfolioName.trim(); if (name) renamePortfolio.mutate({ portfolioId: portfolio.id, name }) }}><input autoFocus value={editingPortfolioName} onChange={(event) => setEditingPortfolioName(event.target.value)} aria-label="Nome da carteira" className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm" /><div className="mt-2 flex gap-2"><button type="submit" disabled={renamePortfolio.isPending} className="rounded-full bg-slate-900 px-3 py-1 text-xs font-semibold text-white">Salvar</button><button type="button" onClick={() => setEditingPortfolioId(null)} className="rounded-full border border-slate-300 px-3 py-1 text-xs">Cancelar</button></div></form> : <button type="button" onClick={() => { setSelectedPortfolioId(portfolio.id); setSelectedDocumentId(null); setPositionPage(1); setFeedback(null) }} className="min-w-0 flex-1 text-left">
                  <div className="flex items-center justify-between gap-3">
                    <strong className="text-base">{portfolio.name}</strong>
                    <span className={`rounded-full px-3 py-1 text-xs ${isActive ? 'bg-white/10 text-white' : 'bg-slate-100 text-slate-600'}`}>
                      {portfolio.positionCount} posi&ccedil;&otilde;es
                    </span>
                  </div>
                  <p className={`mt-2 text-sm ${isActive ? 'text-slate-200' : 'text-slate-500'}`}>{formatCurrency(portfolio.totalValue, privacyHidden)}</p>
                  </button>}
                  {editingPortfolioId !== portfolio.id && <button type="button" aria-label="Renomear carteira" title="Renomear carteira" onClick={() => { setEditingPortfolioId(portfolio.id); setEditingPortfolioName(portfolio.name) }} className={`h-10 w-10 shrink-0 rounded-full border ${isActive ? 'border-white/30 text-white hover:bg-white/10' : 'border-slate-300 text-slate-700 hover:border-amber-400 hover:text-amber-700'}`}><FontAwesomeIcon icon={faPen} /></button>}
                  <button type="button" aria-label={`Excluir carteira ${portfolio.name}`} title="Excluir carteira" onClick={() => { if (window.confirm(`Excluir a carteira "${portfolio.name}"? Esta ação não poderá ser desfeita.`)) deletePortfolio.mutate(portfolio.id) }} disabled={deletePortfolio.isPending} className={`shrink-0 rounded-full border px-3 py-2 text-xs ${isActive ? 'border-white/30 text-white hover:bg-white/10' : 'border-rose-200 text-rose-700 hover:bg-rose-50'} disabled:opacity-50`}><FontAwesomeIcon icon={faTrash} /></button>
                </div>
              )
            })}
          </div>
        </section>

        <section className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
          <div className="flex items-baseline justify-between border-b border-slate-200 pb-3">
            <h2 className="text-2xl font-semibold">Posi&ccedil;&otilde;es</h2>
            <span className="text-sm text-slate-500">{activePortfolio?.name ?? 'Nenhuma carteira selecionada'}</span>
          </div>

          {!activePortfolio && (
            <div className="mt-5 rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">
              Crie uma carteira para come&ccedil;ar a cadastrar ativos.
            </div>
          )}

          {activePortfolio && (
            <>
              <form onSubmit={handleCreatePosition} className="mt-5 grid gap-3 md:grid-cols-4">
                <div>
                  <label htmlFor="ticker" className="text-sm font-medium text-slate-700">Ticker</label>
                  <input id="ticker" value={positionForm.ticker} onChange={(event) => setPositionForm((current) => ({ ...current, ticker: event.target.value.toUpperCase() }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="BBSE3" />
                </div>
                <div>
                  <label htmlFor="asset-name" className="text-sm font-medium text-slate-700">Nome do ativo</label>
                  <input id="asset-name" value={positionForm.name} onChange={(event) => setPositionForm((current) => ({ ...current, name: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="Brasilseguridade" />
                </div>
                <div>
                  <label htmlFor="asset-type" className="text-sm font-medium text-slate-700">Tipo do ativo</label>
                  <select id="asset-type" value={positionForm.assetType} onChange={(event) => setPositionForm((current) => ({ ...current, assetType: Number(event.target.value) as AssetType }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white">
                    {assetTypeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                  </select>
                </div>
                <div>
                  <label htmlFor="sector" className="text-sm font-medium text-slate-700">Setor</label>
                  <input id="sector" value={positionForm.sector} onChange={(event) => setPositionForm((current) => ({ ...current, sector: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="Seguros" />
                </div>
                <div>
                  <label htmlFor="quantity" className="text-sm font-medium text-slate-700">Quantidade</label>
                  <input id="quantity" inputMode="decimal" value={positionForm.quantity} onChange={(event) => setPositionForm((current) => ({ ...current, quantity: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="100" />
                </div>
                <div>
                  <label htmlFor="average-price" className="text-sm font-medium text-slate-700">Pre&ccedil;o m&eacute;dio</label>
                  <input id="average-price" inputMode="decimal" value={positionForm.averagePrice} onChange={(event) => setPositionForm((current) => ({ ...current, averagePrice: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="29,15" />
                </div>
                <div>
                  <label htmlFor="current-price" className="text-sm font-medium text-slate-700">Pre&ccedil;o de refer&ecirc;ncia</label>
                  <input id="current-price" inputMode="decimal" value={positionForm.currentPrice} onChange={(event) => setPositionForm((current) => ({ ...current, currentPrice: event.target.value }))} className="mt-2 w-full rounded-2xl border border-slate-300 bg-stone-50 px-4 py-3 text-sm outline-none transition focus:border-amber-500 focus:bg-white" placeholder="31,40" />
                </div>
                <div className="flex items-end">
                  <button type="submit" disabled={createPosition.isPending} className="w-full rounded-full bg-slate-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:cursor-not-allowed disabled:opacity-60">
                    {createPosition.isPending ? 'Salvando...' : 'Adicionar posi\u00e7\u00e3o'}
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
                <div className="mt-6 overflow-x-auto rounded-2xl border border-slate-200">
                  <div className="min-w-[980px] grid grid-cols-[1.2fr_.9fr_.8fr_.8fr_.9fr_.9fr_.7fr_.9fr] gap-3 bg-slate-50 px-4 py-3 text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">
                    <span>Ativo</span>
                    <span>Tipo</span>
                    <span>Qtde</span>
                    <span>Pre&ccedil;o m&eacute;dio</span>
                    <span title="Preço informado na última atualização da carteira.">Pre&ccedil;o ref.</span>
                    <span>Valor atual</span>
                    <span>Peso</span>
                    <span>A&ccedil;&otilde;es</span>
                  </div>
                  <div className="divide-y divide-slate-200 bg-white">
                    {dashboard.data.positions.map((position) => (
                      <div key={position.id} className="px-4 py-4">
                        {editingPositionId === position.id ? (
                          <form onSubmit={(event) => handleUpdatePosition(event, position.id)} className="grid gap-3 md:grid-cols-[1.1fr_.9fr_.8fr_.8fr_.9fr_.9fr_.7fr_.9fr] md:items-end">
                            <div>
                              <p className="text-sm font-semibold">{position.ticker}</p>
                              <p className="text-xs text-slate-500">{position.name}</p>
                            </div>
                            <div className="text-sm text-slate-600">{assetTypeLabels[position.assetType]}</div>
                            <input value={editForm.quantity} onChange={(event) => setEditForm((current) => ({ ...current, quantity: event.target.value }))} className="rounded-xl border border-slate-300 px-3 py-2 text-sm" />
                            <input value={editForm.averagePrice} onChange={(event) => setEditForm((current) => ({ ...current, averagePrice: event.target.value }))} className="rounded-xl border border-slate-300 px-3 py-2 text-sm" />
                            <input value={editForm.currentPrice} onChange={(event) => setEditForm((current) => ({ ...current, currentPrice: event.target.value }))} className="rounded-xl border border-slate-300 px-3 py-2 text-sm" />
                            <div className="text-sm text-slate-500">{formatCurrency(position.currentValue, privacyHidden)}</div>
                            <div className="text-sm text-slate-500">{position.portfolioWeight.toFixed(2)}%</div>
                            <div className="flex gap-2">
                              <button type="submit" disabled={updatePosition.isPending} className="rounded-full bg-slate-900 px-3 py-2 text-xs font-semibold text-white hover:bg-amber-700 disabled:opacity-60">Salvar</button>
                              <button type="button" onClick={() => setEditingPositionId(null)} className="rounded-full border border-slate-300 px-3 py-2 text-xs font-semibold text-slate-700">Cancelar</button>
                            </div>
                          </form>
                        ) : (
                          <div className="grid gap-3 md:grid-cols-[1.2fr_.9fr_.8fr_.8fr_.9fr_.9fr_.7fr_.9fr] md:items-center">
                            <div>
                              <p className="text-sm font-semibold">{position.ticker}</p>
                              <p className="text-xs text-slate-500">{position.name}</p>
                            </div>
                            <div className="text-sm text-slate-600">{assetTypeLabels[position.assetType]}</div>
                            <div className="text-sm text-slate-600">{formatNumber(position.quantity)}</div>
                            <div className="text-sm text-slate-600">{formatCurrency(position.averagePrice, privacyHidden)}</div>
                            <div className="text-sm text-slate-600">{formatCurrency(position.currentPrice, privacyHidden)}</div>
                            <div className="text-sm text-slate-900">{formatCurrency(position.currentValue, privacyHidden)}</div>
                            <div className="text-sm text-slate-600">{position.portfolioWeight.toFixed(2)}%</div>
                            <div className="flex gap-2">
                              <button type="button" aria-label="Editar posição" title="Editar posição" onClick={() => startEditing(position)} className="h-10 w-10 rounded-full border border-slate-300 text-slate-700 hover:border-amber-400 hover:text-amber-700 focus:outline-none focus:ring-2 focus:ring-amber-500"><FontAwesomeIcon icon={faPen} /></button>
                              <button type="button" aria-label="Excluir posição" title="Excluir posição" onClick={() => deletePosition.mutate(position.id)} disabled={deletePosition.isPending} className="h-10 w-10 rounded-full border border-rose-200 text-rose-700 hover:bg-rose-50 focus:outline-none focus:ring-2 focus:ring-rose-500 disabled:opacity-60"><FontAwesomeIcon icon={faTrash} /></button>
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
    {selectedAsset && <section className="mt-8 rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm" aria-labelledby="asset-detail-title"><div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-xs font-semibold uppercase tracking-[0.16em] text-amber-700">Detalhe do ativo</p><h2 id="asset-detail-title" className="mt-2 text-3xl font-semibold">{selectedAsset.ticker}</h2><p className="mt-1 text-sm text-slate-500">{selectedAsset.name} · {selectedAsset.assetType === 1 ? 'FII' : selectedAsset.assetType === 2 ? 'ETF' : selectedAsset.assetType === 0 ? 'Ação' : 'Outro'}</p></div><button type="button" onClick={() => setSelectedAssetId(null)} className="rounded-full border border-slate-300 px-3 py-2 text-xs font-semibold">Fechar</button></div><div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4"><div><p className="text-xs text-slate-500">Quantidade</p><p className="mt-1 font-semibold">{formatNumber(selectedAsset.quantity)}</p></div><div><p className="text-xs text-slate-500">Preço de referência</p><p className="mt-1 font-semibold">{formatCurrency(selectedAsset.currentPrice, privacyHidden)}</p></div><div><p className="text-xs text-slate-500">Valor da posição</p><p className="mt-1 font-semibold">{formatCurrency(selectedAsset.currentValue, privacyHidden)}</p></div><div><p className="text-xs text-slate-500">Peso na carteira</p><p className="mt-1 font-semibold">{selectedAsset.portfolioWeight.toFixed(2)}%</p></div></div><div className="mt-6 border-t border-slate-200 pt-5"><p className="text-sm font-semibold">Eventos e documentos</p><p className="mt-2 text-sm text-slate-600">Consulte a lista paginada de documentos para abrir os eventos deste ativo.</p></div></section>}
  </>
}
