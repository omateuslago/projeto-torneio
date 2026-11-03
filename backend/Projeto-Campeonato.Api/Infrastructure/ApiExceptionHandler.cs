using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Projeto_Campeonato.Api.Domain;

namespace Projeto_Campeonato.Api.Infrastructure;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var postgres = exception.GetBaseException() as PostgresException;
        var (status, detail) = exception switch
        {
            ArgumentException => (400, exception.Message),
            DomainConflictException => (409, exception.Message),
            _ when postgres?.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
                => (409, "Registration or tournament changed concurrently. Reload and retry."),
            _ => (500, "An unexpected error occurred.")
        };
        if (status == 500) logger.LogError(exception, "Unhandled API error");
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = status == 500 ? "Server error" : "Request rejected", Detail = detail }
        });
    }
}
