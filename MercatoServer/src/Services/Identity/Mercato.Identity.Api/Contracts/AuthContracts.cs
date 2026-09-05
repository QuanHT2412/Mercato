namespace Mercato.Identity.Api.Contracts;

public sealed record RegisterRequest(string Email, string Username, string Password, string Name);

public sealed record RegisterResponse(Guid Id, string Email, string Username, string Name);

public sealed record LoginRequest(string Email, string Password);

public sealed record CurrentUserResponse(Guid Id, string Email, string Username, string Name, string Role);

// Shape returned by both /login and /refresh — the refresh token itself never appears
// here, only in the Set-Cookie header (ADR 0002 Decision 3).
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
