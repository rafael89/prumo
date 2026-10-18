import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { DocumentAnalysisContent } from '../components/DocumentAnalysisContent'
import { Pagination } from '../components/Pagination'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowUpRightFromSquare, faMagnifyingGlass, faRotate, faSpinner } from '@fortawesome/free-solid-svg-icons'

import { apiBaseUrl } from '../api/client'
const apiUrl = apiBaseUrl
type DocumentItem = { id: string; ticker: string; assetName: string; documentType: number; sourceType: string | null; sourceName: string; publishedAt: string; processingStatus: number; processedAt: string | null; sourceUrl: string; ignoredReason: string | null; lastError: string | null; jobStatus: string | null; retryAt: string | null }
type DocumentsResponse = { items: DocumentItem[]; page: number; totalItems: number; totalPages: number }
type ProcessingStatus = { failedCount: number }
type PortfolioAssessment = { priority: string; currentWeight: number; currentValue: number | null; requiresAttention: boolean; portfolioImpactText: string; actionRequirement: string; capitalDecisionImpact: string }
type DocumentAnalysis = { importance: string; classification: string; summary: string; impact: string; portfolioImpact: string; portfolioAssessment: PortfolioAssessment | null; suggestedAction: string; requiresAction: boolean; confidence: number; model: string; promptVersion: string; affectedAssets: string[]; createdAt: string; publishedAt: string }
type DocumentsSectionProps = { portfolioId: string; selectedDocumentId: string | null; setSelectedDocumentId: (id: string | null) => void }
const processingStatuses = ['Descoberto', 'Baixado', 'Aguardando análise', 'Analisando', 'Processado', 'Erro', 'Ignorado']
const documentTypes = ['Fato relevante', 'Comunicado', 'Relatório gerencial', 'Informe mensal', 'Informe trimestral', 'Outro']
const documentTypeOptions = ['Fato relevante', 'Comunicado', 'Relatório gerencial', 'Informe mensal', 'Informe trimestral', 'Outro']
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

