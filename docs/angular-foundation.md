# Decisão 002 — Frontend oficial Angular

## Estado anterior

O Next.js tinha landing page, cabeçalho, botões, badges e uma listagem com busca e
filtro alimentada por dados mockados. Também existiam clientes HTTP para a API
legada. Não havia fluxo de cadastro e inscrição conectado à API persistida.

Foram preservados os 39 arquivos originais em frontend-legacy/, sem alteração de
conteúdo. Mantivemos como referência a navegação, o azul nos botões e a indicação
de estado dos campeonatos. A interface Angular prioriza as operações do organizador,
sem reproduzir a landing page e suas promessas de funcionalidades futuras.

## Estrutura e escolhas

Angular 22 estável, componentes standalone, signals para estado local e Reactive
Forms tipados. Rotas carregam as features de campeonatos e equipes sob demanda.
A feature registrations é usada dentro dos detalhes de campeonato.

`core/models.ts` corresponde aos DTOs do backend; `core/tournament-api.ts`
centraliza HttpClient e `core/api-error.ts` apresenta ProblemDetails ao usuário.
A URL base fica em environment.ts e o proxy local resolve /api para a porta 5094.
Não houve necessidade de alterar CORS. Não há store global, repositories,
framework visual ou abstração genérica de CRUD.

A UI usa branco (#ffffff), azul (#0369a1), texto (#243449), fundo (#f3f6fa) e
bordas (#ced9e5), com Segoe UI/system-ui. Lista e formulário ficam lado a lado em
telas largas e empilhados em telas pequenas. Labels, foco de teclado, mensagens
com role alert/status e link para pular a navegação facilitam o uso acessível.

## Limites e verificação

As regras de domínio continuam na API. A interface informa que fechar inscrições
é irreversível nesta etapa. Os nomes dos estados são traduzidos apenas na UI.
Não foram adicionadas operações, modelos ou regras de partidas.

Build Angular e 10 testes de componente/HTTP validam os casos relevantes. Dois
cenários Playwright exercitam cadastro pela UI, leitura após reload e respostas
reais de duplicidade, lotação e fechamento por outra sessão. O primeiro também
verifica ausência de overflow horizontal em 390 px e gera screenshots.
Os testes de navegador criam registros no banco local, identificados por timestamp.

A suite de 25 testes do backend permaneceu intacta e passou em .NET 10.
O Docker da API permanece com a limitação de rede descrita no documento da migração.
