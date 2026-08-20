using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Mercato.Identity.Application.Users;

public sealed record LoginUserCommand(string Email, string Password);

public sealed record LoginUserResult(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class LoginUserHandler(
    IUserRepository userRepository,
    IPasswordHasher<User> passwordHasher,
    IAccessTokenGenerator accessTokenGenerator)
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

        var token = accessTokenGenerator.Generate(user);

        return new LoginUserResult(token.Value, token.ExpiresAt);
    }
}
