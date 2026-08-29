# Lodira

Aplicacao self-hosted para acompanhar eventos relevantes dos ativos de uma carteira de investimentos brasileira.

O repositorio contempla os incrementos funcionais de descoberta e analise de documentos, Radar, priorizacao, orientação de aporte mensal, importação de carteira B3 em XLSX e as melhorias de UX do Incremento 10.

## Stack

- React, TypeScript, Vite e Tailwind CSS
- ASP.NET Core Web API em .NET 10
- .NET Worker
- PostgreSQL 16
- Docker Compose

## Funcionalidades atuais

- Carteiras e posições com detalhe do ativo, busca e paginação server-side.
- Importação do Relatório Consolidado Mensal da B3 em XLSX, com prévia e sincronização.
- Descoberta e processamento de documentos da CVM/FNet.
- Análise global do evento e avaliação do impacto em cada carteira.
- Radar com prioridade, filtros, estados por portfolio e paginação.
- Orientação de aporte mensal baseada em alocações-alvo e eventos que requerem atenção.
- Privacy mode para ocultar valores financeiros na sessão atual.
- Frontend organizado em sections para Dashboard, Carteira e Documentos, com Radar, Aporte e importação B3.

## Fluxo de dados

```text
B3 XLSX -> prévia -> confirmação -> carteira atualizada
CVM/FNet -> descoberta -> download -> extração/validação -> análise IA
Documento -> DocumentAnalysis global -> PortfolioAssessment por portfolio -> Radar/Aporte
```

O backend é a fonte de verdade. A regra `RequiresAttention` é compartilhada pelo Radar e pela orientação de aporte; o frontend apenas apresenta os resultados da API.

## Renomeacao tecnica e compatibilidade

O nome oficial do produto e Lodira, com a solucao e os projetos organizados sob esse nome. O diretorio local pode continuar sendo `prumo` sem impacto no build ou na execucao.

Para preservar ambientes existentes, os valores padrao do PostgreSQL continuam usando o banco, usuario e senha `prumo`, o `UserSecretsId` historico do worker foi mantido e as migrations preservam seu historico, operacoes e dados persistidos. Essas referencias sao deliberadas e nao representam o namespace atual.

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

## Dependencia de PDF

O projeto utiliza `UglyToad.PdfPig` na versao `1.7.0-custom-5`, conforme registrado em [src/Lodira.Infrastructure/Lodira.Infrastructure.csproj](src/Lodira.Infrastructure/Lodira.Infrastructure.csproj). A dependencia foi mantida neste incremento porque ja estava instalada no repositrio e e a versao referenciada pelo estado atual do projeto; nao houve troca de biblioteca por preferencia.

Nao ha OCR neste incremento. PDFs sem texto confiavel devem falhar na validacao de qualidade e seguir a politica de retry/erro do job.

## Migrations

As migrations de documentos, jobs e analises ficam em [src/Lodira.Infrastructure/Persistence/Migrations](src/Lodira.Infrastructure/Persistence/Migrations). O incremento adiciona a migration manual `20260827203000_AddDocumentAnalyses`, que cria a tabela `DocumentAnalyses` e relaxa o indice de `ProcessingJobs` para permitir historico de execucoes sem perder a protecao contra duplicacao acidental em andamento.

Para aplicar migrations com ambiente compativel:

```powershell
dotnet ef database update --project src/Lodira.Infrastructure --startup-project src/Lodira.Api
```

As migrations mais recentes incluem contexto de portfolio no Radar, estratégia de alocação, classes de ativos, sessões de importação B3, CNPJ dos ativos e ajustes de escopo dos estados do Radar. A migration pendente precisa ser aplicada ao banco antes de usar as funcionalidades correspondentes.

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

As listas de carteira, documentos e Radar são paginadas no backend. Filtros resetam a página para 1 e as query keys incluem o `portfolioId` quando o dado é específico da carteira.

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

## Validação automatizada

- `dotnet build Lodira.sln`: aprovado
- `dotnet test Lodira.sln`: 106 aprovados, 0 falhos
- `npm run build`: aprovado
- `npm run lint`: aprovado

O E2E real com serviços externos, como CVM/FNet e providers de IA, depende da configuração correspondente no ambiente.

## Warning conhecido

Avisos `NU1900` podem aparecer quando o indice de vulnerabilidades do NuGet esta indisponivel. Eles nao impedem a compilacao ou os testes.
