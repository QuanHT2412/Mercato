namespace Mercato.Identity.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    // PEM-encoded EC (P-256) private key. Never hardcoded — resolved from Aspire
    // User Secrets (Parameters:jwt-private-key) via AppHost, Identity.Api only.
    public string PrivateKey { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } = 10;
}
