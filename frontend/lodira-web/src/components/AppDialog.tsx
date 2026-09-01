import { useEffect, useState, type FormEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faTriangleExclamation, faXmark } from '@fortawesome/free-solid-svg-icons'

export type AppDialogField = { name: string; label: string; defaultValue?: string; placeholder?: string; inputMode?: 'decimal' | 'numeric' | 'text'; readOnly?: boolean; getValue?: (values: Record<string, string>) => string }
type AppDialogProps = { title: string; description?: string; fields?: AppDialogField[]; confirmLabel: string; cancelLabel?: string; tone?: 'danger' | 'primary'; onConfirm: (values: Record<string, string>) => void; onClose: () => void }

export function AppDialog({ title, description, fields = [], confirmLabel, cancelLabel = 'Cancelar', tone = 'primary', onConfirm, onClose }: AppDialogProps) {
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(fields.map(field => [field.name, field.defaultValue ?? ''])))
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])
  const submit = (event: FormEvent) => { event.preventDefault(); onConfirm(values) }
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 p-4" role="dialog" aria-modal="true" aria-labelledby="app-dialog-title" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <form onSubmit={submit} className="w-full max-w-md rounded-3xl bg-white p-6 shadow-2xl">
      <div className="flex items-start gap-4"><span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${tone === 'danger' ? 'bg-rose-100 text-rose-700' : 'bg-amber-100 text-amber-700'}`}><FontAwesomeIcon icon={tone === 'danger' ? faTriangleExclamation : faTriangleExclamation} /></span><div className="min-w-0 flex-1"><h2 id="app-dialog-title" className="text-lg font-semibold text-slate-950">{title}</h2>{description && <p className="mt-2 text-sm leading-6 text-slate-600">{description}</p>}</div><button type="button" aria-label="Fechar" onClick={onClose} className="rounded-full p-2 text-slate-400 hover:bg-slate-100 hover:text-slate-700"><FontAwesomeIcon icon={faXmark} /></button></div>
      {fields.length > 0 && <div className="mt-5 space-y-4">{fields.map(field => <label key={field.name} className="block text-sm font-medium text-slate-700">{field.label}<input autoFocus={field === fields[0]} readOnly={field.readOnly} inputMode={field.inputMode} value={field.getValue ? field.getValue(values) : values[field.name] ?? ''} onChange={event => setValues(current => ({ ...current, [field.name]: event.target.value }))} placeholder={field.placeholder} className={`mt-2 w-full rounded-xl border border-slate-300 px-4 py-3 outline-none ${field.readOnly ? 'cursor-default bg-slate-100 text-slate-600' : 'bg-stone-50 focus:border-amber-500 focus:bg-white'}`} /></label>)}</div>}
      <div className="mt-6 flex justify-end gap-3"><button type="button" onClick={onClose} className="rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50">{cancelLabel}</button><button type="submit" className={`rounded-xl px-4 py-2.5 text-sm font-semibold text-white ${tone === 'danger' ? 'bg-rose-700 hover:bg-rose-800' : 'bg-slate-900 hover:bg-amber-700'}`}>{confirmLabel}</button></div>
    </form>
  </div>
}
