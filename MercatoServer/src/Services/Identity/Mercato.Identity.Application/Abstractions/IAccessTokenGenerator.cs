using Mercato.Identity.Domain.Users;

namespace Mercato.Identity.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface IAccessTokenGenerator
{
    AccessToken Generate(User user);
}
