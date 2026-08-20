namespace Mercato.Identity.Api.Auth;

public sealed class AuthCookieOptions
{
    public const string SectionName = "Auth";

    public const string RefreshTokenCookieName = "refresh_token";

    // Browser-visible path THROUGH THE GATEWAY — not the service's own local route
    // (the gateway strips /api/identity before forwarding). Scoped to the whole
    // /api/identity prefix, not just /refresh: a narrower Path=/api/identity/refresh
    // means the browser never attaches the cookie to /api/identity/logout, so logout
    // could never read it. SameSite=Strict carries the real CSRF protection; this Path
    // scope is defense-in-depth, not the primary control — see ADR 0002 Decision 4.
    public string RefreshCookiePath { get; init; } = "/api/identity";
}
