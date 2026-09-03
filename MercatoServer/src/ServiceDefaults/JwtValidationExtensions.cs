using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.Hosting;

// Shared access-token validation (ADR 0002 "Validation placement"): the gateway AND every
// service validate the JWT themselves, all from this one place so the rules cannot drift.
// Validators only ever hold the ES256 *public* key — none of them can mint a token.
public static class JwtValidationExtensions
{
    public const string AdminPolicy = "Admin";

    // The claim names Identity writes (see JwtAccessTokenGenerator). MapInboundClaims is
    // off, so these arrive exactly as issued instead of being rewritten to the long
    // ClaimTypes.* URIs.
    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";

    private const string DefaultIssuer = "mercato-identity";
    private const string DefaultAudience = "mercato";

    public static TBuilder AddJwtValidation<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var section = builder.Configuration.GetSection("Jwt");
        var publicKeyPem = section["PublicKey"];

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            // Fail at startup, not on the first authenticated request.
            throw new InvalidOperationException(
                "Jwt:PublicKey is not configured. The AppHost passes it to every service " +
                "from the 'jwt-public-key' parameter (see README, \"Backend secrets\").");
        }

        // Lives for the whole process: the key is captured by the validation parameters.
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicKeyPem);

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = section["Issuer"] ?? DefaultIssuer,
                    ValidAudience = section["Audience"] ?? DefaultAudience,
                    IssuerSigningKey = new ECDsaSecurityKey(ecdsa),
                    // Pin the algorithm: never let the token's own header choose it.
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    NameClaimType = "name",
                    RoleClaimType = RoleClaim,
                    // Access tokens live 10 minutes; the default 5-minute skew would
                    // stretch that by half.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("Admin"));

        return builder;
    }
}
