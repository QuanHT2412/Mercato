using System.Net;
using Mercato.Identity.Api.Auth;
using Mercato.Identity.Api.Contracts;
using Mercato.Identity.Api.Mapping;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Application.Tokens;
using Mercato.Identity.Application.Users;
using Microsoft.Extensions.Options;

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
            .WithSummary("Log in; returns an access token and sets the refresh cookie")
            .Produces<AccessTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .WithTags("Auth")
            .WithSummary("Exchange the refresh cookie for a new access token, rotating it")
            .Produces<AccessTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithTags("Auth")
            .WithSummary("Revoke the current refresh token family and clear the cookie")
            .Produces(StatusCodes.Status204NoContent);

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
        HttpContext httpContext,
        LoginUserHandler handler,
        IOptions<AuthCookieOptions> cookieOptions,
        CancellationToken cancellationToken)
    {
        var command = new LoginUserCommand(request.Email, request.Password, GetDeviceInfo(httpContext), GetIpAddress(httpContext));
        var result = await handler.HandleAsync(command, cancellationToken);

        RefreshTokenCookie.Set(httpContext.Response, result.RefreshToken, result.RefreshTokenExpiresAt, cookieOptions);

        return Results.Ok(result.ToResponse());
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        RefreshAccessTokenHandler handler,
        IOptions<AuthCookieOptions> cookieOptions,
        CancellationToken cancellationToken)
    {
        var rawToken = httpContext.Request.Cookies[AuthCookieOptions.RefreshTokenCookieName];

        try
        {
            if (string.IsNullOrEmpty(rawToken))
            {
                throw new InvalidRefreshTokenException();
            }

            var command = new RefreshAccessTokenCommand(rawToken, GetDeviceInfo(httpContext), GetIpAddress(httpContext));
            var result = await handler.HandleAsync(command, cancellationToken);

            RefreshTokenCookie.Set(httpContext.Response, result.RefreshToken, result.RefreshTokenExpiresAt, cookieOptions);

            return Results.Ok(result.ToResponse());
        }
        catch (InvalidRefreshTokenException)
        {
            // Every refresh failure clears the cookie, including "there was no cookie at
            // all" — harmless no-op in that case, but keeps this the single place that
            // decides "the client no longer has a usable refresh cookie".
            RefreshTokenCookie.Clear(httpContext.Response, cookieOptions);
            throw;
        }
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        LogoutHandler handler,
        IOptions<AuthCookieOptions> cookieOptions,
        CancellationToken cancellationToken)
    {
        var rawToken = httpContext.Request.Cookies[AuthCookieOptions.RefreshTokenCookieName];

        // Absolutely idempotent: no cookie, an unknown token, and an already-revoked
        // token all mean "already logged out" from the caller's point of view — every
        // branch responds 204 and clears the cookie, none of them is an error.
        if (!string.IsNullOrEmpty(rawToken))
        {
            await handler.HandleAsync(new LogoutCommand(rawToken), cancellationToken);
        }

        RefreshTokenCookie.Clear(httpContext.Response, cookieOptions);

        return Results.NoContent();
    }

    private static string? GetDeviceInfo(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrEmpty(userAgent) ? null : userAgent;
    }

    private static IPAddress? GetIpAddress(HttpContext httpContext) => httpContext.Connection.RemoteIpAddress;
}
