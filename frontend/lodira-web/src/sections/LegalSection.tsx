import { Link, useLocation } from 'react-router-dom'

export const LEGAL_DOCUMENT_VERSION = '2026-09-01'

const pages = {
  '/terms': {
    eyebrow: 'Termos de uso', title: 'Regras claras para usar o Lodira.',
    sections: [
      ['Sobre o serviço', 'O Lodira é uma plataforma de acompanhamento de carteiras que organiza dados informados pelo usuário, documentos públicos e informações relacionadas aos investimentos para produzir análises e cenários explicáveis.'],
      ['Uso dos cenários', 'As análises, classificações e alocações calculadas têm caráter informativo e educacional. O Lodira não executa ordens, não recebe credenciais de corretora e não substitui a avaliação do investidor ou o aconselhamento de profissional habilitado.'],
      ['Como os cenários são calculados', 'Os cenários de aporte combinam a estratégia cadastrada, a composição atual, os desvios de alocação, limites de concentração, aportes recentes e informações de contexto. A distribuição é calculada por regras do produto; recursos automatizados podem complementar a explicação, mas não alteram livremente os cálculos determinísticos.'],
      ['Responsabilidade pelos dados', 'O usuário é responsável pela exatidão dos dados importados ou cadastrados e por validar informações antes de tomar qualquer decisão. Resultados podem ser afetados por dados incompletos, desatualizados ou incorretos.'],
      ['Fontes externas', 'Documentos e informações de mercado podem ser obtidos de fontes públicas ou integradas ao serviço. O Lodira pode reproduzir atrasos, indisponibilidades ou inconsistências dessas fontes e recomenda a conferência nos documentos oficiais.'],
      ['Disponibilidade e alterações', 'O serviço pode receber melhorias, correções, alterações ou interrupções temporárias. O Lodira poderá atualizar suas funcionalidades e estes termos para refletir a evolução do produto.'],
      ['Uso adequado', 'É proibido utilizar o serviço para atividades ilegais, tentar acessar dados de terceiros, explorar vulnerabilidades ou reproduzir o conteúdo e a lógica do produto sem autorização.'],
    ],
  },
  '/privacy': {
    eyebrow: 'Política de privacidade', title: 'Transparência sobre os dados do Lodira.',
    sections: [
      ['Dados tratados', 'O Lodira pode tratar dados de cadastro, preferências, carteiras, posições, movimentações, documentos importados, análises geradas, registros técnicos e informações necessárias para segurança e funcionamento do serviço. O Lodira não solicita credenciais de corretora para executar ordens.'],
      ['Finalidades', 'Os dados são utilizados para importar e organizar a carteira, calcular alocações e desvios, processar documentos, contextualizar eventos, gerar cenários, corrigir falhas, proteger o serviço e melhorar a experiência.'],
      ['Fontes e dados enviados', 'O produto pode receber arquivos e informações de fontes públicas ou integrações configuradas pelo operador, incluindo documentos relacionados ao mercado. Somente os dados necessários ao processamento da funcionalidade devem ser enviados a prestadores envolvidos na operação.'],
      ['Inteligência e prestadores', 'Quando necessário para uma análise, dados ou trechos relacionados ao contexto podem ser processados por provedores configurados pelo operador, como Google Gemini, OpenAI ou outro serviço integrado. Os provedores efetivamente utilizados, finalidades, retenção e transferências devem ser confirmados na versão jurídica final.'],
      ['Armazenamento e segurança', 'Os dados são armazenados na infraestrutura configurada para o Lodira pelo período definido pelo responsável pelo serviço e pelas obrigações aplicáveis. São adotadas medidas técnicas e administrativas para reduzir riscos de acesso, alteração, perda ou divulgação indevida.'],
      ['Direitos e contato', 'O titular pode solicitar informações sobre o tratamento, correção, atualização e, quando aplicável, eliminação ou revogação de consentimentos. O responsável pelo Lodira deverá publicar um canal específico e as bases legais aplicáveis antes do lançamento público.'],
    ],
  },
  '/legal': {
    eyebrow: 'Aviso legal', title: 'Informação para apoiar decisões mais conscientes.',
    sections: [
      ['Caráter informativo', 'O Lodira é uma ferramenta de acompanhamento e apoio à decisão. Suas análises e cenários de alocação são produzidos a partir dos dados disponíveis, regras financeiras determinísticas e recursos automatizados de interpretação.'],
      ['Sem execução de ordens', 'O Lodira não realiza compras, vendas, transferências ou qualquer outra ordem em nome do usuário. A decisão final, a conferência das informações e a execução são de responsabilidade do investidor.'],
      ['Limitações', 'Informações de mercado, documentos e dados de carteira podem estar incompletos, atrasados ou sujeitos a atualização. Nenhuma análise garante resultado, rentabilidade ou adequação a um objetivo individual.'],
      ['Revisão profissional', 'Este conteúdo é uma versão inicial de comunicação do produto e não constitui aconselhamento jurídico. Recomenda-se revisão por profissional especializado antes da publicação definitiva, especialmente quanto ao enquadramento regulatório das funcionalidades personalizadas.'],
    ],
  },
} as const

