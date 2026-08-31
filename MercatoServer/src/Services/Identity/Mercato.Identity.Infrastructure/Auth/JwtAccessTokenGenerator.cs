using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mercato.Identity.Infrastructure.Auth;

// ES256 (ECDSA P-256): asymmetric so that services which only ever verify tokens
// (dual validation per ADR 0002) never hold a key capable of minting one — only
// Identity holds the private key. See ADR 0002.
internal sealed class JwtAccessTokenGenerator : IAccessTokenGenerator, IDisposable
{
    private readonly ECDsa _ecdsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtOptions _options;

    public JwtAccessTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(_options.PrivateKey);

        var key = new ECDsaSecurityKey(_ecdsa) { KeyId = "identity-signing-key" };
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
    }

    public AccessToken Generate(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Name),
            // Short claim name on purpose ("role", not the long ClaimTypes.Role URI): the
            // validators set RoleClaimType = "role" (see ServiceDefaults), and the token
            // rides on every request so it should stay small.
            new Claim("role", user.Role.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessToken(value, expiresAt);
    }

    public void Dispose() => _ecdsa.Dispose();
}
