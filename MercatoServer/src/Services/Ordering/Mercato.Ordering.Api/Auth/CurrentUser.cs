using System.Security.Claims;

namespace Mercato.Ordering.Api.Auth;

internal static class CurrentUser
{
    // Every cart and order is scoped to this id. It is read from the validated access
    // token's `sub` claim and from nowhere else — a client cannot ask for another user's
    // cart by putting an id in the URL or the body, because no endpoint accepts one.
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtValidationExtensions.SubjectClaim), out userId) && userId != Guid.Empty;

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId(out var userId)
            ? userId
            : throw new InvalidOperationException("No user id on the principal; RequireUserId must run first.");

    // Group-level guard: a token that validates but carries no usable subject is treated
    // as unauthenticated before any handler runs.
    public static RouteGroupBuilder RequireUserId(this RouteGroupBuilder group)
    {
        group.RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
            context.HttpContext.User.TryGetUserId(out _) ? await next(context) : Results.Unauthorized());

        return group;
    }
}
