type LoadingStateProps = {
  message?: string
  description?: string
  className?: string
}

export function LoadingState({ message = 'Só um instante…', description, className = '' }: LoadingStateProps) {
  return (
    <div role="status" aria-live="polite" aria-atomic="true" className={`flex items-center gap-3 rounded-2xl bg-stone-50 p-4 text-sm text-slate-600 ${className}`}>
      <span aria-hidden="true" style={{ borderTopColor: 'currentColor' }} className="h-6 w-6 shrink-0 rounded-full border-2 border-slate-300 text-amber-700 motion-safe:animate-spin" />
      <div className="min-w-0">
        <p className="font-semibold text-slate-700">{message}</p>
        {description && <p className="mt-1 leading-5">{description}</p>}
      </div>
    </div>
  )
}
