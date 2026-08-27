using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Domain.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Mercato.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository(IdentityDbContext dbContext) : IRefreshTokenRepository
{
    public void Add(RefreshToken token) => dbContext.RefreshTokens.Add(token);

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && !t.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.IsRevoked, true), cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RefreshTokenRotationConflictException();
        }
    }
}
