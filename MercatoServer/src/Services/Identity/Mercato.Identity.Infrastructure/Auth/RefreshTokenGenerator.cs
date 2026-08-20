using System.Security.Cryptography;
using System.Text;
using Mercato.Identity.Application.Abstractions;

namespace Mercato.Identity.Infrastructure.Auth;

// Refresh tokens are opaque, server-side-revocable values — deliberately NOT JWTs.
// A JWT is stateless and self-verifying; rotation/family-revocation need a server-side
// row to rotate and revoke, so a JWT refresh token would just be a slower, bigger
// opaque token with no extra benefit. See ADR 0002.
internal sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int TokenSizeBytes = 32; // 256-bit

    public GeneratedRefreshToken Generate()
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenSizeBytes));
        return new GeneratedRefreshToken(raw, Hash(raw));
    }

    public string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
