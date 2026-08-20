using Microsoft.Extensions.Options;

namespace Mercato.Identity.Api.Auth;

internal static class RefreshTokenCookie
{
    public static void Set(HttpResponse response, string rawToken, DateTimeOffset expiresAt, IOptions<AuthCookieOptions> options)
    {
        var cookieOptions = BaseCookieOptions(options.Value);

        // Max-Age tracks the token's own idle expiry (7 days), NOT the token family's
        // absolute cap (family_expires_at) — the family can die sooner. A cookie that
        // outlives its family is harmless: the server rejects the token on refresh and
        // that 401 response clears the cookie. Do not "fix" this to track
        // family_expires_at — it would need threading a second expiry through every
        // call site for no behavioural difference.
        cookieOptions.MaxAge = expiresAt - DateTimeOffset.UtcNow;

        response.Cookies.Append(AuthCookieOptions.RefreshTokenCookieName, rawToken, cookieOptions);
    }

    public static void Clear(HttpResponse response, IOptions<AuthCookieOptions> options) =>
        response.Cookies.Delete(AuthCookieOptions.RefreshTokenCookieName, BaseCookieOptions(options.Value));

    private static CookieOptions BaseCookieOptions(AuthCookieOptions options) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = options.RefreshCookiePath,
    };
}
