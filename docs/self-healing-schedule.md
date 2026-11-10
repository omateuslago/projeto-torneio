# Milestone 9 — Self-Healing Schedule v1

## Fluxo

`POST /api/tournaments/{id}/schedule/repair/generate` recebe:

```json
{
  "horizon": {"horizonStart":"2026-11-14T18:00:00Z", "horizonEnd":"2026-11-15T02:00:00Z", "granularityMinutes":15},
  "incident": {"kind":"playingAreaUnavailable", "resourceId":"UUID", "startsAt":"2026-11-14T18:00:00Z", "endsAt":"2026-11-14T19:00:00Z"}
}
```

O incidente também pode ser `refereeUnavailable` ou `teamUnavailable`, com
`resourceId`, início e fim; ou `matchDelayed`, com somente `matchId` e
`delayMinutes` (1–240). Recursos devem pertencer ao campeonato e estar ativos.

Snapshot consistente → Python/CP-SAT → revalidação C# → preview antes/depois →
aprovação humana → `POST .../repair/apply` com somente `proposalToken` → commit.
Sem impacto, o retorno informa “Nenhuma alteração necessária”, zero mudanças e
nenhum token. `infeasible` não relaxa regras; `timeout` não prova impossibilidade.
`feasible` entrega reparo válido, mas ainda sem prova do mínimo; `optimal` prova
ótimo do objetivo inteiro abaixo. Prazo do solver: 5 segundos.

## Escopo e incidentes

Somente reservas não finalizadas do campeonato inteiramente contidas no horizonte
podem mudar. Partidas pendentes sem horário não entram. Finalizadas, externas e
fora do horizonte são fixas. Incidente conflitante com uma reserva imutável
retorna inviável. Até 200 candidatas e horizonte de até 31 dias.

Indisponibilidade remove temporariamente o intervalo das janelas do recurso.
Intervalos são semiabertos: adjacência é permitida. Restrições existentes de
áreas, árbitros, equipes, duração, grade e ocupações continuam obrigatórias.
A presença/ausência de árbitro é preservada por partida; não há um modo global
que remova arbitragem durante o reparo.

`matchDelayed` não muda a reserva nem a duração persistida: mantém início,
área e árbitro e estende apenas a ocupação operacional até `EndsAt + atraso`.
A própria partida incidente nunca é candidata. Sua reserva precisa pertencer ao
campeonato e não estar finalizada. Outros jogos conflitantes podem ser movidos.
Não há incidente nem duração individual persistidos: reservas manuais posteriores
não lembram esse atraso; é uma análise/aplicação transitória.

## Minimal Change com limites seguros

O módulo `repair.py` reutiliza a construção das hard constraints de `solver.py`,
fornecendo somente o objetivo de reparo e exigência de árbitro por partida.
Não há cascata procedural nem segundo solve. CP-SAT escolhe simultaneamente os
horários/recursos de todas as candidatas, permitindo cascatas quando necessárias.

Para N candidatas e horizonte H em minutos inteiros:

```text
Rmax = 2N
Lmax = N × H × (Rmax + 1) + Rmax
objetivo = partidasAlteradas × (Lmax + 1)
         + deslocamentoTotalMinutos × (Rmax + 1)
         + mudançasDeÁreaEÁrbitro
```

Uma partida é alterada se início, área ou árbitro mudar. Deslocamento é absoluto;
pode antecipar ou adiar dentro do horizonte. Cada partida muda até dois recursos,
e desloca no máximo H minutos. Assim, uma mudança adicional custa mais que toda
melhora possível nos níveis inferiores; um minuto custa mais que todas as trocas
de recursos. Esses limites provam a prioridade lexicográfica, sem pesos empíricos.
Com N≤200 e H≤44640, os coeficientes/objetivo cabem em int64.

## Segurança, atomicidade e métricas

Token autenticado/cifrado específico de reparo, com campeonato, incidente,
horizonte, assignments, hash do snapshot e expiração em dez minutos. Não aceita
mudanças arbitrárias do navegador. Lock global de scheduling e lock do campeonato
são os mesmos do Autopilot. Estado alterado/expirado retorna 409; token adulterado
ou de outro campeonato retorna 400. Preview nunca chama SaveChanges.

C# revalida a composição exata de candidatas, horizonte, grade, arbitragem,
incidente e colisões na **agenda final inteira** antes de usar ScheduleWriter.
Somente após essa verificação ele pode ignorar as reservas antigas das partidas
alteradas nas verificações individuais; isso permite trocas sem colisão falsa.
ScheduleWriter valida disponibilidade e recursos ativos de todas as candidatas.
Só campos realmente diferentes são persistidos e só mudanças reais recebem
`MatchRescheduled`, com antes/depois. Commit único inclui auditoria; erro em
qualquer etapa reverte tudo.

Preview apresenta incidente, mudanças antes/depois e métricas: impactedCount
(reservas diretamente conflitantes), changedCount (inclui cascatas), preservedCount
(reservas do campeonato não alteradas), deslocamento total/máximo e trocas de
área/árbitro. Nenhum score artificial de Self-Healing é criado.

Fairness e Resilience são recalculados após a solução, com as janelas temporárias
e ocupação estendida do incidente. Suas fórmulas não mudam e não entram no objetivo
do reparo. Em no-op não são calculados novos diagnósticos. Fairness otimiza justiça
na geração inicial; Resilience analisa alternativas; Self-Healing propõe mudanças
operacionais mínimas que ainda dependem de aprovação humana.

## Limitações

- Um incidente por proposta; sem histórico, monitoramento ou reparo automático.
- Reservas candidatas devem ser compatíveis com a duração atual e grade relativa
  ao início do horizonte. Incompatibilidade retorna erro explícito, sem arredondar
  silenciosamente as reservas. Ajuste horizonte/grade antes de repetir.
- Recursos fixos são arredondados conservadoramente em minutos, como no solver
  original. Não há novos critérios de preferência, viagens ou otimização de score.
- Um worker, seed zero e IDs ordenados favorecem determinismo; o limite de tempo
  pode variar o resultado entre máquinas. `feasible` não garante mínimo provado.
- Ocupações externas são aquelas relevantes fornecidas pelo snapshot atual.
- O MVP mantém suas limitações de autenticação existentes.
