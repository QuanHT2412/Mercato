using System.Net;
using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;

namespace Mercato.Identity.Application.Tokens;

public sealed record RefreshAccessTokenCommand(string RawRefreshToken, string? DeviceInfo, IPAddress? IpAddress);

// RefreshToken is null when the caller hit the rotation grace window: they get a fresh
// access token, but the cookie already holds the successor issued to the request that
// won the race, so there is nothing new to set.
public sealed record RefreshAccessTokenResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAt);

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

        // Every failure reason collapses to the same exception on purpose (mirrors
        // login's "don't leak which case it was").
        if (token is null
            || token.IsRevoked
            || token.IsExpired(timeProvider)
            || token.IsFamilyExpired(timeProvider))
        {
            throw new InvalidRefreshTokenException();
        }

        if (token.IsRotated && !token.IsWithinRotationGrace(timeProvider))
        {
            // Reuse detection (RFC 9700): an already-rotated token showing up again after
            // the grace window means it leaked — whoever holds the newest token in this
            // family can no longer be trusted either, so the whole family dies.
            await refreshTokenRepository.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            throw new InvalidRefreshTokenException();
        }

        var user = await userRepository.GetByIdAsync(token.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new InvalidRefreshTokenException();
        }

        var accessToken = accessTokenGenerator.Generate(user);

        if (token.IsRotated)
        {
            return GraceResult(accessToken);
        }

        var generatedRefreshToken = refreshTokenGenerator.Generate();
        var successor = token.Rotate(generatedRefreshToken.TokenHash, command.DeviceInfo, command.IpAddress, timeProvider);
        refreshTokenRepository.Add(successor);

        try
        {
            // One SaveChangesAsync call — EF Core wraps the old token's update
            // (ReplacedBy/RotatedAt) and the new token's insert in a single implicit
            // transaction, so rotation is atomic without an explicit BeginTransaction.
            await refreshTokenRepository.SaveChangesAsync(cancellationToken);
        }
        catch (RefreshTokenRotationConflictException)
        {
            // Lost a concurrent rotation of the same token: nothing of ours was saved
            // and the winner's successor is already in the cookie jar — same situation
            // as the grace window, answered the same way.
            return GraceResult(accessToken);
        }

        return new RefreshAccessTokenResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            generatedRefreshToken.RawToken,
            successor.ExpiresAt);
    }

    private static RefreshAccessTokenResult GraceResult(AccessToken accessToken) =>
        new(accessToken.Value, accessToken.ExpiresAt, RefreshToken: null, RefreshTokenExpiresAt: null);
}
