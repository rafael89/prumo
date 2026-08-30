// @ts-nocheck
import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Pagination } from '../components/Pagination'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUpRightFromSquare, faMagnifyingGlass } from '@fortawesome/free-solid-svg-icons'

const apiUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5041'
const priorityLabels: Record<string, string> = { Critical: 'Crítica', High: 'Alta', Medium: 'Média', Low: 'Baixa' }
const processingStatuses = ['Descoberto', 'Baixado', 'Aguardando processamento', 'Processando', 'Processado', 'Erro', 'Ignorado']
const documentTypes = ['Fato relevante', 'Comunicado', 'Relatório gerencial', 'Outro']
const importanceLabels = { Critical: 'Cr\u00edtico', Relevant: 'Relevante', Informative: 'Informativo', NoAction: 'Sem\u00e7\u00e3o' }
const classificationLabels = { CorporateAction: 'A\u00e7\u00e3o corporativa', FinancialResult: 'Resultado financeiro', Distribution: 'Distribui\u00e7\u00e3o', CapitalRaise: 'Capta\u00e7\u00e3o', AssetTransaction: 'Transa\u00e7\u00e3o de ativos', Governance: 'Governan\u00e7a', Operational: 'Operacional', Liquidation: 'Liquida\u00e7\u00e3o', Other: 'Outro' }
const actionLabels = { Maintain: 'Manter', IncreasePosition: 'Aumentar posi\u00e7\u00e3o', ReduceExposure: 'Reduzir exposi\u00e7\u00e3o', EvaluateSale: 'Avaliar venda', Monitor: 'Acompanhar', NoAction: 'Sem a\u00e7\u00e3o', OpenPosition: 'Abrir posi\u00e7\u00e3o' }
async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`)
  if (!response.ok) throw new Error('Não foi possível carregar os dados.')
  return response.json()
}

async function getOptionalJson<T>(path: string): Promise<T | null> {
  const response = await fetch(`${apiUrl}${path}`)
  if (response.status === 404) return null
  if (!response.ok) throw new Error('Não foi possível carregar os dados.')
  return response.json()
}

function formatCurrency(value: number | null | undefined, hidden = false) {
  if (hidden) return 'R$ ••••••'
  if (value == null) return 'Sem valor'
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value)
}

function formatDate(value: string | null) {
  if (!value) return '-'
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function analysisTone(importance: string) {
  if (importance === 'Critical') return 'border-rose-300 bg-rose-50 text-rose-900'
  if (importance === 'Relevant') return 'border-amber-300 bg-amber-50 text-amber-900'
  if (importance === 'Informative') return 'border-sky-300 bg-sky-50 text-sky-900'
  return 'border-slate-300 bg-white text-slate-900'
}

export function DocumentsSection({ portfolioId, selectedDocumentId, setSelectedDocumentId, privacyHidden }: any) {
  const [documentPage, setDocumentPage] = useState(1)
  const [documentFilter, setDocumentFilter] = useState('')
  const [documentStatus, setDocumentStatus] = useState('')
  const [documentTypeFilter, setDocumentTypeFilter] = useState('')

  useEffect(() => {
    setDocumentPage(1)
    setSelectedDocumentId(null)
  }, [portfolioId, setSelectedDocumentId])

  const documents = useQuery({
    queryKey: ['documents', portfolioId, documentPage, documentFilter, documentStatus, documentTypeFilter],
    queryFn: () => getJson(`/api/portfolios/${portfolioId}/documents?page=${documentPage}&pageSize=10&asset=${encodeURIComponent(documentFilter)}${documentStatus ? `&status=${documentStatus}` : ''}${documentTypeFilter ? `&type=${documentTypeFilter}` : ''}`),
    enabled: Boolean(portfolioId),
    refetchInterval: query => query.state.data?.items?.some((item: any) => [0, 1, 2, 3].includes(item.processingStatus)) ? 2000 : false,
  })

  const selectedDocument = documents.data?.items.find((item) => item.id === selectedDocumentId) ?? null
  const analysis = useQuery({
    queryKey: ['document-analysis', selectedDocument?.id ?? null, portfolioId],
    queryFn: () => selectedDocument ? getOptionalJson(`/api/documents/${selectedDocument.id}/analysis?portfolioId=${portfolioId}`) : Promise.resolve(null),
    enabled: Boolean(selectedDocument?.id),
    refetchInterval: selectedDocument && [0, 1, 2, 3].includes(selectedDocument.processingStatus) ? 2000 : false,
  })

  if (!portfolioId) return null

  return (
    <div className="order-4 mt-0 grid min-w-0 gap-8 self-start xl:col-start-2 xl:row-start-2 [&>*]:min-w-0">
      <div id="documentos" className="rounded-[1.75rem] border border-slate-200 bg-white p-5 shadow-sm">
        <div className="flex items-center justify-between gap-4">
          <h2 className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Documentos recentes</h2>
          <span className="text-xs text-slate-500">{documents.data?.totalItems ?? 0} encontrados</span>
        </div>
        <div className="mt-4 grid min-w-0 gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(8.5rem,.8fr)_minmax(8.5rem,.8fr)]">
          <div className="relative min-w-0">
            <label htmlFor="document-filter" className="sr-only">Filtrar documentos por ativo</label>
            <FontAwesomeIcon icon={faMagnifyingGlass} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-xs text-slate-500" />
            <input id="document-filter" value={documentFilter} onChange={(event) => { setDocumentFilter(event.target.value); setDocumentPage(1) }} placeholder="Buscar ativo..." className="w-full min-w-0 rounded-xl border border-slate-300 py-2 pl-9 pr-3 text-sm outline-none focus:border-amber-500" />
          </div>
          <select aria-label="Filtrar documentos por status" value={documentStatus} onChange={(event) => { setDocumentStatus(event.target.value); setDocumentPage(1) }} className="w-full min-w-0 rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm"><option value="">Todos os status</option>{processingStatuses.map((label, index) => <option key={label} value={index}>{label}</option>)}</select>
          <select aria-label="Filtrar documentos por tipo" value={documentTypeFilter} onChange={(event) => { setDocumentTypeFilter(event.target.value); setDocumentPage(1) }} className="w-full min-w-0 rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm"><option value="">Todos os tipos</option>{documentTypes.map((label, index) => <option key={label} value={index}>{label}</option>)}</select>
        </div>
        {documents.isLoading && <p className="py-5 text-sm text-slate-500">Carregando documentos...</p>}
        {!documents.isLoading && documents.data?.items.length === 0 && <p className="py-5 text-sm text-slate-500">Nenhum documento encontrado.</p>}
        <div className="mt-3 divide-y divide-slate-200">
          {documents.data?.items.map((document) => {
            const isSelected = document.id === selectedDocument?.id
            return <button key={document.id} type="button" onClick={() => setSelectedDocumentId(document.id)} className={`flex w-full items-center justify-between gap-4 py-3 text-left text-sm transition ${isSelected ? 'text-slate-950' : 'text-slate-700 hover:text-slate-950'}`}>
              <div className="min-w-0"><strong>{document.ticker}</strong><span className="ml-3 text-slate-500">{document.sourceName}</span><p className="mt-1 truncate text-xs text-slate-500">{formatDate(document.publishedAt)} - {documentTypes[document.documentType] ?? 'Documento'}</p></div>
              <span className={`shrink-0 rounded-full px-3 py-1 text-xs ${isSelected ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-600'}`}>{processingStatuses[document.processingStatus] ?? 'Desconhecido'}</span>
            </button>
          })}
        </div>
        {documents.data && <Pagination page={documents.data.page} totalPages={documents.data.totalPages} onPageChange={setDocumentPage} />}
      </div>
      <section className="rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
        {!selectedDocument && <p className="text-sm text-slate-500">Selecione um documento para ver os detalhes.</p>}
        {selectedDocument && <div className="space-y-6"><div className="border-b border-slate-200 pb-4"><p className="text-sm font-semibold uppercase tracking-[0.16em] text-slate-500">Detalhe do documento</p><h2 className="mt-3 text-3xl font-semibold">{selectedDocument.ticker}</h2><p className="mt-2 text-sm text-slate-500">{selectedDocument.assetName} - {selectedDocument.sourceName}</p><div className="mt-4 grid gap-2 text-sm text-slate-600 md:grid-cols-2"><span>Publicado em {formatDate(selectedDocument.publishedAt)}</span><span>Status {processingStatuses[selectedDocument.processingStatus] ?? 'Desconhecido'}</span><span>Processado em {formatDate(selectedDocument.processedAt)}</span><a href={selectedDocument.sourceUrl} target="_blank" rel="noreferrer" className="text-amber-700 hover:text-amber-800"><FontAwesomeIcon icon={faArrowUpRightFromSquare} className="mr-2" />Abrir origem</a></div></div>
          {analysis.isLoading && <p className="text-sm text-slate-500">Carregando an&aacute;lise...</p>}{analysis.isError && <p className="text-sm text-rose-700">N&atilde;o foi poss&iacute;vel carregar a an&aacute;lise deste documento.</p>}{!analysis.isLoading && !analysis.isError && !analysis.data && <div className="rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">Nenhuma an&aacute;lise validada foi encontrada para este documento ainda.</div>}
          {analysis.data && <div className={`rounded-[1.5rem] border p-5 ${analysisTone(analysis.data.importance)}`}><div className="grid gap-4 md:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">Import&acirc;ncia</p><p className="mt-2 text-2xl font-semibold">{importanceLabels[analysis.data.importance] ?? analysis.data.importance}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">Classifica&ccedil;&atilde;o</p><p className="mt-2 text-lg font-semibold">{classificationLabels[analysis.data.classification] ?? analysis.data.classification}</p></div></div><div className="mt-6 grid gap-5"><article><p className="text-xs font-semibold uppercase tracking-[0.16em]">Resumo</p><p className="mt-2 text-sm leading-7">{analysis.data.summary}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.16em]">Impacto</p><p className="mt-2 text-sm leading-7">{analysis.data.impact}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.16em]">Impacto na minha carteira</p><p className="mt-2 text-sm leading-7">{analysis.data.portfolioAssessment?.portfolioImpactText ?? analysis.data.portfolioImpact}</p>{analysis.data.portfolioAssessment && <div className="mt-3 flex flex-wrap gap-2 text-xs font-semibold"><span className="rounded-full border border-current/20 px-3 py-1">Prioridade {priorityLabels[analysis.data.portfolioAssessment.priority] ?? analysis.data.portfolioAssessment.priority}</span><span className="rounded-full border border-current/20 px-3 py-1">Peso {analysis.data.portfolioAssessment.currentWeight.toFixed(2)}%</span><span className="rounded-full border border-current/20 px-3 py-1">Valor {formatCurrency(analysis.data.portfolioAssessment.currentValue, privacyHidden)}</span>{analysis.data.portfolioAssessment.requiresAttention && <span className="rounded-full bg-amber-100 px-3 py-1">Requer aten&ccedil;&atilde;o</span>}</div>}</article></div><div className="mt-6 grid gap-4 border-t border-current/10 pt-5 md:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">A&ccedil;&atilde;o sugerida</p><p className="mt-2 text-lg font-semibold">{actionLabels[analysis.data.suggestedAction] ?? analysis.data.suggestedAction}</p><p className="mt-1 text-sm opacity-75">{analysis.data.requiresAction ? "Requer aten&ccedil;&atilde;o" : "N&atilde;o requer a&ccedil;&atilde;o imediata"}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">Confian&ccedil;a</p><p className="mt-2 text-lg font-semibold">{Math.round(analysis.data.confidence * 100)}%</p><p className="mt-1 text-sm opacity-75">Modelo {analysis.data.model} - Prompt {analysis.data.promptVersion}</p></div></div><div className="mt-6"><p className="text-xs font-semibold uppercase tracking-[0.16em]">Ativos afetados</p><div className="mt-3 flex flex-wrap gap-2">{analysis.data.affectedAssets.length > 0 ? analysis.data.affectedAssets.map((asset) => <span key={asset} className="rounded-full border border-current/15 px-3 py-1 text-xs font-semibold">{asset}</span>) : <span className="text-sm opacity-75">Nenhum ativo listado.</span>}</div><p className="mt-4 text-xs opacity-75">An&aacute;lise criada em {formatDate(analysis.data.createdAt)}</p></div></div>}
        </div>}
      </section>
    </div>
  )
}
