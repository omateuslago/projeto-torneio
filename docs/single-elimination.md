# Chave eliminatória simples

O formato `SingleElimination` usa a mesma inscrição, partida e central de resultados
do restante da aplicação. A diferença está na estrutura persistida da chave e na
progressão automática de quem vence.

## Estrutura e geração

`POST /api/tournaments/{id}/matches/generate` exige inscrições encerradas e ao
menos duas equipes. As inscrições são ordenadas por data e `TeamId`; essa ordem é
o seed determinístico usado na distribuição balanceada da chave. Repetir a
operação devolve a estrutura existente, sem novos IDs ou mudanças de posição.

A tabela `BracketNodes` representa todas as fases, inclusive confrontos ainda sem
equipes definidas. Uma partida em `Matches` só existe quando os dois participantes
são conhecidos. Quando a quantidade não é potência de dois, os melhores seeds
recebem folgas distribuídas pela chave e avançam sem placar, partida fictícia ou
notificação de resultado.

`GET /api/tournaments/{id}/bracket` devolve as fases, posições, equipes conhecidas,
partidas e campeão em um contrato próprio de apresentação.

## Progressão, resultados e correções

O endpoint de partidas continua sendo a fonte para submissão bilateral, disputa e
override administrativo. Assim, tanto dois placares concordantes quanto uma
resolução de disputa ou um resultado administrativo passam pelo mesmo
`ResultWorkflow` e avançam o vencedor.

Partidas eliminatórias exigem um vencedor. Um empate retorna conflito sem alterar
a partida. Ao oficializar o resultado, o vencedor ocupa o slot conectado da fase
seguinte; assim que os dois lados ficam conhecidos, a próxima partida é criada.
Apenas o resultado da final encerra o campeonato.

Corrigir o placar sem trocar o vencedor é sempre seguro. Trocar o vencedor atualiza
o confronto seguinte somente enquanto ele estiver pendente, sem agendamento,
submissão ou disputa. Caso já exista atividade, a API retorna conflito e não tenta
um rollback em cascata de várias fases.

## Interface

A tela apresenta uma coluna por fase, cartões compactos e linhas de conexão. Slots
futuros aparecem como **A definir**, folgas como **Avançou direto** e o campeão
recebe destaque após a final. Em telas pequenas, somente a área da chave tem
rolagem horizontal. A classificação por pontos e as ferramentas automáticas de
agenda não são exibidas para o mata-mata.

## Limitações atuais

Não há disputa de terceiro lugar, chave manual, drag-and-drop de seeds, melhor de
N, pênaltis detalhados, grupos, double elimination ou losers bracket. Uma troca de
vencedor também não desfaz automaticamente resultados de várias fases: quando o
confronto seguinte já tem atividade, a correção precisa ser tratada manualmente.
