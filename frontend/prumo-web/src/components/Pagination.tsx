import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronLeft, faChevronRight } from '@fortawesome/free-solid-svg-icons'

type PaginationProps = {
  page: number
  totalPages: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  if (totalPages <= 1) return null
  const start = Math.max(1, page - 2)
  const end = Math.min(totalPages, page + 2)
  const pageItems: Array<number | 'ellipsis-start' | 'ellipsis-end'> = []
  if (start > 1) pageItems.push(1)
  if (start > 2) pageItems.push('ellipsis-start')
  for (let current = start; current <= end; current += 1) pageItems.push(current)
  if (end < totalPages - 1) pageItems.push('ellipsis-end')
  if (end < totalPages) pageItems.push(totalPages)

  return <nav aria-label="Paginação" className="pagination-nav mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-4 text-sm">
    <button type="button" disabled={page === 1} onClick={() => onPageChange(page - 1)} className="pagination-arrow rounded-full border border-slate-300 px-3 py-2 disabled:opacity-40"><FontAwesomeIcon icon={faChevronLeft} className="mr-2" />Anterior</button>
    <div className="pagination-pages flex items-center gap-1" aria-label={`Página ${page} de ${totalPages}`}>
      {pageItems.map(item => typeof item === 'number' ? <button key={item} type="button" aria-current={item === page ? 'page' : undefined} onClick={() => onPageChange(item)} className={`h-9 min-w-9 rounded-full px-2 font-medium transition ${item === page ? 'bg-slate-900 text-white' : 'text-slate-600 hover:bg-slate-100'}`}>{item}</button> : <span key={item} className="px-1 text-slate-400">…</span>)}
    </div>
    <button type="button" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)} className="pagination-arrow rounded-full border border-slate-300 px-3 py-2 disabled:opacity-40">Próxima<FontAwesomeIcon icon={faChevronRight} className="ml-2" /></button>
  </nav>
}
