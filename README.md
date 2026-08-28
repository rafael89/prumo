# Prumo

Aplicacao self-hosted para acompanhar eventos relevantes dos ativos de uma carteira de investimentos brasileira.

O repositorio esta no Incremento 6: descoberta, download, fila de jobs, extracao de texto em PDF, validacao deterministica de qualidade, analise estruturada por IA e contexto enriquecido da carteira.

## Stack

- React, TypeScript, Vite e Tailwind CSS
- ASP.NET Core Web API em .NET 10
- .NET Worker
- PostgreSQL 16
- Docker Compose

## Executar localmente

### API e worker

```powershell
dotnet restore Prumo.sln
dotnet build Prumo.sln
dotnet run --project src/Prumo.Api
dotnet run --project src/Prumo.Worker
```

### Frontend

```powershell
Set-Location frontend/prumo-web
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

O projeto utiliza `UglyToad.PdfPig` na versao `1.7.0-custom-5`, conforme registrado em [src/Prumo.Infrastructure/Prumo.Infrastructure.csproj](/D:/Dev/prumo/src/Prumo.Infrastructure/Prumo.Infrastructure.csproj). A dependencia foi mantida neste incremento porque ja estava instalada no repositrio e e a versao referenciada pelo estado atual do projeto; nao houve troca de biblioteca por preferencia.

Nao ha OCR neste incremento. PDFs sem texto confiavel devem falhar na validacao de qualidade e seguir a politica de retry/erro do job.

## Migrations

As migrations de documentos, jobs e analises ficam em [src/Prumo.Infrastructure/Persistence/Migrations](/D:/Dev/prumo/src/Prumo.Infrastructure/Persistence/Migrations). O incremento adiciona a migration manual `20260827203000_AddDocumentAnalyses`, que cria a tabela `DocumentAnalyses` e relaxa o indice de `ProcessingJobs` para permitir historico de execucoes sem perder a protecao contra duplicacao acidental em andamento.

Para aplicar migrations com ambiente compativel:

```powershell
dotnet ef database update --project src/Prumo.Infrastructure --startup-project src/Prumo.Api
```

## Configuracao

Configuracoes principais do worker em [src/Prumo.Worker/appsettings.json](/D:/Dev/prumo/src/Prumo.Worker/appsettings.json):

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
dotnet run --project src/Prumo.Worker
```

Para usar OpenAI, defina `AI__Provider="OpenAI"`, `AI__ApiKey` e `AI__Model`. Nunca versione chaves no repositorio.

## Validacao realizada neste ambiente

- `npm run build`: aprovado em 2026-08-27
- `npm run lint`: aprovado em 2026-08-27
- `dotnet build Prumo.sln`: aprovado em 2026-08-27
- `dotnet test Prumo.sln`: 71 aprovados, 0 falhos, em 2026-08-27

O Incremento 6 foi validado por testes automatizados. O E2E real de uma nova analise Gemini nao foi executado nesta etapa porque nao havia credencial configurada no ambiente.

## Warning conhecido

Avisos `NU1900` podem aparecer quando o indice de vulnerabilidades do NuGet esta indisponivel. Eles nao impedem a compilacao ou os testes.
