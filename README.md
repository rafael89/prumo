# Prumo

Aplicacao self-hosted para acompanhar eventos relevantes dos ativos de uma carteira de investimentos brasileira.

Este repositorio esta no Incremento 3: descoberta e armazenamento de documentos. Processamento de PDFs, análises de IA e recomendações pertencem aos próximos incrementos.

## Stack

- React, TypeScript, Vite e Tailwind CSS
- ASP.NET Core Web API em .NET 10 LTS
- .NET Worker
- PostgreSQL 16
- Docker Compose

## Executar localmente

### API e worker

```powershell
dotnet restore Prumo.sln
dotnet build Prumo.sln
dotnet run --project src/Prumo.Api
```

A API fica disponivel em `https://localhost:5001` ou `http://localhost:5000`. O health check esta em `/health`.

### Frontend

```powershell
Set-Location frontend/prumo-web
npm install
npm run dev
```

O frontend fica disponivel em `http://localhost:5173`.

## Carteira e API

Os endpoints de carteira ficam sob `/api/portfolios`:

- `GET` e `POST /api/portfolios`
- `GET`, `PUT` e `DELETE /api/portfolios/{id}`
- `GET /api/portfolios/{id}/dashboard`
- `POST /api/portfolios/{id}/positions`
- `PUT` e `DELETE /api/portfolios/{id}/positions/{positionId}`

Ao criar uma posição, `currentPrice` é opcional. Quando informado, o backend calcula `currentValue` e `portfolioWeight`; sem cotação, o valor fica nulo e o peso fica zero. O patrimônio soma somente posições com valor calculado.

## Docker Compose

Requer Docker Desktop instalado e em execucao:

```powershell
docker compose up --build
```

Servicos:

- frontend: `http://localhost:5173`
- api: `http://localhost:8080/health`
- postgres: `localhost:5432`

Os dados do PostgreSQL e os documentos persistem nos volumes `postgres-data` e `document-storage`.

## Migrations

A infraestrutura do EF Core esta configurada em `Prumo.Infrastructure`. A migration `20260827173316_InitialCreate` cria as tabelas de ativos, carteiras e posições, com índice único para ticker e para ativo dentro da carteira.

Com a ferramenta EF instalada, uma migration pode ser aplicada com:

```powershell
dotnet ef database update --project src/Prumo.Infrastructure --startup-project src/Prumo.Api
```

Para executar a atualização localmente sem Docker, inicie um PostgreSQL em `localhost:5432` ou defina a connection string `ConnectionStrings__Postgres`.

## Validacao do Incremento 1

```powershell
dotnet build Prumo.sln
dotnet test Prumo.sln
Set-Location frontend/prumo-web
npm run build
npm run lint
```

O build, os testes financeiros, o build/lint frontend e o reconhecimento da migration foram validados. A aplicação da migration e os endpoints que consultam dados exigem PostgreSQL em execução. O Docker Compose não foi executado porque Docker CLI não está instalado ou disponível no `PATH` deste ambiente.

## Documentos

O Worker consulta a fonte configurável `Documents:CvmFnetEndpoint`, busca documentos para os ativos presentes nas carteiras, deduplica por fingerprint SHA-256 e salva downloads no storage local. O formato esperado da fonte é uma lista JSON de `DocumentCandidate`.

O `Fingerprint` é a identidade lógica do documento: combina fonte, ticker, data de publicação, tipo e, preferencialmente, o identificador da fonte; sem identificador, usa a URL normalizada. O `FileHash` é separado e representa o conteúdo físico baixado. URL e identificador possuem índices não únicos, pois a fonte pode reutilizá-los; somente o fingerprint lógico é único.

Endpoints disponíveis:

- `GET /api/portfolios/{id}/documents`
- `GET /api/documents/{id}`

O pipeline atual para em `Downloaded`; extração de texto, jobs e análise pertencem aos próximos incrementos. Sem endpoint CVM/FNET configurado, o Worker permanece ocioso de forma intencional.

A integração CVM/FNET ainda não foi validada end-to-end: este ambiente não possui Docker/PostgreSQL disponíveis e nenhum endpoint CVM/FNET foi configurado. A estratégia de deduplicação e as migrations foram validadas localmente, mas a descoberta e o download contra a fonte real aguardam esse ambiente.
