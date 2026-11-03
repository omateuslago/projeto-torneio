# Projeto Torneio

Base incremental para organização de campeonatos amadores físicos e eSports.
O milestone 1 oferece Tournament, Team e Registration em uma API .NET 9 com
Controllers, EF Core e PostgreSQL. O frontend Next.js existente foi preservado.

## Executar com Docker

Requisitos: Docker Engine e Docker Compose v2.

```bash
cp .env.example .env
# Defina POSTGRES_PASSWORD em .env antes de iniciar.
docker compose up --build -d
```

API: http://localhost:5094 — Swagger: http://localhost:5094/swagger
O serviço `migrate` aplica migrations antes de iniciar a API. Os dados ficam no
volume `tournament-data`. `docker compose down` preserva esse volume.
As portas são expostas somente em localhost para desenvolvimento.

## Desenvolvimento com SDK .NET 9

Na raiz do repositório, com o banco do Compose em execução:

```bash
docker compose up -d db
dotnet tool restore
export ConnectionStrings__Tournaments='Host=localhost;Database=tournaments;Username=tournaments;Password=SUA_SENHA'
dotnet ef database update --project backend/Projeto-Campeonato.Api
dotnet run --project backend/Projeto-Campeonato.Api --launch-profile http
```

A conexão é fornecida por configuração; nenhuma credencial é versionada.
Para uma nova alteração de modelo:

```bash
dotnet ef migrations add NomeDaAlteracao --project backend/Projeto-Campeonato.Api --output-dir Infrastructure/Migrations
```

## Fluxo do milestone 1

1. `POST /api/tournaments` com `{"name":"Copa","sport":"Futsal","maxTeams":8,"format":"RoundRobin"}`.
2. `POST /api/teams` com `{"name":"Equipe Azul"}`.
3. `PUT /api/tournaments/{id}/status` com `{"status":"Open"}`.
4. `POST /api/tournaments/{id}/registrations` com `{"teamId":"UUID_DA_EQUIPE"}`.
5. `GET /api/tournaments/{id}/registrations` lista os participantes.

Também existem GET de lista e por ID para torneios e equipes. IDs são UUIDs.
Torneios começam em `Draft`; as transições são `Draft → Open → Closed`.
Repetir o estado atual é permitido. Inscrições exigem estado `Open`, equipe
existente e vaga disponível (capacidade entre 2 e 128). Uma equipe só pode se
inscrever uma vez em cada torneio. Formatos aceitos: `SingleElimination` e
`RoundRobin`; geração de confrontos pertence ao próximo milestone.

Erros retornam ProblemDetails: 400 para dados inválidos, 404 para recurso
inexistente e 409 para conflito de domínio ou concorrência. Após 409, consulte
novamente o estado antes de tentar a inscrição. As transações serializáveis do
PostgreSQL e o índice único protegem vagas e duplicidades entre processos.

## Testes

Os testes de integração usam PostgreSQL real e aplicam a migration em um banco
isolado, com nome aleatório, removido ao terminar. O usuário de teste precisa de
permissão CREATEDB (o usuário local do Compose já possui).

```bash
export TEST_POSTGRES_CONNECTION='Host=localhost;Database=postgres;Username=tournaments;Password=SUA_SENHA'
dotnet test backend/Projeto-Campeonato.sln
```

Para rodar somente regras de domínio sem PostgreSQL:

```bash
dotnet test backend/Projeto-Campeonato.sln --filter FullyQualifiedName~DomainTests
```

A suíte cobre validação, persistência entre requisições, estados, participantes,
recursos inexistentes, duplicidades, concorrência pela última vaga, migration e
compatibilidade do endpoint legado.

## Compatibilidade e próximos passos

`GET/POST /campeonatos` mantém o contrato e o armazenamento em memória originais,
utilizados pelo frontend. Essa coleção legada é independente dos novos torneios
persistidos em `/api/tournaments`. A integração/migração do frontend deve ser um
milestone explícito; nenhuma migração para Angular foi feita nesta recuperação.

A API ainda não tem autenticação. O Compose é destinado a desenvolvimento local.
Partidas, resultados, autenticação, agendamento Python, disputas, notificações,
WhatsApp e pagamentos ficam para etapas posteriores.

Veja [decisões de arquitetura](docs/architecture.md) e
[comparação da recuperação](docs/recovery-milestone-1.md).
