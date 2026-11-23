using Api.Services;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.Infrastructure;

/// <summary>Answers a <see cref="DomainException"/> with its status and the rule's message.
/// Anything else is left to the default handler, which answers 500 without the exception's
/// detail: a framework or startup exception's message is not written for the caller.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The framework types are mapped only until every domain rule throws a
        // DomainException (docs/specs/0003); then they fall through like anything else.
        var (status, title) = exception switch
        {
            DomainException domain => (domain.StatusCode, domain.Title),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Conflict"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (0, "")
        };

        if (status == 0) return false;

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
            }
        });
    }
}
