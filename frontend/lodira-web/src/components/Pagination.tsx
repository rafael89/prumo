type PaginationProps = {
  page: number
  totalPages: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  if (totalPages <= 1) return null
  return <nav aria-label="Paginação" className="mt-5 flex items-center justify-between border-t border-slate-200 pt-4 text-sm">
    <button type="button" disabled={page === 1} onClick={() => onPageChange(page - 1)} className="rounded-full border border-slate-300 px-3 py-2 disabled:opacity-40"><FontAwesomeIcon icon={faChevronLeft} className="mr-2" />Anterior</button>
    <span>Página {page} de {totalPages}</span>
    <button type="button" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)} className="rounded-full border border-slate-300 px-3 py-2 disabled:opacity-40">Próxima<FontAwesomeIcon icon={faChevronRight} className="ml-2" /></button>
  </nav>
}
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronLeft, faChevronRight } from '@fortawesome/free-solid-svg-icons'
