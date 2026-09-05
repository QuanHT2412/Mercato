using Mercato.Identity.Application.Abstractions;

namespace Mercato.Identity.Application.Users;

public sealed record CurrentUserResult(Guid Id, string Email, string Username, string Name, string Role);

// Reads the profile from the database rather than echoing the token's claims: the token
// can be up to 10 minutes stale (name changed, account deactivated), the row cannot.
public sealed class GetCurrentUserHandler(IUserRepository userRepository)
{
    public async Task<CurrentUserResult?> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return null;
        }

        return new CurrentUserResult(user.Id, user.Email, user.Username, user.Name, user.Role.ToString());
    }
}
