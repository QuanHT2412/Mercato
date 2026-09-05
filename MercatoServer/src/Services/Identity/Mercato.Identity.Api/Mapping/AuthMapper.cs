using Mercato.Identity.Api.Contracts;
using Mercato.Identity.Application.Tokens;
using Mercato.Identity.Application.Users;
using Riok.Mapperly.Abstractions;

namespace Mercato.Identity.Api.Mapping;

[Mapper]
public static partial class AuthMapper
{
    public static partial RegisterUserCommand ToCommand(this RegisterRequest request);

    public static partial RegisterResponse ToResponse(this RegisterUserResult result);

    public static partial CurrentUserResponse ToResponse(this CurrentUserResult result);

    // LoginUserCommand/RefreshAccessTokenCommand also need DeviceInfo/IpAddress pulled
    // from HttpContext, not from the request body — endpoints construct those directly
    // rather than through a mapper.

    [MapProperty(nameof(LoginUserResult.AccessTokenExpiresAt), nameof(AccessTokenResponse.ExpiresAt))]
    [MapperIgnoreSource(nameof(LoginUserResult.RefreshToken))]
    [MapperIgnoreSource(nameof(LoginUserResult.RefreshTokenExpiresAt))]
    public static partial AccessTokenResponse ToResponse(this LoginUserResult result);

    [MapProperty(nameof(RefreshAccessTokenResult.AccessTokenExpiresAt), nameof(AccessTokenResponse.ExpiresAt))]
    [MapperIgnoreSource(nameof(RefreshAccessTokenResult.RefreshToken))]
    [MapperIgnoreSource(nameof(RefreshAccessTokenResult.RefreshTokenExpiresAt))]
    public static partial AccessTokenResponse ToResponse(this RefreshAccessTokenResult result);
}
