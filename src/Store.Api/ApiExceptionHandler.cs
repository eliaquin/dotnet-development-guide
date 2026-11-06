using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Store.Application;
using Store.Domain;

namespace Store.Api;

public sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            DomainRuleException => (400, "Datos inválidos", exception.Message),
            MissingRecordException => (404, "Recurso no encontrado", exception.Message),
            ConflictException => (409, "Conflicto", exception.Message),
            _ => (500, "Error interno", "No se pudo completar la operación.")
        };
        if (status >= 500)
        {
            LogUnexpectedError(logger, exception, httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }

    [LoggerMessage(1, LogLevel.Error, "Error inesperado al procesar la solicitud {TraceId}")]
    private static partial void LogUnexpectedError(ILogger logger, Exception exception, string traceId);
}
