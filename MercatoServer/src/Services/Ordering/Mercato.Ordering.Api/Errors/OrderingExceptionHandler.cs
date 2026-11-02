using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Mercato.Ordering.Api.Errors;

internal sealed class OrderingExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            DomainValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            EmptyCartException => (StatusCodes.Status400BadRequest, "Cart is empty"),
            OrderNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            InvalidOrderTransitionException => (StatusCodes.Status409Conflict, "Order is not in a state that allows this"),
            OutOfStockException => (StatusCodes.Status409Conflict, "Out of stock"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            StockServiceException => (StatusCodes.Status503ServiceUnavailable, "Catalog is unavailable"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            // Not one of ours — let the default ProblemDetails fallback (500) handle it.
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            // An upstream failure's message is for the logs, not for the client.
            Detail = exception is StockServiceException ? "Stock could not be checked right now. Please try again." : exception.Message,
        };

        if (exception is OutOfStockException outOfStock)
        {
            // Machine-readable, so a client can point at the offending cart line.
            problem.Extensions["variantId"] = outOfStock.VariantId;
            problem.Extensions["availableQuantity"] = outOfStock.AvailableQuantity;
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }
}
