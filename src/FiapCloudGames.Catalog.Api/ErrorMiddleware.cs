using FiapCloudGames.Catalog.Application;
using Microsoft.AspNetCore.Mvc;

namespace FiapCloudGames.Catalog.Api;

public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error)
        {
            var (status, title) = error switch
            {
                PurchaseConflictException => (409, "Jogo já adquirido ou com pedido pendente."),
                NotFoundException => (404, "Recurso não encontrado."),
                ArgumentException => (400, "Dados inválidos."),
                _ => (500, "Erro interno.")
            };
            logger.LogWarning("Requisição recusada: {Status}, tipo {ErrorType}, trace {TraceId}", status, error.GetType().Name, context.TraceIdentifier);
            await Write(context, status, title);
        }
    }
    public static Task Write(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Instance = context.Request.Path };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
