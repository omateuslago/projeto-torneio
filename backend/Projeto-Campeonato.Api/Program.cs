var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

var campeonatos = new List<Campeonato>();

app.MapGet("/campeonatos", () => campeonatos);

app.MapPost("/campeonatos", (Campeonato campeonato) =>
{
    campeonato.Id = campeonatos.Count + 1;
    campeonatos.Add(campeonato);

    return Results.Created($"/campeonatos/{campeonato.Id}", campeonato);
});

app.Run();

public class Campeonato
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Modalidade { get; set; } = "";
}