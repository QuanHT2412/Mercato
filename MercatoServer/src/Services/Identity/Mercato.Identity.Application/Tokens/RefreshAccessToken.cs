using System.Net;
using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;

namespace Mercato.Identity.Application.Tokens;

public sealed record RefreshAccessTokenCommand(string RawRefreshToken, string? DeviceInfo, IPAddress? IpAddress);

public sealed record RefreshAccessTokenResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class RefreshAccessTokenHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IUserRepository userRepository,
    IRefreshTokenGenerator refreshTokenGenerator,
    IAccessTokenGenerator accessTokenGenerator,
    TimeProvider timeProvider)
{
    public async Task<RefreshAccessTokenResult> HandleAsync(RefreshAccessTokenCommand command, CancellationToken cancellationToken)
    {
        var presentedHash = refreshTokenGenerator.Hash(command.RawRefreshToken);
        var token = await refreshTokenRepository.GetByTokenHashAsync(presentedHash, cancellationToken);

        // Every failure reason below collapses to the same exception on purpose (mirrors
        // login's "don't leak which case it was"). IsRotated is a flat failure here —
        // slice 3 replaces this branch with the grace-window/reuse-detection logic.
        if (token is null
            || token.IsRevoked
            || token.IsRotated
            || token.IsExpired(timeProvider)
            || token.IsFamilyExpired(timeProvider))
        {
            throw new InvalidRefreshTokenException();
        }

        var user = await userRepository.GetByIdAsync(token.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new InvalidRefreshTokenException();
        }

        var generatedRefreshToken = refreshTokenGenerator.Generate();
        var successor = token.Rotate(generatedRefreshToken.TokenHash, command.DeviceInfo, command.IpAddress, timeProvider);
        refreshTokenRepository.Add(successor);

        var accessToken = accessTokenGenerator.Generate(user);

        // One SaveChangesAsync call — EF Core wraps the old token's update
        // (ReplacedBy/RotatedAt) and the new token's insert in a single implicit
        // transaction, so rotation is atomic without an explicit BeginTransaction.
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        return new RefreshAccessTokenResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            generatedRefreshToken.RawToken,
            successor.ExpiresAt);
    }
}
