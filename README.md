# Lodira

Aplicacao self-hosted para acompanhar eventos relevantes dos ativos de uma carteira de investimentos brasileira.

O repositorio contempla os incrementos funcionais de descoberta e analise de documentos, Radar, priorizacao, orientação de aporte sob demanda, importação de carteira B3 em XLSX e as melhorias de UX do Incremento 10.

## Stack

- React, TypeScript, Vite e Tailwind CSS
- ASP.NET Core Web API em .NET 10
- .NET Worker
- PostgreSQL 16
- Docker Compose

## Funcionalidades atuais

- Carteiras e posições com detalhe do ativo; o Dashboard oferece paginação server-side das posições.
- Importação do Relatório Consolidado Mensal da B3 em XLSX, com prévia e sincronização.
- Descoberta e processamento de documentos da CVM/FNet.
- Análise global do evento e avaliação do impacto em cada carteira.
- Radar com prioridade, filtros, estados por portfolio e paginação.
- Orientação de aporte sob demanda baseada em alocações-alvo e impactos de eventos relevantes.
- Privacy mode para ocultar valores financeiros na sessão atual.
- Frontend organizado em sections para Dashboard, Carteira e Documentos, com Radar, Aporte e importação B3.

## Fluxo de dados

```text
B3 XLSX -> prévia -> confirmação -> carteira atualizada
CVM/FNet -> descoberta -> download -> extração/validação -> análise IA
Documento -> DocumentAnalysis global -> InvestmentEvent -> PortfolioAssessment por portfolio -> Radar/Aporte
```

O backend é a fonte de verdade para os cálculos financeiros e a inteligência de produto. O Radar determina `RequiresAttention` pela `RadarAttentionNowPolicy`; a orientação de aporte usa os impactos dos assessments (`CapitalDecisionImpact`) para selecionar e priorizar candidatos. O frontend gerencia apresentação, filtros, estado de tela e entrada dos formulários.

## Renomeacao tecnica e compatibilidade

O nome oficial do produto e Lodira, com a solucao e os projetos organizados sob esse nome. O diretorio local pode continuar sendo `prumo` sem impacto no build ou na execucao.

Para preservar o ambiente local existente, os valores padrao do PostgreSQL continuam usando o banco, usuario e senha `prumo`, e o `UserSecretsId` historico do worker foi mantido. O historico de migrations foi consolidado na baseline oficial descrita abaixo; nao ha compromisso de upgrade a partir das migrations antigas.

## Executar localmente

### API e worker

```powershell
dotnet restore Lodira.sln
dotnet build Lodira.sln
dotnet run --project src/Lodira.Api
dotnet run --project src/Lodira.Worker
```

### Frontend

```powershell
Set-Location frontend/lodira-web
npm install
npm run dev
```

## Documentos e analise

Fluxo atual:

```text
DocumentDiscoveryWorker
  -> ProcessingJob DownloadDocument
  -> DocumentDownloadWorker
  -> Document em WaitingProcessing
  -> ProcessingJob AnalyzeDocument
  -> DocumentAnalysisWorker
  -> PDF armazenado
  -> IPdfTextExtractor
  -> ITextQualityValidator
  -> IAiAnalysisService
  -> AiAnalysisValidator
  -> DocumentAnalysis persistido
```

O backend continua como fonte de verdade. A IA apenas interpreta o texto extraido e o contexto consolidado da carteira. Nenhum dado financeiro da carteira e alterado durante a analise.

O endpoint `GET /api/documents/{id}/analysis` retorna a analise validada mais recente do documento. `RawResponse` nao e exposto por padrao.

## Contexto da carteira

O `PortfolioAnalysisContext` e reconstruido no momento da analise e fornece a carteira principal relacionada ao ativo do documento, patrimonio total, quantidade de posicoes, dados da posicao afetada, top 5 posicoes por peso e indicadores de concentracao.

Os calculos financeiros continuam no backend e preservam a precisao interna. No prompt, valores monetarios sao apresentados com duas casas em `pt-BR` e percentuais com duas casas, sem depender da cultura da maquina. A concentracao inclui o maior peso individual e a soma dos tres maiores pesos.

## Migrations

As migrations ficam em [src/Lodira.Infrastructure/Persistence/Migrations](src/Lodira.Infrastructure/Persistence/Migrations). O estado atual e representado por uma unica `InitialCreate` gerada diretamente do modelo EF.

Para aplicar migrations com ambiente compativel:

```powershell
dotnet ef database update --project src/Lodira.Infrastructure --startup-project src/Lodira.Api
```

O banco deve ser inicializado com `dotnet ef database update --project src/Lodira.Infrastructure --startup-project src/Lodira.Api` antes de usar as funcionalidades correspondentes.

## Estrutura do frontend

```text
frontend/lodira-web/src/
  App.tsx                         # shell, navegação e estado compartilhado
  components/Pagination.tsx       # paginação reutilizável
  sections/DashboardSection.tsx
  sections/PortfolioSection.tsx
  sections/DocumentsSection.tsx
  sections/FeatureSections.tsx    # Radar, Aporte e importação B3
```

