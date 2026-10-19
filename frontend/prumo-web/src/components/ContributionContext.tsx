import { contributionContext } from '../utils/contributionContext'

export function ContributionContext({ reasons }: { reasons: readonly string[] }) {
  const { summary, paragraphs, hasDetails } = contributionContext(reasons)
  if (!summary) return null
  return <div className="space-y-3 leading-6">
    <p>{summary}</p>
    {hasDetails && <details className="border-t border-slate-100 pt-3">
      <summary className="cursor-pointer text-xs font-semibold text-slate-500">Ver explicação completa</summary>
      <div className="mt-3 max-h-80 space-y-3 overflow-y-auto pr-2 text-xs leading-5">
        {paragraphs.map((paragraph, index) => <p key={index}>{paragraph}</p>)}
      </div>
    </details>}
  </div>
}
