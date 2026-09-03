# Prumo

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![React 19.2.8](https://img.shields.io/badge/React-19.2.8-61DAFB?logo=react&logoColor=20232A)](https://react.dev/)
[![TypeScript 7.0.2](https://img.shields.io/badge/TypeScript-7.0.2-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![Vite 8.2.2](https://img.shields.io/badge/Vite-8.2.2-646CFF?logo=vite&logoColor=white)](https://vite.dev/)
[![Tailwind CSS 4.3.3](https://img.shields.io/badge/Tailwind_CSS-4.3.3-06B6D4?logo=tailwindcss&logoColor=white)](https://tailwindcss.com/)
[![PostgreSQL 16](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Docker Compose](https://img.shields.io/badge/Docker_Compose-ready-2496ED?logo=docker&logoColor=white)](https://docs.docker.com/compose/)

Aplicação self-hosted para acompanhar eventos relevantes dos ativos de uma carteira de investimentos brasileira e apoiar decisões de alocação com contexto da própria carteira.

O Prumo combina importação de carteira da B3, descoberta e análise de documentos da CVM/FNet, avaliação do impacto por carteira, Radar e orientação de aportes sob demanda.

## Funcionalidades

- Carteiras e posições com cadastro, edição, exclusão e visualização paginada.
- Importação do Relatório Consolidado Mensal da B3 em XLSX, com prévia antes da confirmação.
- Descoberta, download, extração, validação e análise de documentos da CVM/FNet.
- Análise global do evento e avaliação do impacto em cada carteira.
- Visão geral com saúde da carteira, Radar e atualizações recentes.
- Radar com prioridades, filtros, estados por carteira, paginação e resolução de situações.
- Tela própria de Aportes (`/contributions`) para gerar orientação, acompanhar a análise da IA e registrar aportes realizados.
- Configuração de alocações-alvo por classe de ativo.
- Privacy mode para ocultar valores financeiros durante a sessão.
- Providers de IA `Fake`, `Gemini`, `OpenAI` e HTTP customizado.

## Stack

### Backend

- .NET 10.0 com ASP.NET Core Web API.
- Worker Service .NET 10.0 para descoberta, processamento e análise assíncrona.
- Entity Framework Core 10.0.11.
- PostgreSQL 16 com Npgsql Entity Framework Core 10.0.3.
- Swagger/OpenAPI com Swashbuckle 10.2.3.

### Frontend

- React 19.2.8.
- TypeScript 7.0.2.
- Vite 8.2.2.
- Tailwind CSS 4.3.3.
- TanStack React Query 5.102.8.
- React Router 7.18.2.

### Operação

- Docker e Docker Compose.
- Serviços Compose: `postgres`, `api`, `worker` e `frontend`.
- Portas publicadas apenas em `localhost` por padrão.

## Organização da aplicação

```text
B3 XLSX -> prévia -> confirmação -> carteira atualizada
CVM/FNet -> descoberta -> download -> extração/validação -> análise IA
Documento -> DocumentAnalysis global -> InvestmentEvent
                                -> PortfolioAssessment por carteira -> Radar/Aportes
```

O backend é a fonte de verdade para os cálculos financeiros e as decisões de produto. A IA interpreta o texto extraído e o contexto consolidado, mas não altera dados financeiros durante a análise.

O frontend é organizado por fluxos:

```text
/                 Visão geral da carteira
/portfolio        Carteira, posições e alocações-alvo
/radar            Eventos que exigem atenção
/contributions    Orientação e registro de aportes
/documents        Documentos e análises
```

A orientação de aportes fica concentrada na tela `/contributions`; a visão geral não exibe mais o card de aportes.

## Executar localmente

### Pré-requisitos

- .NET SDK 10.0.
- Node.js compatível com o frontend e npm.
- PostgreSQL 16 local ou Docker Desktop com Docker Compose.

### Banco com Docker Compose

Na raiz do repositório:

```powershell
docker compose up -d postgres
```

Os defaults locais são:

```text
Database: prumo
User:     postgres
Password: dev123
Host:     localhost
Port:     5432
```

Altere esses valores com `POSTGRES_DB`, `POSTGRES_USER` e `POSTGRES_PASSWORD` quando necessário.

### API e worker

```powershell
dotnet restore Prumo.sln
dotnet ef database update --project src/Prumo.Infrastructure --startup-project src/Prumo.Api
dotnet run --project src/Prumo.Api
dotnet run --project src/Prumo.Worker
```

### Frontend

```powershell
Set-Location frontend/prumo-web
npm install
npm run dev
```

Durante o desenvolvimento, o Vite encaminha `/api` para `http://localhost:5200`. A porta pode ser ajustada em [vite.config.ts](frontend/prumo-web/vite.config.ts).

### Executar tudo com Compose

```powershell
docker compose up --build
```

O frontend fica disponível em `http://localhost:5273` e a API em `http://localhost:5200`.

## IA e configuração

As configurações principais do worker ficam em [src/Prumo.Worker/appsettings.json](src/Prumo.Worker/appsettings.json):

- `AI:Provider`
- `AI:Endpoint`
- `AI:ApiKey`
- `AI:Model`
- `AI:FallbackModel`
- `AI:TimeoutSeconds`
- `AI:RateLimit`
- `Jobs:*`
- `TextQuality:*`
- `Storage:RootPath`

Providers suportados:

- `Fake`: determinístico, indicado para desenvolvimento e testes.
- `Gemini`: Gemini Developer API via `generateContent` com Structured Output.
- `OpenAI`: Responses API com JSON Schema estrito.
- Qualquer outro valor: provider HTTP customizado legado.

Para usar o Gemini localmente sem versionar a chave:

```powershell
dotnet user-secrets set "AI:Provider" "Gemini" --project src/Prumo.Worker
dotnet user-secrets set "AI:ApiKey" "<sua-chave>" --project src/Prumo.Worker
dotnet user-secrets set "AI:Model" "gemini-3.5-flash-lite" --project src/Prumo.Worker
```

Para OpenAI, configure `AI__Provider=OpenAI`, `AI__ApiKey` e `AI__Model`. Nunca versione chaves de providers.

## Documentos e análise

O fluxo assíncrono é executado pelo worker:

```text
DocumentDiscoveryWorker
  -> DownloadDocument
  -> DocumentDownloadWorker
  -> AnalyzeDocument
  -> DocumentAnalysisWorker
  -> extração e validação de texto
  -> IAiAnalysisService
  -> validação e persistência de DocumentAnalysis
```

O contexto enviado para análise pode incluir a carteira relacionada ao ativo, patrimônio total, quantidade de posições, posição afetada, maiores posições por peso e indicadores de concentração.

O endpoint `GET /api/documents/{id}/analysis` retorna a análise validada mais recente; `RawResponse` não é exposto por padrão.

## Migrations

As migrations ficam em [src/Prumo.Infrastructure/Persistence/Migrations](src/Prumo.Infrastructure/Persistence/Migrations). O estado atual é uma baseline única `InitialCreate`, gerada diretamente do modelo EF.

```powershell
dotnet ef database update --project src/Prumo.Infrastructure --startup-project src/Prumo.Api
```

A baseline atual é:

```text
20260903035839_InitialCreate
```

Depois do primeiro banco Beta com dados importantes, novas migrations devem ser append-only.

## Validação

```powershell
dotnet build Prumo.sln
dotnet test Prumo.sln

Set-Location frontend/prumo-web
npm run build
npm run lint
```

O E2E com serviços externos, como CVM/FNet e providers de IA, depende da configuração correspondente no ambiente.

## Reset local do banco

Para simular uma instalação limpa em Development:

```powershell
.\scripts\reset-local-database.ps1
```

O script valida a connection string, bloqueia hosts remotos, exige a confirmação literal `RESET`, reseta o schema `public`, reaplica as migrations e verifica se o banco está vazio. Nenhum seed, carteira ou usuário é criado.

## Segurança e deployment

O Compose atual foi desenhado para uso self-hosted em rede privada. As portas são vinculadas ao localhost e o TLS deve ser terminado por um reverse proxy externo. Autenticação multiusuário ainda não faz parte do modelo atual; não exponha a API diretamente à Internet.

Os valores `prumo` e `dev123` são apenas defaults locais de desenvolvimento. Em ambientes reais, forneça credenciais e chaves por variáveis de ambiente ou secrets.
