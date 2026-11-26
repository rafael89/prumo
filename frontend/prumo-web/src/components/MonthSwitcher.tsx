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
  const buttonClass = 'month-switcher-button inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-xl text-slate-700'
  const iconClass = 'flex h-8 w-8 items-center justify-center rounded-lg border border-slate-300 text-xs motion-safe:transition-colors'

  return <nav aria-label="Selecionar mês dos rendimentos" className="flex w-full items-center justify-between gap-2 sm:w-auto">
    <button type="button" aria-label="Mês anterior" onClick={() => changeMonth(-1)} className={buttonClass}><span className={iconClass}><FontAwesomeIcon icon={faChevronLeft} aria-hidden="true" /></span></button>
    <span aria-live="polite" aria-atomic="true" className="min-w-0 flex-1 text-center text-sm font-semibold text-slate-700 sm:min-w-36">{label}</span>
    <button type="button" aria-label="Próximo mês" onClick={() => changeMonth(1)} className={buttonClass}><span className={iconClass}><FontAwesomeIcon icon={faChevronRight} aria-hidden="true" /></span></button>
  </nav>
}
