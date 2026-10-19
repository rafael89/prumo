import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'
import { execFileSync } from 'node:child_process'
import { stableVersion, isNewerVersion } from './src/utils/appVersion.ts'

function buildVersion(): string {
  const configured = process.env.PRUMO_VERSION?.trim()
  if (configured) {
    if (configured === 'dev') return 'dev'
    const version = stableVersion(configured)
    if (!version) throw new Error('PRUMO_VERSION deve ser uma tag estável (v1.2.3) ou dev.')
    return version
  }
  try {
    const tags = execFileSync('git', ['tag', '--merged', 'HEAD', '--sort=-version:refname'], {
      cwd: import.meta.dirname, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'],
    }).trim().split(/\r?\n/)
    return tags.reduce((latest, tag) => {
      const version = stableVersion(tag)
      return version && (latest === 'dev' || isNewerVersion(version, latest)) ? version : latest
    }, 'dev')
  } catch {
    return 'dev'
  }
}

// https://vite.dev/config/
export default defineConfig({
  define: { 'import.meta.env.VITE_APP_VERSION': JSON.stringify(buildVersion()) },
  plugins: [react(), tailwindcss()],
  server: {
    port: 5273,
    proxy: {
      '/api': 'http://localhost:5200',
    },
  },
})
