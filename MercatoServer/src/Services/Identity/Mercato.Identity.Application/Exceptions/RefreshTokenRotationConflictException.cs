namespace Mercato.Identity.Application.Exceptions;

// Two requests tried to rotate the same refresh token at the same time and this one lost
// (optimistic concurrency on the token row). Never reaches the client — the refresh
// handler turns it into the grace-window answer.
public sealed class RefreshTokenRotationConflictException : Exception
{
    public RefreshTokenRotationConflictException() : base("The refresh token was rotated concurrently.")
    {
    }
}
