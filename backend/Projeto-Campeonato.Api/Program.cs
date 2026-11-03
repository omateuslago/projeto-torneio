using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<TournamentDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Tournaments")));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TournamentDbContext>().Database.MigrateAsync();
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();
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
    public string Jogo { get; set; } = "";
    public int Equipes { get; set; }
    public string Status { get; set; } = "";
    public string Modalidade { get; set; } = "";
}

public partial class Program { }
