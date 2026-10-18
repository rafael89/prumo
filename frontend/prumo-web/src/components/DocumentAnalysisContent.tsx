import { analysisTone } from '../utils/documentAnalysis'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCircleInfo } from '@fortawesome/free-solid-svg-icons'

type Analysis = {
  importance: string
  classification: string
  summary: string
  impact: string
  suggestedAction: string
  requiresAction: boolean
  portfolioAssessment: { requiresAttention: boolean } | null
  confidence: number
  model: string
  promptVersion: string
  affectedAssets: string[]
}

const importanceLabels: Record<string, string> = { Critical: 'Cr\u00edtico', Relevant: 'Relevante', Informative: 'Informativo', NoAction: 'Sem a\u00e7\u00e3o' }
const classificationLabels: Record<string, string> = { CorporateAction: 'A\u00e7\u00e3o corporativa', FinancialResult: 'Resultado financeiro', Distribution: 'Distribui\u00e7\u00e3o', CapitalRaise: 'Capta\u00e7\u00e3o', AssetTransaction: 'Transa\u00e7\u00e3o de ativos', Governance: 'Governan\u00e7a', Operational: 'Operacional', Liquidation: 'Liquida\u00e7\u00e3o', Other: 'Outro' }
const actionLabels: Record<string, string> = { Maintain: 'Manter', IncreasePosition: 'Aumentar posi\u00e7\u00e3o', ReduceExposure: 'Reduzir exposi\u00e7\u00e3o', EvaluateSale: 'Avaliar venda', Monitor: 'Acompanhar', NoAction: 'Sem a\u00e7\u00e3o', OpenPosition: 'Abrir posi\u00e7\u00e3o' }

export function DocumentAnalysisContent({ analysis, framed = true }: { analysis: Analysis; framed?: boolean }) {
  return <div className={framed ? `level-card rounded-3xl border border-slate-300 bg-white p-5 text-slate-900 ${analysisTone(analysis.importance)}` : 'text-slate-900'}><div className="grid gap-4 md:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">Importância</p><p className="mt-2 text-2xl font-semibold">{importanceLabels[analysis.importance] ?? analysis.importance}</p></div><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">Classificação</p><p className="mt-2 text-lg font-semibold">{classificationLabels[analysis.classification] ?? analysis.classification}</p></div></div><div className="mt-6 grid gap-5"><article><p className="text-xs font-semibold uppercase tracking-[0.16em]">Resumo</p><p className="mt-2 text-sm leading-7">{analysis.summary}</p></article><article><p className="text-xs font-semibold uppercase tracking-[0.16em]">Impacto</p><p className="mt-2 text-sm leading-7">{analysis.impact}</p></article></div><div className="mt-6 grid gap-4 border-t border-current/10 pt-5 md:grid-cols-2"><div><p className="text-xs font-semibold uppercase tracking-[0.16em]">Ação sugerida</p><p className="mt-2 text-lg font-semibold">{actionLabels[analysis.suggestedAction] ?? analysis.suggestedAction}</p><p className="mt-1 text-sm opacity-75">{(analysis.portfolioAssessment?.requiresAttention ?? analysis.requiresAction) ? "Requer atenção" : "Não requer ação imediata"}</p></div><div><div className="flex items-center gap-2"><p className="text-xs font-semibold uppercase tracking-[0.16em]">Confiança</p><span title="Estimativa da IA sobre a confiança na interpretação do documento. O backend valida o valor entre 0% e 100%; não é uma verificação factual independente." aria-label="Sobre a confiança da análise"><FontAwesomeIcon icon={faCircleInfo} className="text-xs opacity-60" /></span></div><p className="mt-2 text-lg font-semibold">{Math.round(analysis.confidence * 100)}%</p><p className="mt-1 text-sm opacity-75">Modelo {analysis.model || 'N\u00e3o informado'} - Prompt {analysis.promptVersion || 'N\u00e3o informado'}</p></div></div><div className="mt-6"><p className="text-xs font-semibold uppercase tracking-[0.16em]">Ativos afetados</p><div className="mt-3 flex flex-wrap gap-2">{analysis.affectedAssets.length > 0 ? analysis.affectedAssets.map((asset) => <span key={asset} className="rounded-full border border-current/15 px-3 py-1 text-xs font-semibold">{asset}</span>) : <span className="text-sm opacity-75">Nenhum ativo listado.</span>}</div></div></div>
}
