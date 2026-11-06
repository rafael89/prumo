<p align="center">
  <img src="frontend/prumo-web/public/prumo_logo.png" alt="Prumo" width="240">
</p>

# Prumo

[![CI](https://github.com/rafael89/prumo/actions/workflows/ci.yml/badge.svg)](https://github.com/rafael89/prumo/actions/workflows/ci.yml)
[![Versão](https://img.shields.io/github/v/tag/rafael89/prumo?filter=v*&label=vers%C3%A3o&sort=semver)](https://github.com/rafael89/prumo/tags)
[![Docker Compose](https://img.shields.io/badge/Docker_Compose-ready-2496ED?logo=docker&logoColor=white)](https://docs.docker.com/compose/)

O Prumo é uma aplicação self-hosted para acompanhar sua carteira de investimentos brasileira. Ele reúne posições, documentos da CVM/FNet e análises com IA para ajudar a entender eventos relevantes e planejar novos aportes.

- [Instalação rápida](#instalação-rápida)
- [Configuração](#configuração)
- [Desenvolvimento](#desenvolvimento)

## Objetivo

Dar contexto às decisões sobre a carteira sem exigir o acompanhamento manual de cada documento publicado. Importe suas posições, defina as alocações desejadas e use o Radar para revisar o que merece atenção.

## Funcionalidades

- Importação do Relatório Consolidado Mensal da B3 em XLSX, com prévia antes da confirmação.
- Cadastro e acompanhamento de carteiras, posições e alocações-alvo.
- Descoberta e análise de documentos da CVM/FNet, com avaliação do impacto na sua carteira.
- Radar de eventos relevantes, organizado por prioridade.
- Orientação de aportes com cenários de alocação e registro das compras realizadas.
- Histórico de posições, eventos e análises.
- Tema claro e escuro e modo de privacidade para ocultar valores.
- Integração com Gemini, OpenAI e provedores HTTP de IA.

## Instalação rápida

Você precisa de [Git](https://git-scm.com/) e [Docker com Compose](https://docs.docker.com/compose/install/).

1. Clone o repositório e copie o arquivo de configuração:

   ```sh
   git clone https://github.com/rafael89/prumo.git
   cd prumo
   cp .env.example .env
   ```

   No PowerShell, use `Copy-Item .env.example .env` para copiar o arquivo.

2. Edite o `.env`: altere `POSTGRES_PASSWORD` e preencha `AI_API_KEY` com a chave do provedor escolhido. O exemplo usa Gemini.

3. Construa e inicie os serviços:

   ```sh
   docker compose up -d --build
   ```

4. Acesse [http://localhost:5273](http://localhost:5273), crie uma carteira e importe suas posições ou cadastre os ativos manualmente.

O Compose inicia o frontend, a API, o worker e o PostgreSQL. As migrações do banco são aplicadas automaticamente. Dados e documentos ficam em volumes Docker.

Para atualizar uma instalação feita a partir do código:

```sh
git pull --ff-only
docker compose up -d --build
```

## Configuração

As opções mais comuns estão no [.env.example](.env.example):

| Variável | Uso |
| --- | --- |
| `POSTGRES_PASSWORD` | Senha do banco de dados. Altere antes de iniciar. |
| `AI_PROVIDER` | Provedor de IA: `Gemini`, `OpenAI` ou `Http`. |
| `AI_API_KEY` | Chave do provedor. `GEMINI_API_KEY` também é aceita como alternativa. |
| `AI_MODEL` | Modelo usado nas análises. |
| `AI_FALLBACK_MODEL` | Modelo alternativo, quando aplicável ao provedor. |
| `DISCORD_WEBHOOK_URL` | Webhook opcional para notificações de logs. |

Ao trocar de provedor, ajuste também os modelos. O provedor `Http` exige `AI__Endpoint` no ambiente do worker. A integração OpenAI atende à análise documental; nesse caso, a orientação de aportes usa o motor de regras.

O Prumo foi pensado para uso pessoal em rede privada e ainda não possui autenticação de usuários. Para acesso remoto, proteja o frontend com VPN ou um proxy com autenticação e HTTPS. Não exponha o banco nem a API diretamente à internet.

## Desenvolvimento

O Prumo usa .NET 10, React, TypeScript, Vite, Tailwind CSS e PostgreSQL 16. O processamento de documentos e as análises assíncronas são executados por um worker separado da API.

### Ambiente local

Instale o .NET SDK definido em [global.json](global.json), Node.js 26.12.0 e Docker com Compose.

Na raiz do repositório, copie `.env.example` para `.env`, configure a senha do banco e inicie o PostgreSQL:

```sh
docker compose up -d postgres
dotnet restore Prumo.sln
```

Para executar API e worker fora do Compose, configure `ConnectionStrings:Postgres` em ambos os projetos com as mesmas credenciais do `.env`. O .NET não carrega esse arquivo automaticamente. Você pode usar variáveis de ambiente (`ConnectionStrings__Postgres`) ou `dotnet user-secrets`.

As configurações de desenvolvimento usam `D:\storage\documents` para armazenamento. Ajuste `Storage:RootPath` nos dois projetos se precisar de outro caminho.

Aplique as migrações com a ferramenta `dotnet-ef` compatível com EF Core 10:

```sh
dotnet ef database update --project src/Prumo.Infrastructure --startup-project src/Prumo.Api
```

Em terminais separados, execute:

```sh
dotnet run --project src/Prumo.Api
```

```sh
dotnet run --project src/Prumo.Worker
```

```sh
cd frontend/prumo-web
npm ci
npm run dev
```

O frontend fica em [http://localhost:5273](http://localhost:5273) e encaminha `/api` para [http://localhost:5200](http://localhost:5200). O Swagger está disponível em [http://localhost:5200/swagger](http://localhost:5200/swagger) no ambiente Development.

### IA e notificações

Configure credenciais com variáveis de ambiente ou user-secrets, sem editar arquivos versionados. Exemplo para o worker:

```sh
dotnet user-secrets set "AI:Provider" "Gemini" --project src/Prumo.Worker
dotnet user-secrets set "AI:ApiKey" "<sua-chave>" --project src/Prumo.Worker
```

`AI:Model` e `AI:FallbackModel` definem os modelos. Para OpenAI, configure um modelo compatível com Responses API e saída estruturada. O provedor `Http` exige `AI:Endpoint`. O provedor `Fake` é permitido apenas fora de Production, para desenvolvimento e testes.

Na análise documental, os provedores disponíveis são `Gemini`, `OpenAI`, `Http` e `Fake`. A orientação de aportes usa IA com `Gemini` e `Http`; com `OpenAI` e `Fake`, usa o motor determinístico.

Para notificações opcionais, configure `Discord:WebhookUrl` na API e no worker. No Compose, `DISCORD_WEBHOOK_URL` é repassada aos dois serviços. Sem um webhook configurado, as notificações ficam inativas.

### Testes

Na raiz do repositório:

```sh
dotnet build Prumo.sln
dotnet test Prumo.sln
```

Os testes relacionais exigem PostgreSQL e `PRUMO_RUN_RELATIONAL_TESTS=1`. Sem essa opção, são ignorados. Eles usam bancos temporários; confira a configuração local antes de ativá-los.

No diretório `frontend/prumo-web`:

```sh
npm ci
npm run lint
npm test
npm run build
```

No Windows, use `npm.cmd` se o PowerShell bloquear `npm.ps1`.

### Versões

O build do frontend identifica a maior tag estável (`vMAJOR.MINOR.PATCH`) alcançável a partir do commit compilado. Sem tags, exibe `desenvolvimento`. O CI busca o histórico e passa a versão à imagem pelo argumento de build `PRUMO_VERSION`.

Em builds locais com Compose, defina `PRUMO_VERSION` no ambiente ou no `.env` com a tag correspondente ao código compilado; sem isso, a imagem exibe `desenvolvimento`. Builds versionados consultam as tags públicas do GitHub ao abrir a aplicação e a cada hora para indicar atualizações disponíveis.

### Estrutura

| Diretório | Responsabilidade |
| --- | --- |
| `src/Prumo.Api` | API HTTP e operações sobre carteiras. |
| `src/Prumo.Worker` | Descoberta, processamento e análise de documentos. |
| `src/Prumo.Application` | Casos de uso e regras da aplicação. |
| `src/Prumo.Domain` | Entidades do domínio. |
| `src/Prumo.Infrastructure` | Persistência, migrações e integrações. |
| `frontend/prumo-web` | Interface web. |
| `tests` | Testes do backend. |

Para detalhes do frontend, consulte seu [README](frontend/prumo-web/README.md).

## Licença

Este projeto está disponível sob a [licença MIT](LICENSE).
