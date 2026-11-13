using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Infrastructure;
using Projeto_Campeonato.Api.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Projeto_Campeonato.Api.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<TournamentDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Tournaments")));
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Brakvyo.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.HeaderName = "X-XSRF-TOKEN";
});
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedEmail = false;
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<TournamentDbContext>().AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "Brakvyo.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<IAccessControl, AccessControl>();
builder.Services.AddSingleton<IAccessInvitationTokens, AccessInvitationTokens>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDataProtection().SetApplicationName("projeto-torneio")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["ProposalKeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, ".keys")));
builder.Services.AddHttpClient<ISchedulerClient, SchedulerClient>(http =>
{
    http.BaseAddress = new Uri((builder.Configuration["Scheduler:BaseUrl"] ?? "http://localhost:8000").TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(Math.Clamp(builder.Configuration.GetValue("Scheduler:TimeoutSeconds", 10), 1, 60));
    http.MaxResponseContentBufferSize = 1024 * 1024;
});

var app = builder.Build();

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TournamentDbContext>().Database.MigrateAsync();
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapControllers();
app.UseSwagger();
app.UseSwaggerUI();

await AuthBootstrap.Seed(app.Services, builder.Configuration);

var campeonatos = new List<Campeonato>();

app.MapGet("/campeonatos", () => campeonatos);

app.MapPost("/campeonatos", async (Campeonato campeonato, HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    campeonato.Id = campeonatos.Count + 1;
    campeonatos.Add(campeonato);

    return Results.Created($"/campeonatos/{campeonato.Id}", campeonato);
}).RequireAuthorization();

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
