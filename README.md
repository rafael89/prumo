# Prumo

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![React 19.3.0](https://img.shields.io/badge/React-19.3.0-61DAFB?logo=react&logoColor=20232A)](https://react.dev/)
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
- Motor de aportes que compara necessidades de alocação, diversificação, concentração, aportes recentes, qualidade financeira e valuation relativo quando há dados suficientes.
- Simulação de cenários concentrados ou distribuídos, com fallback determinístico quando um provider de IA está indisponível.
- Histórico da carteira com compras, eventos, documentos e mudanças de avaliação.
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

- React 19.3.0.
- TypeScript 7.0.2.
- Vite 8.2.2.
- Tailwind CSS 4.3.3.
- TanStack React Query 5.102.8.
- React Router 7.18.2.

### Operação

- Docker e Docker Compose.
- Serviços Compose: `postgres`, `api`, `worker` e `frontend`.
- PostgreSQL e API publicados apenas em `localhost` por padrão; o frontend é publicado na porta `5273` do host.

## Organização da aplicação

```text
B3 XLSX -> prévia -> confirmação -> carteira atualizada
CVM/FNet -> descoberta -> download -> extração/validação -> análise IA
Documento -> DocumentAnalysis global -> InvestmentEvent
                                -> PortfolioAssessment por carteira -> Radar/Aportes
```

O backend é a fonte de verdade para os cálculos financeiros e as decisões de produto. Na orientação de aportes, ele calcula candidatos, restrições e cenários válidos. A IA pode ajudar a selecionar e explicar uma alternativa, mas não cria ativos, valores ou regras. Se um provider falhar, o Prumo utiliza o fallback determinístico seguro.

O frontend é organizado por fluxos:

```text
/                 Visão geral da carteira
/portfolio        Carteira, posições e alocações-alvo
/radar            Eventos que exigem atenção
/contributions    Orientação e registro de aportes
/documents        Documentos e análises
/history          Histórico da carteira e dos ativos
/about            Sobre o Prumo
```

A orientação de aportes fica concentrada na tela `/contributions`; a visão geral não exibe mais o card de aportes.

### Princípio de produto

O Prumo segue um modelo de produto orientado à autonomia. Uma capacidade do backend não se torna automaticamente uma funcionalidade permanente voltada ao usuário. As responsabilidades normais devem permanecer limitadas a importar ou atualizar a carteira, definir alocações-alvo por classe de ativo, informar um valor para aporte e revisar ou confirmar a orientação do Prumo. O restante deve ser automatizado, derivado, contextual, acessível por drill-down ou acionado apenas em exceções, sempre que for seguro.

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

Ao copiar [.env.example](.env.example), altere `POSTGRES_PASSWORD` antes de iniciar os serviços. Também é possível alterar esses valores com `POSTGRES_DB`, `POSTGRES_USER` e `POSTGRES_PASSWORD` quando necessário.

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
Ao usar o Compose completo, a API aplica automaticamente as migrations antes de ficar saudável e o Worker aguarda essa etapa.

Com a API em execução, `/health` e `/health/ready` verificam a conexão com PostgreSQL; `/health/live` verifica se a aplicação responde. A documentação OpenAPI/Swagger fica em `http://localhost:5200/swagger` apenas em Development.

O worker não abre uma porta HTTP adicional. Seu estado operacional fica no processo e nos logs padrão do container; o Compose acompanha se o processo permanece em execução. Os contadores internos do worker existem para diagnóstico e testes, mas não iniciam exporter ou serviço de métricas. API e worker também mantêm os logs de arquivo existentes nos volumes `api-logs` e `worker-logs`, com retenção de sete dias; arquivos mais antigos são removidos na próxima gravação de cada serviço.

## IA e configuração

As configurações principais do worker ficam em [src/Prumo.Worker/appsettings.json](src/Prumo.Worker/appsettings.json):

- `AI:Provider`
- `AI:Endpoint`
- `AI:ApiKey`
- `AI:Model`
- `AI:FallbackModel`
- `AI:TimeoutSeconds`
- `AI:RateLimit`
- `AI:Contribution:Provider`
- `AI:Contribution:Endpoint`
- `AI:Contribution:ApiKey`
- `AI:Contribution:Model`
- `AI:Contribution:FallbackModel`
- `AI:Contribution:ThinkingLevel`
- `Jobs:*`
- `TextQuality:*`
- `Storage:RootPath`
- `Discord:WebhookUrl` (opcional, para notificações de logs)

Providers de análise documental:

- `Fake`: determinístico, indicado para desenvolvimento e testes.
- `Gemini`: Gemini Developer API via `generateContent` com Structured Output.
- `OpenAI`: Responses API com JSON Schema estrito.
- `Http`: provider HTTP customizado, com endpoint e modelo configurados.

O worker valida provider, modelo, credenciais exigidas, endpoint HTTP e timeout na inicialização. `Fake` é permitido apenas fora de Production. Valores desconhecidos de provider são rejeitados.

Para aportes, `AI:Contribution:Provider` aceita `Auto` (padrão), `Gemini`, `Http` ou `Deterministic`:

| Provider de documentos | Aportes com `Auto` |
| --- | --- |
| Gemini | Gemini |
| Http | Http |
| OpenAI | Motor determinístico |
| Fake | Motor determinístico |

OpenAI está integrado à análise documental. Para aportes com IA e documentos OpenAI, configure explicitamente Gemini ou Http em `AI:Contribution`, incluindo modelo e as credenciais/endpoint correspondentes. Quando os providers são iguais, as opções de aportes herdam os valores de documentos; ao trocar de provider, as credenciais e modelos não são herdados. `Deterministic` calcula a orientação diretamente pelas regras, sem tentar chamar IA.

A tela de aportes distingue uso do motor de regras, troca para modelo alternativo de IA e indisponibilidade da revisão adicional. O motivo é preservado junto da orientação.

Para usar o Gemini localmente sem versionar a chave:

```powershell
dotnet user-secrets set "AI:Provider" "Gemini" --project src/Prumo.Worker
dotnet user-secrets set "AI:ApiKey" "<sua-chave>" --project src/Prumo.Worker
dotnet user-secrets set "AI:Model" "gemini-3.5-flash-lite" --project src/Prumo.Worker
```

Para OpenAI, configure `AI__Provider=OpenAI`, `AI__ApiKey` e `AI__Model`. No Compose, use `AI_PROVIDER`, `AI_API_KEY` (ou o alias legado `GEMINI_API_KEY`) e `AI_MODEL`. Para aportes existem `AI_CONTRIBUTION_PROVIDER`, `AI_CONTRIBUTION_ENDPOINT`, `AI_CONTRIBUTION_API_KEY`, `AI_CONTRIBUTION_MODEL`, `AI_CONTRIBUTION_FALLBACK_MODEL` e `AI_CONTRIBUTION_THINKING_LEVEL`. `AI_FALLBACK_MODEL` controla o fallback documental e `DISCORD_WEBHOOK_URL` as notificações opcionais. Nunca versione chaves de providers.

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

O contexto da análise documental é global e contém dados do ativo e do documento. O impacto específico sobre cada carteira é calculado separadamente em `PortfolioAssessment`, considerando posições, metas e políticas versionadas.

O endpoint `GET /api/documents/{id}/analysis` retorna a análise validada mais recente; `RawResponse` não é exposto por padrão.

## Migrations

O schema do banco fica em [src/Prumo.Infrastructure/Migrations](src/Prumo.Infrastructure/Migrations). As migrations são aplicadas pelo Entity Framework Core.

```powershell
dotnet ef database update --project src/Prumo.Infrastructure --startup-project src/Prumo.Api
```

O estado atual do modelo é representado, no momento, por:

```text
InitialCreate
CandidatesJsonUseText
CvmFiiMonthlyStructuredIngestion
PartialContributionExecutions
DocumentIgnoredReason
```

As migrations novas devem ser append-only. Como o Prumo ainda não está em produção, a baseline pode ser regenerada junto com um reset local do banco quando houver alterações estruturais.

## Validação

```powershell
dotnet build Prumo.sln
dotnet test Prumo.sln

Set-Location frontend/prumo-web
npm run build
npm run lint
npm test
```

Sem `PRUMO_RUN_RELATIONAL_TESTS=1`, os testes PostgreSQL aparecem como ignorados, com o motivo explícito. Para executá-los localmente, com PostgreSQL disponível na configuração de desenvolvimento esperada pelos testes:

```powershell
$env:PRUMO_RUN_RELATIONAL_TESTS = '1'
dotnet test Prumo.sln
Remove-Item Env:PRUMO_RUN_RELATIONAL_TESTS
```

Esses testes usam bancos temporários próprios e cobrem migrations, concorrência, importação, publicação de orientação pelo worker e confirmações parciais. O CI os ativa com PostgreSQL 16 e executa também os testes frontend com Node 24, alinhado ao Dockerfile. No Windows, se a política do PowerShell bloquear `npm.ps1`, use `npm.cmd`.

O E2E com serviços externos, como CVM/FNet e providers de IA, depende da configuração correspondente no ambiente. Os testes frontend atuais validam contratos HTTP e regras de apresentação; não substituem testes de navegador.

## Reset local do banco

Para simular uma instalação limpa em Development:

```powershell
.\scripts\reset-local-database.ps1
```

O script valida a connection string, bloqueia hosts remotos, exige a confirmação literal `RESET`, reseta o schema `public`, reaplica as migrations e verifica se o banco está vazio. Nenhum seed, carteira ou usuário é criado.

## Segurança e deployment

O Compose atual foi desenhado para uso self-hosted em rede privada. PostgreSQL e API ficam vinculados ao localhost; o frontend é publicado na porta `5273` do host. Em uma instalação exposta, use um reverse proxy externo para terminar o TLS. Autenticação multiusuário ainda não faz parte do modelo atual; não exponha a API diretamente à Internet.

Os valores `prumo` e `dev123` são apenas defaults locais de desenvolvimento. Em ambientes reais, forneça credenciais e chaves por variáveis de ambiente ou secrets.
