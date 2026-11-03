namespace Projeto_Campeonato.Api.Domain;

public sealed class DomainConflictException(string message) : Exception(message);