As listas de posições do Dashboard, documentos e Radar são paginadas no backend. Filtros resetam a página para 1 e as query keys incluem o `portfolioId` quando o dado é específico da carteira.

## Configuracao

Configuracoes principais do worker em [src/Lodira.Worker/appsettings.json](src/Lodira.Worker/appsettings.json):

- `Jobs:PollInterval`
- `Jobs:Lease`
- `Jobs:RetryDelay`
- `Jobs:MaxAttempts`
- `TextQuality:MinimumCharacters`
- `TextQuality:MinimumPrintableRatio`
- `AI:Endpoint`
- `AI:Provider`
- `AI:ApiKey`
- `AI:Model`
- `AI:TimeoutSeconds`

Providers suportados pelo Worker:

- `Fake`: deterministico, permitido apenas fora de Production.
- `Gemini`: Gemini Developer API via `generateContent`, usando Structured Output.
- `OpenAI`: Responses API com JSON Schema estrito.
- qualquer outro valor: provider HTTP custom legado (`HttpAiAnalysisService`).

Para usar o Gemini:

```powershell
$env:AI__Provider="Gemini"
$env:AI__ApiKey="sua-chave"
$env:AI__Model="gemini-3.5-flash-lite"
$env:AI__TimeoutSeconds="60"
dotnet run --project src/Lodira.Worker
```

Para usar OpenAI, defina `AI__Provider="OpenAI"`, `AI__ApiKey` e `AI__Model`. Nunca versione chaves no repositorio.

### Configuração local de IA

O `Lodira.Worker` é o executável que instancia os providers de IA e já possui suporte padrão a .NET User Secrets em `Development`. Para armazenar uma chave Gemini localmente sem versioná-la:

```powershell
dotnet user-secrets set "AI:ApiKey" "<sua-chave-gemini>" --project src/Lodira.Worker
dotnet user-secrets list --project src/Lodira.Worker
dotnet user-secrets remove "AI:ApiKey" --project src/Lodira.Worker
```

O provider `Fake` continua sendo carregado por `src/Lodira.Worker/appsettings.Development.json`. Para containers, Beta e Production, `AI__ApiKey` continua sendo uma fonte de configuração suportada.

## Validação automatizada

- `dotnet build Lodira.sln`: aprovado
- `dotnet test Lodira.sln`: 161 aprovados, 0 falhos
- `npm run build`: aprovado
- `npm run lint`: executado; 2 warnings conhecidos no frontend

O E2E real com serviços externos, como CVM/FNet e providers de IA, depende da configuração correspondente no ambiente.

## Warning conhecido

Avisos `NU1900` podem aparecer quando o indice de vulnerabilidades do NuGet esta indisponivel. Eles nao impedem a compilacao ou os testes.

## Baseline atual de migrations

O historico de migrations foi consolidado em uma unica baseline oficial:

```text
20260829151400_InitialCreate
```

A pasta [src/Lodira.Infrastructure/Persistence/Migrations](src/Lodira.Infrastructure/Persistence/Migrations) contem somente essa migration, seu Designer e `LodiraDbContextModelSnapshot.cs`. A baseline foi validada em banco PostgreSQL `prumo` vazio usando exclusivamente `dotnet ef database update`.

A partir do primeiro banco Beta com dados importantes, migrations devem ser append-only. Este squash nao deve ser repetido depois desse marco.

Checkpoint local:

```text
Commit: be47836
Tag: lodira-2.0-r6-migration-squash
```

O banco vazio nao depende de seed manual: `POST /api/portfolios` cria uma carteira e Strategy/Objective podem ser configurados posteriormente.

As chaves OpenAI/Gemini devem ser fornecidas por variaveis de ambiente, como `AI__ApiKey`; nenhuma chave real de provider esta versionada. Os valores `prumo` usados em `appsettings` e `docker-compose.yml` sao defaults locais de desenvolvimento.

Ownership futuro deve iniciar em `Portfolio.UserId`. Positions, imports, Objective, Strategy, StrategyAllocation, PortfolioAssessment e RadarItemState sao dependentes da carteira. Assets, Documents, DocumentAnalysis e InvestmentEvent permanecem globais nesta etapa.

Debitos conhecidos mantidos fora deste marco: teste real de concorrencia PostgreSQL para `PortfolioAssessment`, configuracao Beta dedicada, HSTS/Auth e warnings conhecidos de frontend/EF.

## Reset local do banco

Para simular uma instalação limpa em Development, execute a partir da raiz:

```powershell
cd D:\Dev\lodira
.\scripts\reset-local-database.ps1
```

O script lê a connection string da API, bloqueia hosts remotos, exige a confirmação literal `RESET`, solicita interativamente a senha do usuário local sem armazená-la e reseta o schema `public`. Em seguida, reaplica as migrations atuais com `dotnet ef database update` e valida o histórico e as tabelas vazias. Nenhum seed, carteira ou usuário é criado.
