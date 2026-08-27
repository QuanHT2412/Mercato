using Mercato.Identity.Domain.Exceptions;
using Mercato.Identity.Domain.Tokens;
using Microsoft.Extensions.Time.Testing;

namespace Identity.Tests.Domain;

public class RefreshTokenTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void StartNewFamily_SetsExpectedDefaults()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);

        var token = RefreshToken.StartNewFamily(UserId, "hash-1", "agent", null, timeProvider);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.NotEqual(Guid.Empty, token.FamilyId);
        Assert.Equal(UserId, token.UserId);
        Assert.Equal("hash-1", token.TokenHash);
        Assert.False(token.IsRevoked);
        Assert.Null(token.ReplacedBy);
        Assert.Null(token.RotatedAt);
        Assert.False(token.IsRotated);
        Assert.Equal(now, token.CreatedAt);
        Assert.Equal(now + RefreshToken.IdleLifetime, token.ExpiresAt);
        Assert.Equal(now + RefreshToken.FamilyLifetime, token.FamilyExpiresAt);
    }

    [Fact]
    public void Rotate_KeepsFamilyId_AndCopiesFamilyExpiresAtVerbatim()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var original = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);

        timeProvider.Advance(TimeSpan.FromDays(1));
        var successor = original.Rotate("hash-2", null, null, timeProvider);

        Assert.Equal(original.FamilyId, successor.FamilyId);
        Assert.Equal(original.FamilyExpiresAt, successor.FamilyExpiresAt);
        Assert.Equal("hash-2", successor.TokenHash);
    }

    [Fact]
    public void Rotate_MarksTheOriginalAsReplaced()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var original = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var successor = original.Rotate("hash-2", null, null, timeProvider);

        Assert.Equal(successor.Id, original.ReplacedBy);
        Assert.Equal(timeProvider.GetUtcNow(), original.RotatedAt);
        Assert.True(original.IsRotated);
    }

    [Fact]
    public void Rotate_GivesTheSuccessorAFreshIdleWindow()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var original = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);

        timeProvider.Advance(TimeSpan.FromDays(3));
        var successor = original.Rotate("hash-2", null, null, timeProvider);

        Assert.Equal(timeProvider.GetUtcNow() + RefreshToken.IdleLifetime, successor.ExpiresAt);
    }

    [Fact]
    public void Rotate_CalledASecondTime_Throws()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var original = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);
        original.Rotate("hash-2", null, null, timeProvider);

        Assert.Throws<DomainValidationException>(() => original.Rotate("hash-3", null, null, timeProvider));
    }

    [Fact]
    public void IsExpired_FalseJustBeforeIdleExpiry_TrueAtTheExactInstant()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);

        timeProvider.SetUtcNow(token.ExpiresAt - TimeSpan.FromSeconds(1));
        Assert.False(token.IsExpired(timeProvider));

        timeProvider.SetUtcNow(token.ExpiresAt);
        Assert.True(token.IsExpired(timeProvider));
    }

    [Fact]
    public void IsFamilyExpired_FalseJustBeforeAbsoluteCap_TrueAtTheExactInstant()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);

        timeProvider.SetUtcNow(token.FamilyExpiresAt - TimeSpan.FromSeconds(1));
        Assert.False(token.IsFamilyExpired(timeProvider));

        timeProvider.SetUtcNow(token.FamilyExpiresAt);
        Assert.True(token.IsFamilyExpired(timeProvider));
    }

    [Fact]
    public void IsWithinRotationGrace_FalseBeforeRotation_TrueJustAfter_FalseOnceTheWindowCloses()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);
        Assert.False(token.IsWithinRotationGrace(timeProvider));

        token.Rotate("hash-2", null, null, timeProvider);
        timeProvider.Advance(RefreshToken.RotationGraceWindow - TimeSpan.FromSeconds(1));
        Assert.True(token.IsWithinRotationGrace(timeProvider));

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.False(token.IsWithinRotationGrace(timeProvider));
    }

    [Fact]
    public void Revoke_SetsIsRevoked()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = RefreshToken.StartNewFamily(UserId, "hash-1", null, null, timeProvider);

        token.Revoke();

        Assert.True(token.IsRevoked);
    }
}
