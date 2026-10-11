import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronLeft, faChevronRight } from '@fortawesome/free-solid-svg-icons'

type MonthSwitcherProps = {
  month: string
  onMonthChange: (month: string) => void
}

export function MonthSwitcher({ month, onMonthChange }: MonthSwitcherProps) {
  const date = new Date(`${month}T12:00:00`)
  const label = new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' }).format(date)
  const changeMonth = (offset: number) => {
    const next = new Date(date.getFullYear(), date.getMonth() + offset, 1, 12)
    onMonthChange(`${next.getFullYear()}-${String(next.getMonth() + 1).padStart(2, '0')}-01`)
  }
  const buttonClass = 'inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-slate-300 text-slate-700 transition hover:border-amber-500 hover:bg-amber-50 hover:text-amber-700'

  return <nav aria-label="Selecionar mês dos rendimentos" className="inline-flex items-center gap-2">
    <button type="button" aria-label="Mês anterior" title="Mês anterior" onClick={() => changeMonth(-1)} className={buttonClass}><FontAwesomeIcon icon={faChevronLeft} aria-hidden="true" /></button>
    <span aria-live="polite" aria-atomic="true" className="min-w-36 text-center text-sm font-semibold text-slate-700">{label}</span>
    <button type="button" aria-label="Próximo mês" title="Próximo mês" onClick={() => changeMonth(1)} className={buttonClass}><FontAwesomeIcon icon={faChevronRight} aria-hidden="true" /></button>
  </nav>
}
