namespace Mercato.Identity.Application.Abstractions;

public sealed record GeneratedRefreshToken(string RawToken, string TokenHash);

public interface IRefreshTokenGenerator
{
    GeneratedRefreshToken Generate();

    string Hash(string rawToken);
}
