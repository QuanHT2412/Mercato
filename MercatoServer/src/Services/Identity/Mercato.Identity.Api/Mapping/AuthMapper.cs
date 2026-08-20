using Mercato.Identity.Api.Contracts;
using Mercato.Identity.Application.Users;
using Riok.Mapperly.Abstractions;

namespace Mercato.Identity.Api.Mapping;

[Mapper]
public static partial class AuthMapper
{
    public static partial RegisterUserCommand ToCommand(this RegisterRequest request);

    public static partial RegisterResponse ToResponse(this RegisterUserResult result);

    public static partial LoginUserCommand ToCommand(this LoginRequest request);

    public static partial LoginResponse ToResponse(this LoginUserResult result);
}
