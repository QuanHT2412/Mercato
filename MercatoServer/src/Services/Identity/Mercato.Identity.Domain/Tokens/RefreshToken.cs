using System.Net;
using Mercato.Identity.Domain.Exceptions;

namespace Mercato.Identity.Domain.Tokens;

public sealed class RefreshToken
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan FamilyLifetime = TimeSpan.FromDays(30);

    // ADR 0002 "Rotation race handling": a rotated token seen again inside this window is
    // a benign race (two tabs sharing one cookie), outside it is treated as theft.
    public static readonly TimeSpan RotationGraceWindow = TimeSpan.FromSeconds(30);

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; }
    public Guid FamilyId { get; private set; }
    public Guid? ReplacedBy { get; private set; }
    public string? DeviceInfo { get; private set; }
    public IPAddress? IpAddress { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset FamilyExpiresAt { get; private set; }

    // EF Core materialization only — real instances are created via StartNewFamily/Rotate.
    private RefreshToken()
    {
        TokenHash = null!;
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset familyExpiresAt,
        string? deviceInfo,
        IPAddress? ipAddress,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainValidationException("Token hash must not be empty.");
        }

        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        DeviceInfo = deviceInfo;
        IpAddress = ipAddress;
        IsRevoked = false;
        CreatedAt = now;
        ExpiresAt = now + IdleLifetime;
        FamilyExpiresAt = familyExpiresAt;
    }

    // Login: starts a brand new rotation family.
    public static RefreshToken StartNewFamily(
        Guid userId,
        string tokenHash,
        string? deviceInfo,
        IPAddress? ipAddress,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();

        return new RefreshToken(
            Guid.CreateVersion7(),
            userId,
            tokenHash,
            Guid.CreateVersion7(),
            now + FamilyLifetime,
            deviceInfo,
            ipAddress,
            now);
    }

    // Rotation: successor stays in the same family and inherits FamilyExpiresAt verbatim
    // (rotation never extends the 30-day absolute cap — ADR 0002 "Lifetimes").
    public RefreshToken Rotate(string newTokenHash, string? deviceInfo, IPAddress? ipAddress, TimeProvider timeProvider)
    {
        if (IsRotated)
        {
            throw new DomainValidationException("A rotated refresh token cannot be rotated again.");
        }

        var now = timeProvider.GetUtcNow();

        var successor = new RefreshToken(
            Guid.CreateVersion7(),
            UserId,
            newTokenHash,
            FamilyId,
            FamilyExpiresAt,
            deviceInfo,
            ipAddress,
            now);

        ReplacedBy = successor.Id;
        RotatedAt = now;

        return successor;
    }

    public void Revoke() => IsRevoked = true;

    public bool IsRotated => ReplacedBy is not null;

    public bool IsWithinRotationGrace(TimeProvider timeProvider) =>
        RotatedAt is { } rotatedAt && timeProvider.GetUtcNow() < rotatedAt + RotationGraceWindow;

    public bool IsExpired(TimeProvider timeProvider) => timeProvider.GetUtcNow() >= ExpiresAt;

    public bool IsFamilyExpired(TimeProvider timeProvider) => timeProvider.GetUtcNow() >= FamilyExpiresAt;
}
