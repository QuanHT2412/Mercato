using Mercato.Identity.Api.Contracts;
using Mercato.Identity.Api.Mapping;
using Mercato.Identity.Application.Users;

namespace Mercato.Identity.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithTags("Auth")
            .WithSummary("Register a new customer account")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithTags("Auth")
            .WithSummary("Log in and receive a short-lived access token")
            .Produces<LoginResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        RegisterUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        var response = result.ToResponse();

        return Results.Created($"/register/{response.Id}", response);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginUserHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return Results.Ok(result.ToResponse());
    }
}