export function LegalSection() {
  const { pathname } = useLocation()
  const page = pages[pathname as keyof typeof pages] ?? pages['/legal']
  return <section className="mx-auto max-w-5xl px-6 pb-24 pt-3">
    <div className="rounded-3xl bg-slate-900 p-7 text-white shadow-sm sm:p-10"><p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-300">{page.eyebrow}</p><h1 className="mt-4 max-w-3xl text-4xl font-semibold tracking-tight sm:text-5xl">{page.title}</h1><p className="mt-5 max-w-3xl text-sm leading-7 text-slate-300">Última atualização: 1º de setembro de 2026. Este texto deve ser revisado por profissional jurídico antes da publicação definitiva.</p></div>
    <div className="mt-6 space-y-4">{page.sections.map(([title, text]) => <article key={title} className="rounded-3xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8"><h2 className="text-xl font-semibold">{title}</h2><p className="mt-3 text-sm leading-7 text-slate-600">{text}</p></article>)}</div>
  </section>
}

export function SiteFooter() {
  return <footer className="mx-auto max-w-358 border-t border-slate-200 px-6 py-8 text-sm text-slate-500"><div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between"><p>© 2026 Lodira. Informação clara para acompanhar sua carteira.</p><nav className="flex flex-wrap gap-x-5 gap-y-2" aria-label="Informações legais"><Link to="/about" className="hover:text-slate-900">Sobre</Link><Link to="/terms" className="hover:text-slate-900">Termos de uso</Link><Link to="/privacy" className="hover:text-slate-900">Privacidade</Link><Link to="/legal" className="hover:text-slate-900">Aviso legal</Link></nav></div></footer>
}

export function LegalConsentGate({ onAccept }: { onAccept: () => void }) {
  return <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 p-4" role="dialog" aria-modal="true" aria-labelledby="legal-consent-title">
    <div className="w-full max-w-lg rounded-3xl bg-white p-6 shadow-2xl sm:p-8">
      <p className="text-xs font-bold uppercase tracking-[0.2em] text-amber-700">Antes de continuar</p><h2 id="legal-consent-title" className="mt-3 text-2xl font-semibold text-slate-950">Conheça as regras do Lodira</h2>
      <p className="mt-3 text-sm leading-6 text-slate-600">O Lodira organiza dados da sua carteira e gera análises e cenários de apoio à decisão. Leia os documentos antes de utilizar a plataforma.</p>
      <div className="mt-5 rounded-2xl bg-slate-50 p-4 text-sm leading-6 text-slate-700"><p><Link to="/terms" className="font-semibold text-sky-700 hover:text-sky-900">Termos de uso</Link></p><p><Link to="/privacy" className="font-semibold text-sky-700 hover:text-sky-900">Política de privacidade</Link></p><p><Link to="/legal" className="font-semibold text-sky-700 hover:text-sky-900">Aviso legal</Link></p></div>
      <label className="mt-5 flex items-start gap-3 text-sm leading-6 text-slate-700"><input id="legal-consent" type="checkbox" className="mt-1 h-4 w-4 accent-slate-900" onChange={event => { if (event.target.checked) onAccept() }} /><span>Li e aceito os Termos de uso e confirmo que tomei conhecimento da Política de privacidade e do Aviso legal.</span></label>
      <p className="mt-4 text-xs leading-5 text-slate-500">Versão dos documentos: {LEGAL_DOCUMENT_VERSION}. O aceite ficará registrado localmente neste navegador.</p>
    </div>
  </div>
}
