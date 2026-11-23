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
        if (exception is not DomainException domain) return false;

        httpContext.Response.StatusCode = domain.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = domain.StatusCode,
                Title = domain.Title,
                Detail = domain.Message,
            }
        });
    }
}
