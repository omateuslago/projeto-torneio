# Milestone 7 — Fair Scheduling

## Hard e soft constraints

Disponibilidade, duração, ausência de sobreposição, grade, recursos e imutabilidade
das partidas fixas continuam obrigatórios. C# revalida a proposta e aplica somente
por token/snapshot, em uma transação. Preview e score não são persistidos.

O CP-SAT agora minimiza penalidades dentro das agendas válidas. Descanso abaixo
da meta e partidas consecutivas são permitidos quando necessários. Não existe
reordenação ou heurística após o solver.

## Escopo e critérios

- **Descanso:** para cada equipe, ordenar suas partidas e medir somente os
  intervalos entre vizinhas cronológicas. Somar `max(0, R - intervalo)`, com
  `R = preferredRestMinutes × 60`. Request aceita 0–240 minutos, padrão 60.
- **Back-to-back:** contar intervalos de zero segundos, independentemente de R.
- **Horários:** para cada equipe com partidas, calcular sua média de início
  relativa ao horizonte, normalizada em 0–1000. Penalizar a distância entre
  a maior e a menor dessas médias. Usar média evita favorecer equipes com menos
  partidas dentro do horizonte.

Participam todas as partidas novas e as fixas **do próprio campeonato inteiramente
contidas no horizonte**. `countsForFairness` é preenchido pelo C# no DTO interno;
o padrão Python é false. Ocupações externas e fixas fora desse recorte continuam
bloqueando recursos, mas não entram no score. Nenhuma alteração de banco.

## Função objetivo, pesos e score

As constantes ficam em `scheduler/app/fairness.py` e são compartilhadas pela
construção do objetivo e pelo cálculo do score. Sejam:

- `N = soma(max(0, número de partidas da equipe - 1))`, número de intervalos;
- `B`, quantidade desses intervalos sem descanso;
- `D`, soma dos déficits de descanso em segundos;
- `H`, duração do horizonte em segundos;
- `M_t = floor(1000 × soma(inícios relativos da equipe) / (partidas da equipe × H))`.

Penalidades inteiras normalizadas de 0 a 1000:

```text
P_b = ceil(1000 × B / N)       (0 se N = 0)
P_r = ceil(1000 × D / (N × R)) (0 se N × R = 0)
P_t = max(M_t) - min(M_t)      (0 se não houver equipes com partidas)

objetivo = 100 × P_b + 10 × P_r + 1 × P_t
model.minimize(objetivo)

score = arredondar(100 × (1 - objetivo / 111000))
backToBackScore = arredondar(100 × (1 - P_b / 1000))
restScore       = arredondar(100 × (1 - P_r / 1000))
timeBalanceScore = arredondar(100 × (1 - P_t / 1000))
```

Arredondamento de meio para cima, limitado a 0–100. O score usa as penalidades
antes do arredondamento dos componentes. Critérios sem intervalos recebem 100
(nenhuma violação observável). Uma agenda vazia também recebe 100; as métricas
de quantidade permanecem visíveis para contextualizar o resultado.

Os pesos **100:10:1** dão prioridade forte a reduzir a proporção de back-to-back,
depois o déficit relativo de descanso, depois a distribuição de horários.
São prioridades ponderadas, não uma ordem lexicográfica absoluta. Por exemplo,
reduzir 1 ponto percentual da proporção de back-to-back equivale a todo o alcance
da penalidade de distribuição. Uma pequena melhora de distribuição não compensa
esse custo. Testes comparam agendas válidas alternativas e verificam igualdade
entre o objetivo CP-SAT e a fórmula do score.

No modelo, o menor intervalo até uma partida posterior representa o sucessor
cronológico; a última partida não gera penalidade. Partidas fixas entram como
constantes. Médias e penalidades são variáveis vinculadas por igualdades inteiras.
Depois de resolver, apenas se calcula o score e diagnósticos da solução escolhida.
As observações mostram por equipe a quantidade de descansos curtos, o menor
descanso e intervalos sem descanso, além da diferença de médias. Não usam IA.

## Limitações

- A média de início mede posição no horizonte, não preferência por manhã/noite,
  fuso local, qualidade de quadra ou distribuição por dia. Médias iguais podem
  esconder dispersões diferentes. Não compara campeonatos/horizontes distintos.
- O recorte exclui jogos fora do horizonte, inclusive descanso na fronteira.
  Restrições fixas podem limitar o melhor score possível.
- Cálculo de fairness usa segundos inteiros (trunca subsegundos das partidas
  fixas) e resolução de 1/1000 nas penalidades/médias. As hard constraints mantêm
  o arredondamento conservador existente em minutos; não foram relaxadas.
- Prazo CP-SAT de 5 segundos, um worker, seed zero. `optimal` prova o mínimo
  dessa função; `feasible` entrega uma solução válida sem prova do mínimo.
  Limite de tempo/máquina pode afetar a solução retornada. A construção do modelo
  cresce com pares envolvendo partidas novas de cada equipe; fixas entre si usam
  sucessores pré-calculados. Os limites de tamanho anteriores continuam valendo.
- O score é relativo aos critérios atuais, não uma garantia absoluta de justiça.
  Arredondar para inteiro pode dar a mesma nota a soluções ligeiramente diferentes.
- C# valida presença, formato e faixa dos componentes, sem duplicar a matemática
  Python. Soluções sem fairness válido retornam 502 e não recebem token.
