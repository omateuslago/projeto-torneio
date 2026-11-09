# Milestone 8 — Resilience Score

A análise ocorre depois de CP-SAT e Fairness, sem outro solve, sorteio ou alteração
na agenda. Para uma mesma agenda/input, retorna os mesmos números e observações.
Fairness continua otimizando justiça; Resilience apenas mede tolerância operacional.
Self-Healing (fora deste milestone) seria escolher e aplicar reparos.

## Incidentes independentes

- **Atraso:** estende apenas o término de cada alvo em 15 e 30 minutos. Exige
  cobertura integral por uma janela de cada recurso/equipe, término dentro do
  horizonte e ausência de colisão com qualquer equipe, área ou árbitro envolvidos.
- **Área indisponível:** procura outra área ativa do campeonato, livre e disponível
  durante todo o intervalo original, sem mudar horário nem aplicar a alternativa.
- **Árbitro indisponível:** mesma consulta para outro árbitro ativo. Só se aplica
  a partidas que possuem árbitro, inclusive reservas fixas.

Intervalos são semiabertos: terminar exatamente quando outro jogo inicia é seguro.
As simulações são independentes; não somam incidentes nem propagam atrasos.

## Escopo e fórmula

Alvos: partidas novas e fixas do campeonato não finalizadas, inteiramente contidas
no horizonte. C# marca estas últimas com `countsForResilience` no DTO. Não se usa
relógio do servidor: relevância operacional depende do status e horizonte escolhido.
Todas as ocupações recebidas continuam bloqueando recursos, inclusive jogos
finalizados e externos; estes não aumentam o denominador de incidentes.

Cada componente = `round(100 × cenários seguros / cenários avaliados)`.
Delay tem dois cenários por partida; área um; árbitro um quando atribuído.
Score geral = média aritmética dos componentes aplicáveis, já arredondados.
Arredondamento inteiro de meio para cima. Todos os scores estão entre 0 e 100.
`refereeScore` é null quando não há árbitros. Sem alvos, `resilience` é null;
sem solução válida também é null. Não se inventa uma nota para agenda vazia.

O objeto contém `score`, `delayScore`, `areaScore`, `refereeScore`,
`scenariosEvaluated`, `vulnerableScenarios` e `observations`. Quatro observações
agregadas resumem atrasos, alternativas de área e substitutos de árbitro.
C# valida estrutura, presença, faixas e aplicabilidade, sem repetir os cálculos.
A análise não é persistida nem incluída no token de aplicação.

## Limitações

O score não é probabilidade de falha nem previsão. Só analisa os três incidentes
isolados, sem mobilidade, deslocamento ou custos. Fallbacks não são reservados e
podem ser a mesma alternativa para incidentes diferentes. Recursos ausentes do
contrato ativo não oferecem cobertura/fallback. Disponibilidades em janelas
separadas não são fundidas. A agenda gerada pode variar sob o limite de tempo do
solver; a análise da mesma agenda é determinística. Comparações percorrem as
ocupações do contrato e preservam seus limites existentes de tamanho.
