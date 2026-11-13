# Projeto Torneio

[![CI](https://github.com/omateuslago/projeto-torneio/actions/workflows/ci.yml/badge.svg)](https://github.com/omateuslago/projeto-torneio/actions/workflows/ci.yml)

Plataforma incremental para campeonatos amadores físicos e eSports. A base atual
usa Angular 22, ASP.NET Core 10 LTS, EF Core 10 e PostgreSQL 17.

A interface permite criar e listar campeonatos e equipes, consultar detalhes,
abrir/fechar inscrições, gerenciar participantes, gerar confrontos de pontos corridos,
enviar resultados por equipe, confirmar placares concordantes, resolver disputas,
consultar pendências e classificação, além de corrigir resultados administrativamente. Os dados vêm da API real e
persistem no PostgreSQL. O frontend Next.js anterior está preservado em
`frontend-legacy/`; os 39 arquivos originais foram mantidos sem alterações.

Em **Agenda e disponibilidade**, cada campeonato permite cadastrar locais, áreas
de jogo e árbitros, informar disponibilidade por data e agendar/reagendar partidas.
A API verifica disponibilidade e conflitos de equipes, quadra e árbitro. Consulte
[Scheduling Foundation](docs/scheduling-foundation.md) para contratos e decisões.

O **Tournament Autopilot** gera propostas usando FastAPI e OR-Tools CP-SAT.
Na agenda, informe horizonte, granularidade e opção de árbitros, gere o preview e
clique em **Aplicar agenda** para persistir. Partidas já agendadas são preservadas.
Veja [Autopilot v1](docs/tournament-autopilot.md).

## Requisitos

- Node.js 24.15 ou superior da linha 24 (ou uma versão compatível com `frontend/package.json`).
- npm e Docker com Compose v2.
- SDK .NET 10 estável (selecionado por `global.json`) somente para desenvolver/testar o backend
  diretamente no host ou executar comandos `dotnet` manualmente.

## Iniciar localmente

Depois de criar o `.env`, suba rapidamente o ambiente completo com:

```bash
bash iniciar.sh
```

Esse fluxo executa PostgreSQL, scheduler, migrations e API pelo Docker Compose e
mantém apenas o Angular no host. Para simplesmente rodar o projeto, não é necessário
instalar o SDK .NET localmente. Ao pressionar Ctrl+C, o Angular é encerrado e os
containers continuam rodando, preservando o banco.

### 1. PostgreSQL

Na raiz do repositório:

```bash
cp .env.example .env
# Edite POSTGRES_PASSWORD em .env e use essa mesma senha na conexão abaixo.
docker compose up -d db
```

O banco usa a porta 5432 em localhost e persiste no volume `tournament-data`.
`docker compose down` preserva o volume.

### 2. API .NET 10

Ainda na raiz, em um terminal (exemplo para Bash):

```bash
dotnet tool restore
export ConnectionStrings__Tournaments='Host=localhost;Database=tournaments;Username=tournaments;Password=SUA_SENHA'
dotnet ef database update --project backend/Projeto-Campeonato.Api
dotnet run --project backend/Projeto-Campeonato.Api --launch-profile http
```

No PowerShell, defina a variável com
`$env:ConnectionStrings__Tournaments = 'Host=localhost;Database=tournaments;Username=tournaments;Password=SUA_SENHA'`.

API: http://localhost:5094 — Swagger: http://localhost:5094/swagger.
Não há credenciais de banco versionadas. Migrations existentes foram preservadas.

### 3. Frontend Angular

Em outro terminal:

```bash
cd frontend
npm ci
npm start
```

Abra http://localhost:4200. O Angular usa `HttpClient` e a URL base centralizada em
`frontend/src/environments/environment.ts`. O proxy de desenvolvimento em
`frontend/proxy.conf.json` encaminha `/api/**` para `http://127.0.0.1:5094`.
Isso mantém a mesma origem no navegador e dispensa mudanças de CORS no backend.

Em uma hospedagem futura, sirva o conteúdo de `frontend/dist/tournament-web/browser`,
encaminhe `/api` ao backend e configure fallback para `index.html` nas rotas Angular.
O proxy do `ng serve` é exclusivo do desenvolvimento.

## Validar a persistência pela interface

1. Em **Equipes**, cadastre uma equipe.
2. Em **Campeonatos**, informe nome, modalidade, capacidade e formato e clique em **Criar campeonato**.
3. Recarregue a página: os detalhes continuam disponíveis, carregados pela API.
4. Abra as inscrições, selecione a equipe e clique em **Inscrever equipe**.
5. Inscreva pelo menos duas equipes e encerre as inscrições.
6. Clique em **Gerar confrontos** (somente pontos corridos).
7. Use **Enviar como** para simular cada equipe. Ambas informam o placar na ordem
   mandante × visitante. Placares iguais confirmam o resultado e atualizam a classificação.
8. Placares divergentes abrem uma disputa em **Pendências**. Use **Resolver disputa**,
   informe o placar oficial e o motivo. Recarregue para conferir a persistência.
9. A última partida oficialmente finalizada encerra o campeonato, sem disputas abertas.
   O registro/correção manual continua em **Ação administrativa**.

Para conferir diretamente no banco:

```bash
docker compose exec db psql -U tournaments -d tournaments -c 'SELECT "Id", "Name", "Status" FROM "Tournaments";'
```

## API e regras atuais

| Operação | Endpoint |
| --- | --- |
| Listar/criar campeonatos | GET/POST `/api/tournaments` |
| Consultar campeonato | GET `/api/tournaments/{id}` |
| Alterar estado de inscrições | PUT `/api/tournaments/{id}/status` |
| Listar/criar equipes | GET/POST `/api/teams` |
| Consultar equipe | GET `/api/teams/{id}` |
| Consultar/inscrever participantes | GET/POST `/api/tournaments/{id}/registrations` |
| Gerar confrontos | POST `/api/tournaments/{id}/matches/generate` |
| Listar partidas | GET `/api/tournaments/{id}/matches` |
| Registrar/corrigir placar | PUT `/api/matches/{id}/result` |
| Enviar/corrigir submissão da equipe | PUT `/api/matches/{id}/score-submissions/{teamId}` |
| Resolver disputa | PUT `/api/disputes/{id}/resolve` |
| Consultar histórico da partida | GET `/api/matches/{id}/audit-log` |
| Consultar classificação | GET `/api/tournaments/{id}/standings` |

Torneios começam em `Draft`; as transições são `Draft → Open → Closed → InProgress → Finished`. Closed
significa inscrições encerradas. Não é permitido reabrir um torneio fechado.
Capacidade entre 2 e 128, com uma inscrição por equipe em cada campeonato.
O cadastro aceita `SingleElimination` e `RoundRobin` por compatibilidade, mas
somente `RoundRobin` gera partidas e classificação. A geração inicia o campeonato;
a última partida finalizada o encerra. Vitória vale 3 pontos, empate 1 e derrota 0.
As regras continuam no backend. Veja [o ciclo de vida](docs/tournament-lifecycle.md)
e [resultados bilaterais e disputas](docs/bilateral-results.md).

O sistema usa ASP.NET Core Identity com cookie seguro e antiforgery. Organizadores
administram somente os campeonatos vinculados, managers enviam placares apenas por
equipes vinculadas e participantes, e `PlatformAdmin` pode administrar dados sem
vínculo. Veja [o modelo de autenticação e autorização](docs/authentication-authorization.md).

Erros usam ProblemDetails: 400 para validação, 403 para equipe externa à partida,
404 para recurso inexistente e 409
para conflito de domínio ou concorrência. O Angular mostra mensagens para
inscrição duplicada, lotação e inscrições fechadas, sem apagar os formulários
após falhas. PostgreSQL Serializable e o índice único protegem as inscrições
concorrentes. A interface também trata carregamento, listas vazias e falhas de conexão.

## Build e testes

Para geração automática e E2E, inicie também o scheduler em outro terminal:

```bash
python3 -m venv scheduler/.venv
scheduler/.venv/bin/pip install -r scheduler/requirements.txt
cd scheduler
.venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8000
```

A API usa `http://localhost:8000` por padrão; configure `Scheduler__BaseUrl` quando
necessário. `Scheduler__TimeoutSeconds` tem padrão 10. Scheduler offline afeta
somente o Autopilot, mantendo cadastro, resultados e agendamento manual disponíveis.
Saúde: `GET /health` no Python e `GET /api/scheduler/health` na API .NET.

Na raiz, com PostgreSQL iniciado:

```bash
dotnet build backend/Projeto-Campeonato.sln
export TEST_POSTGRES_CONNECTION='Host=localhost;Database=postgres;Username=tournaments;Password=SUA_SENHA'
dotnet test backend/Projeto-Campeonato.sln
cd frontend
npm run build
npm test -- --watch=false
```

Os testes de backend incluem testes HTTP com PostgreSQL real em banco isolado por execução, com migrations aplicadas
e remoção ao terminar. O usuário precisa de CREATEDB; o usuário local do Compose
já possui. Para executar somente domínio sem banco:
`dotnet test backend/Projeto-Campeonato.sln --filter FullyQualifiedName~DomainTests`.

Os testes Angular cobrem autenticação, permissões, criação, validação, erros de inscrição e recuperação
após falha HTTP, partidas, classificação, submissões, disputas, pendências e respostas
atrasadas de status, recursos, disponibilidade, agendamento e conflitos. Não dependem de backend em execução.

Para os oito testes de navegador, mantenha API, scheduler e banco de desenvolvimento
iniciados e, dentro de `frontend`, execute:

```bash
npx playwright install chromium
npm run e2e
```

O runner inicia o Angular automaticamente. Alternativamente, indique um Chromium
já instalado: `CHROMIUM_PATH=/usr/bin/chromium npm run e2e` (Bash).
Esses testes usam a API real, criam dados identificados por timestamp no banco de
desenvolvimento e verificam persistência após recarregar, duplicidade, lotação,
fechamento concorrente, geração de confrontos, registro/correção de placares,
confirmação bilateral, resolução de disputa, classificação atualizada, disponibilidade,
agendamento/reagendamento com conflito real e layout em tela pequena. Não há limpeza de registros
por API, pois exclusão ainda não faz parte do produto. Screenshots e traces
ficam em `frontend/test-results/` (ignorado pelo Git).

Testes Python (14 casos, sem PostgreSQL), dentro de `scheduler`:
`.venv/bin/python -m pytest -q`. Lint: `.venv/bin/ruff check app tests`.

## API inteira via Docker Compose

```bash
docker compose up --build -d
```

O serviço `migrate` aplica migrations antes da API. As imagens usam .NET 10;
as portas do banco e API ficam restritas a localhost.
Neste ambiente cloud, o download da imagem oficial foi bloqueado por HTTP 403
em `westcentralus.data.mcr.microsoft.com`. O PostgreSQL via Compose e a API pelo
SDK foram validados; a execução completa da imagem API aguarda uma rede com
acesso a esse domínio. Consulte [a migração .NET 10](docs/dotnet10-upgrade.md).

O Compose também inicia `scheduler` com Python 3.12 e healthcheck. A API usa o
hostname interno `http://scheduler:8000`, sem depender da saúde dele para iniciar.
O volume `proposal-keys` mantém as chaves dos tokens entre reinicializações. O
Angular continua rodando via `npm start`. Para redes cloud com proxy TLS, veja o
[build com CA temporária](scheduler/README.md).

## Escopo e arquitetura

- `backend/`: API com domínio, features, infraestrutura e testes.
- `frontend/`: Angular standalone, signals, Reactive Forms e rotas por feature.
- `frontend-legacy/`: Next.js preservado como referência.
- `docs/`: decisões e histórico dos milestones.

`GET/POST /campeonatos` permanece compatível com o protótipo antigo e continua
em memória, independente da API persistida. O Angular usa somente `/api`.
A aplicação atual é destinada a desenvolvimento local e ainda não tem autenticação.
Não foram adicionados IA generativa, notificações, WhatsApp, autenticação, evidências ou pagamentos.

Documentação: [arquitetura inicial](docs/architecture.md),
[recuperação do milestone 1](docs/recovery-milestone-1.md) e
[base Angular](docs/angular-foundation.md) e [milestone 4](docs/bilateral-results.md).
