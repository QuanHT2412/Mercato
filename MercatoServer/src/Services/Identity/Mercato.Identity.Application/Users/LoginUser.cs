using System.Net;
using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Domain.Tokens;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Mercato.Identity.Application.Users;

public sealed record LoginUserCommand(string Email, string Password, string? DeviceInfo, IPAddress? IpAddress);

public sealed record LoginUserResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class LoginUserHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher<User> passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenGenerator refreshTokenGenerator,
    TimeProvider timeProvider)
{
    public async Task<LoginUserResult> HandleAsync(LoginUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // Same failure for "no such user", "wrong password", and "deactivated" —
        // does not leak which case it was.
        if (user is null || !user.IsActive)
        {
            throw new InvalidCredentialsException();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            throw new InvalidCredentialsException();
        }

        var accessToken = accessTokenGenerator.Generate(user);

        var generatedRefreshToken = refreshTokenGenerator.Generate();
        var refreshToken = RefreshToken.StartNewFamily(
            user.Id,
            generatedRefreshToken.TokenHash,
            command.DeviceInfo,
            command.IpAddress,
            timeProvider);

        refreshTokenRepository.Add(refreshToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        return new LoginUserResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            generatedRefreshToken.RawToken,
            refreshToken.ExpiresAt);
    }
}
