using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Mercato.Identity.Application.Users;

public sealed record RegisterUserCommand(string Email, string Username, string Password, string Name);

public sealed record RegisterUserResult(Guid Id, string Email, string Username, string Name);

public sealed class RegisterUserHandler(IUserRepository userRepository, IPasswordHasher<User> passwordHasher)
{
    public async Task<RegisterUserResult> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (await userRepository.ExistsByEmailAsync(command.Email, cancellationToken))
        {
            throw new EmailAlreadyExistsException(command.Email);
        }

        if (await userRepository.ExistsByUsernameAsync(command.Username, cancellationToken))
        {
            throw new UsernameAlreadyExistsException(command.Username);
        }

        // PasswordHasher<TUser>'s default implementation is user-agnostic for hashing —
        // it never reads the `user` argument — so there is no User yet to pass.
        var passwordHash = passwordHasher.HashPassword(null!, command.Password);
        var user = User.Register(command.Email, command.Username, passwordHash, command.Name);

        userRepository.Add(user);
        await userRepository.SaveChangesAsync(cancellationToken);

        return new RegisterUserResult(user.Id, user.Email, user.Username, user.Name);
    }
}
