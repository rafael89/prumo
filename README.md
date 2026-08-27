# Prumo

Aplicacao self-hosted para acompanhar eventos relevantes dos ativos de uma carteira de investimentos brasileira.

O repositorio esta no Incremento 5: descoberta, download, fila de jobs, extracao de texto em PDF, validacao deterministica de qualidade, analise estruturada por IA e exibicao basica da analise no frontend.

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
- `AI:ApiKey`
- `AI:Model`
- `AI:TimeoutSeconds`

Sem `AI:Endpoint` e `AI:Model`, a chamada real ao provider nao sera validada neste ambiente. Os testes do pipeline devem usar fakes ou mocks do `IAiAnalysisService`.

## Validacao realizada neste ambiente

- `npm run build`: aprovado em 2026-08-27
- `npm run lint`: aprovado em 2026-08-27
- `dotnet build Prumo.sln`: bloqueado em 2026-08-27 porque o ambiente possui apenas o SDK `6.0.428`, enquanto a solucao exige `.NET 10`

Nao foi possivel validar `dotnet restore`, `dotnet test`, `dotnet ef` nem a chamada real ao provider de IA neste ambiente por ausencia do SDK compativel e de credenciais configuradas.

## Warning conhecido

Existe um warning historico citado para `NU1903` envolvendo `System.Security.Cryptography.Xml 9.0.0`, associado ao tooling/dependencia transitiva do EF. Neste ambiente nao foi possivel confirmar se o warning ainda ocorre nem rastrear a arvore de dependencias com seguranca, porque a restauracao em `.NET 10` esta bloqueada pelo SDK disponivel.
