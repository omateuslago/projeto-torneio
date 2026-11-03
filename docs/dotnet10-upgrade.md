# Migração conservadora para .NET 10 LTS

API e testes passaram de net9.0 para net10.0. EF Core e dotnet-ef usam 10.0.14,
Npgsql EF Core 10.0.3, MVC Testing 10.0.14 e Test SDK 18.11.0. Swashbuckle e xUnit
foram preservados. global.json seleciona um SDK estável da linha 10.0.
As imagens do Dockerfile passaram para sdk:10.0 e aspnet:10.0.

Não foram alteradas regras de negócio, DTOs, migrations ou snapshot. A suíte de
25 testes passou com SDK 10.0.403 e PostgreSQL 17, incluindo Swagger e ausência
de mudanças pendentes no modelo. O comando --migrate reconheceu o banco existente
como atualizado. Build sem warnings e sem erros.

O PostgreSQL via Compose funciona. O build da imagem da API foi tentado e ficou
bloqueado por HTTP 403 no download de westcentralus.data.mcr.microsoft.com, que
não consta na política de rede do ambiente. Portanto a execução completa da API
via Compose ainda precisa de validação em uma rede que permita esse domínio.
