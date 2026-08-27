using Mercato.Identity.Domain.Tokens;

namespace Mercato.Identity.Application.Abstractions;

public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);

    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    // Set-based: revokes every token in the family, not just one instance — a single
    // RefreshToken has no notion of its siblings, so this can't be a domain method.
    Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    // Throws RefreshTokenRotationConflictException when a tracked token row was changed
    // by someone else since it was read (optimistic concurrency).
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
