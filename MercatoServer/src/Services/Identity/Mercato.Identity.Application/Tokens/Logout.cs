using Mercato.Identity.Application.Abstractions;

namespace Mercato.Identity.Application.Tokens;

public sealed record LogoutCommand(string RawRefreshToken);

// Deliberately has no failure path: an unknown or already-revoked token means the
// caller is already logged out, which is the outcome logout wants anyway. The Api
// layer only calls this when a cookie is present at all — see AuthEndpoints.
public sealed class LogoutHandler(IRefreshTokenRepository refreshTokenRepository, IRefreshTokenGenerator refreshTokenGenerator)
{
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var hash = refreshTokenGenerator.Hash(command.RawRefreshToken);
        var token = await refreshTokenRepository.GetByTokenHashAsync(hash, cancellationToken);

        if (token is not null)
        {
            await refreshTokenRepository.RevokeFamilyAsync(token.FamilyId, cancellationToken);
        }
    }
}
