namespace Projeto_Campeonato.Api.Domain;

public sealed class Team
{
    private Team() { }
    public Team(string name) => Name = DomainRules.Name(name, 120, nameof(name));
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
}
