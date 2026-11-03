# Recuperação do milestone 1

Base inspecionada: `origin/main` em `bca4656`, em 3 de novembro de 2026.
O fetch confirmou que a única branch remota era main, nesse commit. Os commits
locais do ambiente anterior não estavam disponíveis; esta é uma reconstrução
funcional do escopo informado, e não uma restauração dos mesmos hashes ou arquivos.

| Área | Encontrado na base | Recuperação |
| --- | --- | --- |
| Backend | .NET 9, Minimal API, Swagger | Mantidos; Controllers adicionados |
| Campeonatos legados | GET/POST em memória | Preservados sem mudar o contrato |
| Frontend | Next.js/React com páginas de campeonatos | Preservado |
| Tournament | Ausente | Criação, consulta e estado de inscrições |
| Team | Ausente | Cadastro, consulta e listagem |
| Registration | Ausente | Inscrição, participantes, capacidade e unicidade |
| Persistência | Ausente | EF Core, PostgreSQL, migration e snapshot |
| Execução local | Sem Compose | PostgreSQL, migration e API via Compose |
| Testes | Nenhum projeto | Domínio e integração real, incluindo concorrência |
| Documentação | README sem instruções | Setup, contratos, testes e decisões |

O relato anterior mencionava 18 testes. Como os arquivos se perderam, não é
possível afirmar quais eram; a nova suíte cobre o comportamento reconstruído.
O restante da visão de produto permanece fora deste milestone.

## Validação executada

- SDK .NET 9.0.320; compilação da solução sem warnings ou erros na execução final.
- 25 testes aprovados, zero falhas e zero ignorados, incluindo integração com PostgreSQL 17.
- Migration inicial aplicada em bancos isolados de teste e pelo comando `--migrate`.
- `docker compose config --quiet` aprovado; serviço PostgreSQL iniciado pelo Compose.
- Build completo da imagem da API tentado, mas bloqueado por HTTP 403 ao baixar
  a imagem .NET no domínio `westcentralus.data.mcr.microsoft.com`, ausente da
  lista de rede permitida. O Dockerfile não foi validado até o fim neste ambiente.
- Removida a referência não utilizada a Microsoft.AspNetCore.OpenApi 9.0.3,
  incompatível com a versão de Microsoft.OpenApi exigida pelo Swashbuckle já
  existente. A documentação Swagger foi preservada e validada por teste HTTP.
- Versões transitivas do EF Core alinhadas; exceções aninhadas de serialização
  PostgreSQL passam a retornar 409, verificadas pelos testes concorrentes.

Não houve alteração no frontend nem merge em main.
