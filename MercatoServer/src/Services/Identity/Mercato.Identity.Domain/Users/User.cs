using Mercato.Identity.Domain.Exceptions;

namespace Mercato.Identity.Domain.Users;

public sealed class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; }
    public string Username { get; private set; }
    public string PasswordHash { get; private set; }
    public string Name { get; private set; }
    public string? AvatarUrl { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core materialization only — real instances are created via Register.
    private User()
    {
        Email = null!;
        Username = null!;
        PasswordHash = null!;
        Name = null!;
    }

    private User(Guid id, string email, string username, string passwordHash, string name, UserRole role)
    {
        Id = id;
        Email = email;
        Username = username;
        PasswordHash = passwordHash;
        Name = name;
        Role = role;
        IsActive = true;
        IsVerified = false;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // Public registration always creates a Customer; Admin accounts only come from
    // seeding (see AdminSeeder) — there is no endpoint that lets a caller pick a role.
    public static User Register(string email, string username, string passwordHash, string name) =>
        Create(email, username, passwordHash, name, UserRole.Customer);

    public static User CreateAdmin(string email, string username, string passwordHash, string name) =>
        Create(email, username, passwordHash, name, UserRole.Admin);

    private static User Create(string email, string username, string passwordHash, string name, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainValidationException("Password hash must not be empty.");
        }

        return new User(
            Guid.CreateVersion7(),
            ValidateEmail(email),
            ValidateUsername(username),
            passwordHash,
            ValidateName(name),
            role);
    }

    // Never hard-deleted (ADR 0001): a user is deactivated, historical references
    // to their id (e.g. old orders) remain valid without a live row disappearing.
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainValidationException("Email must not be empty.");
        }

        email = email.Trim();

        if (email.Length > 255)
        {
            throw new DomainValidationException("Email must not exceed 255 characters.");
        }

        if (email.Contains(' ') || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
        {
            throw new DomainValidationException("Email format is invalid.");
        }

        return email;
    }

    private static string ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DomainValidationException("Username must not be empty.");
        }

        username = username.Trim();

        if (username.Length > 50)
        {
            throw new DomainValidationException("Username must not exceed 50 characters.");
        }

        foreach (var c in username)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
            {
                throw new DomainValidationException("Username may only contain letters, digits, '_' or '-'.");
            }
        }

        return username;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("Name must not be empty.");
        }

        name = name.Trim();

        if (name.Length > 100)
        {
            throw new DomainValidationException("Name must not exceed 100 characters.");
        }

        return name;
    }
}
