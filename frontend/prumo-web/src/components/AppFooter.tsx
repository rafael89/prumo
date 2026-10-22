import { useQuery } from '@tanstack/react-query'
import { fetchLatestStableTag, isNewerVersion } from '../utils/appVersion'

const copyrightYear = new Date().getFullYear()
const installedVersion: string = import.meta.env.VITE_APP_VERSION
const versionLabel = installedVersion === 'dev' ? 'desenvolvimento' : `v${installedVersion}`
const checkInterval = 60 * 60 * 1000

export function AppFooter() {
  const { data: latestTag } = useQuery({
    queryKey: ['latest-stable-tag'],
    queryFn: ({ signal }) => fetchLatestStableTag(signal),
    staleTime: checkInterval,
    refetchInterval: checkInterval,
    retry: false,
    enabled: installedVersion !== 'dev',
  })
  const updateAvailable = latestTag && isNewerVersion(latestTag, installedVersion)

  return <footer className="app-footer mt-auto min-w-0 border-t border-slate-200 bg-white/70 px-3 py-4 sm:px-6">
    <div className="mx-auto flex min-h-6 max-w-6xl items-center justify-between gap-2 overflow-x-auto whitespace-nowrap text-[11px] text-[#667b99] sm:gap-3 sm:text-sm">
      <div className="flex shrink-0 items-center gap-1.5 sm:gap-3">
        <p>© {copyrightYear} <a href="https://rafaelrabello.com.br" target="_blank" rel="noopener noreferrer" className="text-[#a6b8d0] transition hover:text-[#c8d4e3]">rafaelrabello.com.br</a></p>
        <span className="inline-flex items-center gap-1.5 sm:gap-3"><span aria-hidden="true">·</span><span className="text-[#a6b8d0]">{versionLabel}</span></span>
        {updateAvailable && <span className="inline-flex items-center gap-1.5 sm:gap-3"><span aria-hidden="true">·</span><a href={`https://github.com/rafael89/prumo/tree/${encodeURIComponent(latestTag)}`} target="_blank" rel="noopener noreferrer" className="font-semibold text-[#ffab00] transition hover:text-[#ffc247]">Atualização disponível: v{latestTag.replace(/^v/, '')}</a></span>}
      </div>
      <a href="https://github.com/rafael89/prumo" target="_blank" rel="noopener noreferrer" className="shrink-0 text-[#a6b8d0] transition hover:text-[#c8d4e3]">GitHub</a>
    </div>
  </footer>
}
