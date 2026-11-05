# Decisão 001 — Base do milestone 1

## Contexto

O repositório já usa .NET 9 e Next.js. O backend original possui apenas dois
endpoints de campeonatos em memória. O objetivo desta etapa é recuperar a base
de torneios, equipes e inscrições, preservando o que já funciona.

## Decisão

Manter um único projeto de API, organizado por responsabilidade:

- `Domain`: entidades e regras de nomes, capacidade e estado.
- `Features`: Controllers e DTOs de torneios, equipes e inscrições.
- `Infrastructure`: DbContext, mapeamentos, migrations e tratamento de erros.
- Projeto separado de testes de domínio e HTTP com PostgreSQL real.

Controllers usam DbContext diretamente. Uma camada de aplicação ou serviços
será extraída quando os casos de uso precisarem ser reutilizados; não há
repository genérico. As entidades têm setters privados e não são contratos HTTP.

Inscrição e alteração de estado usam isolamento Serializable. PostgreSQL pode
abortar uma transação concorrente; a API traduz esse conflito para 409, permitindo
que o cliente atualize o estado e tente novamente. O índice único de
(TournamentId, TeamId) reforça a regra no banco. FKs impedem referências órfãs.

O banco é atualizado por um comando explícito (`--migrate` ou ferramenta EF).
No Compose, um serviço de execução única faz isso antes da API; a inicialização
normal de cada instância não executa migrations automaticamente.

## Compatibilidade e limites

Os endpoints legados continuam intactos e independentes; a migração dos dados e
do frontend exige um plano próprio. `Sport` é uma string nesta etapa; regras por
modalidade e geração de confrontos serão adicionadas quando existirem casos de
uso concretos. Closed significa inscrições encerradas, não campeonato concluído.
Não há autenticação, exclusão, partidas ou resultados neste milestone.

O futuro scheduler Python receberá DTOs da API .NET via HTTP e não terá acesso
direto ao banco. Não há serviço Python vazio nesta etapa.

## Evolução

Esta decisão registra a base histórica do milestone 1. O estado atual usa .NET 10
e Angular; veja [base Angular](angular-foundation.md) e
[ciclo de vida do campeonato](tournament-lifecycle.md) para o milestone de partidas,
resultados e classificação.

No milestone 4, [resultados bilaterais e disputas](bilateral-results.md) acrescentam
ScoreSubmission, Dispute e AuditLog no mesmo projeto. O pequeno ResultWorkflow
centraliza aplicação do resultado oficial, encerramento da disputa e do campeonato
para os fluxos bilateral, resolução e override. Os controllers mantêm transações e
bloqueio do campeonato; não há repository genérico nem infraestrutura de eventos.
Pendências e classificação são leituras derivadas. A identidade enviada é simulada,
com futura substituição pela equipe do usuário autenticado na entrada do caso de uso.

No milestone 5, [Scheduling Foundation](scheduling-foundation.md) acrescenta locais,
áreas de jogo e árbitros por campeonato, uma tabela simples de disponibilidade com
FKs explícitas e agendamento manual. ScheduleTime concentra a semântica dos intervalos;
SchedulingController valida e grava sob transação PostgreSQL. A interface possui
uma página de agenda vinculada ao campeonato, sem biblioteca de calendário.

O [milestone 6](tournament-autopilot.md) introduz FastAPI + OR-Tools como serviço
stateless de cálculo. O C# monta DTOs, valida com ScheduleWriter compartilhado pelo
manual, protege a identidade da proposta e aplica atomicamente após conferir o
snapshot. O Python nunca acessa o banco. O key ring persiste fora do Git; não há
tabela de propostas. A dependência externa é exclusiva do fluxo Autopilot.
