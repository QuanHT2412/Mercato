namespace Mercato.Identity.Application.Exceptions;

// Thrown for every refresh-token failure reason (not found, revoked, rotated, idle
// expired, family expired, inactive user) — deliberately uniform, mirrors
// InvalidCredentialsException's "don't leak which case it was" posture.
public sealed class InvalidRefreshTokenException : Exception
{
    public InvalidRefreshTokenException() : base("Invalid or expired refresh token.")
    {
    }
}
