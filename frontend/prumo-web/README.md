# Prumo Web

SPA React/TypeScript com Vite, Tailwind, React Query e React Router. Consulte o [README principal](../../README.md) para backend, banco e configuração.

## Executar e validar

Use Node 24, versão utilizada no CI e na imagem Docker.

```sh
npm ci
npm run dev
npm run build
npm run lint
npm test
```

No PowerShell, `npm.cmd` funciona quando a política local bloqueia `npm.ps1`.

O Vite serve a aplicação na porta 5273 e encaminha `/api` para `localhost:5200`. `VITE_API_URL` altera a base da API no build; sem ela, as chamadas usam a mesma origem. Em Docker, o Nginx encaminha `/api/` para o serviço `api`.

## Organização

- `App.tsx`: shell, seleção de carteira, navegação e operações de carteira.
- `sections/`: telas carregadas sob demanda com `React.lazy` e `Suspense`.
- `api/client.ts`: helpers HTTP compartilhados, incluindo erros, respostas vazias e upload.
- `utils/contributionAnalysis.ts`: mensagens sobre origem e limitações da orientação.
- `utils/contributionExecution.ts`: contrato da confirmação de quantidade/preço realizados.

Rotas: `/`, `/portfolio`, `/radar`, `/contributions`, `/documents`, `/documents/:id`, `/history` e `/about`. `/dashboard` redireciona para `/`.

## Testes

`npm test` usa o runner nativo do Node, sem dependência de navegador. Exercita contratos de importação e aporte, execuções parciais, tratamento de erros e distinção entre fallback determinístico, modelo alternativo e revisão indisponível. O CI executa testes, lint e build.