function formatDate(value: string | null) {
  if (!value) return '-'
  return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function officialDocumentUrl(url: string) {
  return url.replace('/fnet/publico/downloadDocumento', '/fnet/publico/exibirDocumento');
}

function documentTypeLabel(document: DocumentItem) {
  if (document?.documentType === 5 && document.sourceType) return document.sourceType
  return documentTypeOptions[document?.documentType] ?? document?.sourceType ?? 'Documento'
}

function DocumentJobDetails({ document }: { document: DocumentItem | null }) {
  if (!document?.lastError && !document?.ignoredReason) return null
  return <div className={`document-job-details mt-8 mb-6 rounded-2xl border p-4 text-sm ${document.ignoredReason ? 'border-amber-200 bg-amber-50 text-amber-900' : 'border-rose-200 bg-rose-50 text-rose-900'}`}>{document.ignoredReason && <><p className="font-semibold">Documento ignorado</p><p className="mt-2 wrap-break-word">{document.ignoredReason}</p></>}{document.lastError && <><p className="font-semibold">Último erro ({document.jobStatus ?? 'Falha'})</p><p className="mt-2 wrap-break-word">{document.lastError}</p>{document.retryAt && <p className="mt-2 text-xs">Próxima tentativa: {formatDate(document.retryAt)}</p>}</>}</div>
}

export function DocumentsSection({ portfolioId, selectedDocumentId, setSelectedDocumentId }: DocumentsSectionProps) {
  const queryClient = useQueryClient()
  const [documentPage, setDocumentPage] = useState(1)
  const [documentFilter, setDocumentFilter] = useState('')
  const [documentStatus, setDocumentStatus] = useState('')
  const [documentTypeFilter, setDocumentTypeFilter] = useState('')
  const [errorFilter, setErrorFilter] = useState('')
  const reprocessFailed = useMutation({
    mutationFn: () => sendJson<{ requeuedCount: number }>(`/api/portfolios/${portfolioId}/documents/reprocess-failed`, { errorContains: errorFilter.trim() || null }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['documents'] }); await queryClient.invalidateQueries({ queryKey: ['document-processing-status'] }) },
  })
  const processingStatus = useQuery({ queryKey: ['document-processing-status', portfolioId], queryFn: () => getJson<ProcessingStatus>(`/api/portfolios/${portfolioId}/document-processing-status`), enabled: Boolean(portfolioId), refetchInterval: 2000 })

  const documents = useQuery({
    queryKey: ['documents', portfolioId, documentPage, documentFilter, documentStatus, documentTypeFilter],
    queryFn: () => getJson<DocumentsResponse>(`/api/portfolios/${portfolioId}/documents?page=${documentPage}&pageSize=10&asset=${encodeURIComponent(documentFilter)}${documentStatus ? `&status=${documentStatus}` : ''}${documentTypeFilter ? `&type=${documentTypeFilter}` : ''}`),
    enabled: Boolean(portfolioId),
    refetchInterval: query => query.state.data?.items?.some((item: DocumentItem) => [0, 1, 2, 3].includes(item.processingStatus)) ? 2000 : false,
  })

  const selectedDocumentFromPage = documents.data?.items.find((item) => item.id === selectedDocumentId) ?? null
  const selectedDocumentQuery = useQuery({
    queryKey: ['document', selectedDocumentId],
    queryFn: () => getJson<DocumentItem>(`/api/documents/${selectedDocumentId}`),
    enabled: Boolean(selectedDocumentId && !selectedDocumentFromPage),
  })
  const selectedDocument = selectedDocumentFromPage ?? selectedDocumentQuery.data ?? null

  useEffect(() => {
    if (!selectedDocument?.id) return

    const frame = window.requestAnimationFrame(() => {
      document.getElementById('document-details')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })

    return () => window.cancelAnimationFrame(frame)
  }, [selectedDocument?.id])

  const analysis = useQuery({
    queryKey: ['document-analysis', selectedDocument?.id ?? null, portfolioId],
    queryFn: () => selectedDocument ? getOptionalJson<DocumentAnalysis>(`/api/documents/${selectedDocument.id}/analysis?portfolioId=${portfolioId}`) : Promise.resolve(null),
    enabled: Boolean(selectedDocument?.id),
    refetchInterval: selectedDocument && [0, 1, 2, 3].includes(selectedDocument.processingStatus) ? 2000 : false,
  })
  useEffect(() => {
    const confidenceIcon = document.querySelector('[aria-label="Sobre a confiança da análise"]')
    if (!confidenceIcon || confidenceIcon.querySelector('[data-confidence-tooltip]')) return
    confidenceIcon.removeAttribute('title')
    confidenceIcon.classList.add('group', 'relative', 'inline-flex', 'cursor-help', 'rounded-full', 'focus:outline-none', 'focus:ring-2', 'focus:ring-amber-500')
    confidenceIcon.setAttribute('tabindex', '0')
    const tooltip = document.createElement('span')
    tooltip.dataset.confidenceTooltip = 'true'
    tooltip.setAttribute('role', 'tooltip')
    tooltip.className = 'pointer-events-none invisible absolute bottom-full left-1/2 z-10 mb-2 w-64 -translate-x-1/2 rounded-xl bg-slate-900 px-3 py-2 text-left text-xs font-normal leading-5 text-white opacity-0 shadow-xl transition group-hover:visible group-hover:opacity-100 group-focus:visible group-focus:opacity-100'
    tooltip.textContent = 'A confiança é uma estimativa da IA sobre a interpretação do documento. Não representa uma verificação factual independente.'
    confidenceIcon.appendChild(tooltip)
    return () => { tooltip.remove(); confidenceIcon.removeAttribute('tabindex'); confidenceIcon.removeAttribute('title') }
  }, [analysis.data, selectedDocument?.id])

  if (!portfolioId) return null

  return (
    <div className="grid w-full min-w-0 items-start gap-3 lg:grid-cols-2 *:min-w-0">
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
        {(processingStatus.data?.failedCount ?? 0) > 0 && <div className="mt-3 flex flex-wrap items-center gap-2 rounded-xl bg-rose-50 p-3 text-xs text-rose-900"><span className="font-semibold">{processingStatus.data?.failedCount ?? 0} documento(s) com falha</span><input value={errorFilter} onChange={(event) => setErrorFilter(event.target.value)} placeholder="Filtrar erro (opcional)..." className="min-w-48 flex-1 rounded-lg border border-rose-200 bg-white px-3 py-2 outline-none focus:border-rose-400" /><button type="button" onClick={() => reprocessFailed.mutate()} disabled={reprocessFailed.isPending} className="rounded-lg bg-rose-700 px-3 py-2 font-semibold text-white disabled:opacity-50"><FontAwesomeIcon icon={faRotate} className="mr-2" />{reprocessFailed.isPending ? 'Reprocessando...' : 'Reprocessar falhos'}</button>{reprocessFailed.data && <span>{reprocessFailed.data.requeuedCount} job(s) reaberto(s).</span>}</div>}
        {documents.isLoading && <p className="py-5 text-sm text-slate-500">Carregando documentos...</p>}
        {!documents.isLoading && documents.data?.items.length === 0 && <p className="py-5 text-sm text-slate-500">Nenhum documento encontrado.</p>}
        <div className="mt-3 divide-y divide-slate-200">
          {documents.data?.items.map((document) => {
            const isSelected = document.id === selectedDocument?.id
            return <button id={`document-${document.id}`} key={document.id} type="button" onClick={() => setSelectedDocumentId(document.id)} className={`scroll-mt-28 flex w-full items-center justify-between gap-4 py-3 text-left text-sm transition ${isSelected ? 'text-slate-950' : 'text-slate-700 hover:text-slate-950'}`}>
              <div className="min-w-0"><strong>{document.ticker}</strong><span className="ml-3 text-slate-500">{document.sourceName}</span><p className="mt-1 truncate text-xs text-slate-500">{formatDate(document.publishedAt)} - {documentTypeLabel(document)}</p></div>
              <span className={`inline-flex shrink-0 items-center gap-2 rounded-full px-3 py-1 text-xs ${isSelected ? 'bg-slate-900 text-white' : 'bg-slate-100 text-slate-600'}`}>{[2, 3].includes(document.processingStatus) && <FontAwesomeIcon icon={faSpinner} spin />}{processingStatuses[document.processingStatus] ?? 'Desconhecido'}</span>
            </button>
          })}
        </div>
        {documents.data && <Pagination page={documents.data.page} totalPages={documents.data.totalPages} onPageChange={setDocumentPage} />}
        <DocumentJobDetails document={selectedDocument} />
      </div>
      <section id="document-details" className="scroll-mt-6 rounded-[1.75rem] border border-slate-200 bg-white p-6 shadow-sm">
        {!selectedDocument && <p className="text-sm text-slate-500">Selecione um documento para ver os detalhes.</p>}
        {selectedDocument && <div className="space-y-6"><div className="border-b border-slate-200 pb-4"><p className="text-sm font-semibold uppercase tracking-[0.16em] text-slate-500">Detalhe do documento</p><h2 className="mt-3 text-3xl font-semibold">{selectedDocument.ticker}</h2><p className="mt-2 text-sm text-slate-500">{selectedDocument.assetName} - {selectedDocument.sourceName}</p><div className="mt-4 grid gap-2 text-sm text-slate-600 md:grid-cols-2"><span>Publicado em {formatDate(selectedDocument.publishedAt)}</span><span>Status {processingStatuses[selectedDocument.processingStatus] ?? 'Desconhecido'}</span><span>Processado em {formatDate(selectedDocument.processedAt)}</span><a href={officialDocumentUrl(selectedDocument.sourceUrl)} target="_blank" rel="noreferrer" className="text-amber-700 hover:text-amber-800"><FontAwesomeIcon icon={faArrowUpRightFromSquare} className="mr-2" />Exibir documento oficial</a></div></div>
          {analysis.isLoading && <p className="text-sm text-slate-500">Carregando an&aacute;lise...</p>}{analysis.isError && <p className="text-sm text-rose-700">N&atilde;o foi poss&iacute;vel carregar a an&aacute;lise deste documento.</p>}{!analysis.isLoading && !analysis.isError && !analysis.data && <div className="rounded-2xl border border-dashed border-slate-300 bg-stone-50 p-5 text-sm text-slate-600">Nenhuma an&aacute;lise validada foi encontrada para este documento ainda.</div>}
          {analysis.data && <DocumentAnalysisContent analysis={analysis.data} />}
        </div>}
      </section>
    </div>
  )
}

async function sendJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  if (!response.ok) throw new Error('Não foi possível reprocessar os documentos.')
  return response.json()
}
