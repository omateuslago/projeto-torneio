using System.Net.Http.Json;
namespace Projeto_Campeonato.Api.Features;

public interface ISchedulerClient
{
    Task<SolverResult> Generate(SolverInput input, CancellationToken ct);
    Task<SolverResult> Repair(RepairInput input, CancellationToken ct) => throw new NotSupportedException();
    Task<bool> Healthy(CancellationToken ct);
}
public sealed class SchedulerUnavailableException(int status, string message) : Exception(message)
{ public int Status { get; } = status; }
public sealed class SchedulerClient(HttpClient http) : ISchedulerClient
{
    public Task<SolverResult> Generate(SolverInput input, CancellationToken ct) => Send("schedule/generate", input, ct);
    public Task<SolverResult> Repair(RepairInput input, CancellationToken ct) => Send("schedule/repair", input, ct);
    private async Task<SolverResult> Send<T>(string path, T input, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(path, input, ct);
            if (!response.IsSuccessStatusCode) throw new SchedulerUnavailableException(502, "O scheduler rejeitou o contrato. Nenhuma agenda foi alterada.");
            return await response.Content.ReadFromJsonAsync<SolverResult>(ct) ?? throw new System.Text.Json.JsonException();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SchedulerUnavailableException(504, "O scheduler excedeu o tempo de resposta. Tente novamente."); }
        catch (HttpRequestException) { throw new SchedulerUnavailableException(503, "Geração automática indisponível. O agendamento manual continua disponível."); }
        catch (System.Text.Json.JsonException) { throw new SchedulerUnavailableException(502, "Resposta inválida do scheduler. Nenhuma agenda foi alterada."); }
    }
    public async Task<bool> Healthy(CancellationToken ct)
    {
        try { using var response = await http.GetAsync("health", ct); return response.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }
}
