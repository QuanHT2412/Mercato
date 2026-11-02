using System.Security.Claims;
using Mercato.Ordering.Api.Auth;
using Mercato.Ordering.Api.Contracts;
using Mercato.Ordering.Application.Orders;

namespace Mercato.Ordering.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders").RequireUserId();

        group.MapPost("/checkout", CheckoutAsync)
            .WithName("Checkout")
            .WithSummary("Turn the cart into an order: reserve stock, snapshot prices, start the 15-minute payment window")
            .Produces<OrderResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", ListAsync)
            .WithName("ListOrders")
            .WithSummary("The caller's orders, newest first")
            .Produces<IReadOnlyList<OrderSummary>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetOrder")
            .WithSummary("One of the caller's orders, with its items and current status")
            .Produces<OrderResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/pay", PayAsync)
            .WithName("PayOrder")
            .WithSummary("Pay an order that is awaiting payment (mock provider)")
            .Produces<OrderResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelOrder")
            .WithSummary("Cancel an unpaid order and release its reserved stock")
            .Produces<OrderResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequest request,
        ClaimsPrincipal user,
        CheckoutHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CheckoutCommand(user.GetUserId(), request.Receiver, request.Phone, request.Address, request.Note);
        var order = await handler.HandleAsync(command, cancellationToken);

        return Results.Created($"/orders/{order.Id}", order);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ListOrdersHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(user.GetUserId(), cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        GetOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(user.GetUserId(), id, cancellationToken);

        return order is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: $"Order '{id}' was not found.")
            : Results.Ok(order);
    }

    private static async Task<IResult> PayAsync(
        Guid id,
        PayOrderRequest? request,
        ClaimsPrincipal user,
        PayOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new PayOrderCommand(user.GetUserId(), id, request?.PaymentToken), cancellationToken);

        // A declined payment is not a server error and not a bad request: the order is
        // fine and still payable. 402 says exactly that.
        return result.Paid
            ? Results.Ok(result.Order)
            : Results.Problem(statusCode: StatusCodes.Status402PaymentRequired, title: "Payment declined", detail: result.FailureReason);
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        CancelOrderHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(user.GetUserId(), id, cancellationToken));
}
