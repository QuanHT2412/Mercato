namespace Mercato.Identity.Api.Contracts;

public sealed record RegisterRequest(string Email, string Username, string Password, string Name);

public sealed record RegisterResponse(Guid Id, string Email, string Username, string Name);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
