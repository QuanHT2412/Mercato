using Mercato.Catalog.Application.Exceptions;
using Mercato.Catalog.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Mercato.Catalog.Api.Errors;

internal sealed class CatalogExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            DomainValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            CatalogNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            CatalogConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            InsufficientStockException => (StatusCodes.Status409Conflict, "Insufficient stock"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            // Not one of ours — let the default ProblemDetails fallback (500) handle it.
            return false;
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message,
            },
        });
    }
}
