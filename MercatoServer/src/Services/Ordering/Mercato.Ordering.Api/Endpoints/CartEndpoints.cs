using System.Security.Claims;
using Mercato.Ordering.Api.Auth;
using Mercato.Ordering.Api.Contracts;
using Mercato.Ordering.Application.Carts;

namespace Mercato.Ordering.Api.Endpoints;

public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        // "The" cart, not "a" cart: there is no id in these routes, it is always the
        // caller's own.
        var group = app.MapGroup("/cart").WithTags("Cart").RequireUserId();

        group.MapGet("/", GetAsync)
            .WithName("GetCart")
            .WithSummary("The caller's cart (variant ids and quantities)")
            .Produces<CartResult>(StatusCodes.Status200OK);

        group.MapPut("/items/{variantId:guid}", SetItemAsync)
            .WithName("SetCartItem")
            .WithSummary("Put a variant in the cart with the given quantity (1–10), replacing any previous quantity")
            .Produces<CartResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/items/{variantId:guid}", RemoveItemAsync)
            .WithName("RemoveCartItem")
            .WithSummary("Take a variant out of the cart")
            .Produces<CartResult>(StatusCodes.Status200OK);

        group.MapDelete("/", ClearAsync)
            .WithName("ClearCart")
            .WithSummary("Empty the cart")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, GetCartHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(user.GetUserId(), cancellationToken));

    private static async Task<IResult> SetItemAsync(
        Guid variantId,
        SetCartItemRequest request,
        ClaimsPrincipal user,
        SetCartItemHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new SetCartItemCommand(user.GetUserId(), variantId, request.Quantity);

        return Results.Ok(await handler.HandleAsync(command, cancellationToken));
    }

    private static async Task<IResult> RemoveItemAsync(
        Guid variantId,
        ClaimsPrincipal user,
        RemoveCartItemHandler handler,
        CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(user.GetUserId(), variantId, cancellationToken));

    private static async Task<IResult> ClearAsync(ClaimsPrincipal user, ClearCartHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(user.GetUserId(), cancellationToken);

        return Results.NoContent();
    }
}
