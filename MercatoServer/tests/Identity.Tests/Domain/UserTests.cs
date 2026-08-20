using Mercato.Identity.Domain.Exceptions;
using Mercato.Identity.Domain.Users;

namespace Identity.Tests.Domain;

public class UserTests
{
    [Fact]
    public void Register_WithValidData_SetsExpectedDefaults()
    {
        var user = User.Register("customer@example.com", "customer1", "hashed-password", "Nguyen Van A");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("customer@example.com", user.Email);
        Assert.Equal("customer1", user.Username);
        Assert.Equal("Nguyen Van A", user.Name);
        Assert.True(user.IsActive);
        Assert.False(user.IsVerified);
        Assert.Null(user.AvatarUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("customer@")]
    [InlineData("has space@example.com")]
    public void Register_WithInvalidEmail_Throws(string email)
    {
        Assert.Throws<DomainValidationException>(() =>
            User.Register(email, "customer1", "hashed-password", "Nguyen Van A"));
    }

    [Fact]
    public void Register_WithEmailTooLong_Throws()
    {
        var email = new string('a', 250) + "@example.com";

        Assert.Throws<DomainValidationException>(() =>
            User.Register(email, "customer1", "hashed-password", "Nguyen Van A"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("has@symbol")]
    public void Register_WithInvalidUsername_Throws(string username)
    {
        Assert.Throws<DomainValidationException>(() =>
            User.Register("customer@example.com", username, "hashed-password", "Nguyen Van A"));
    }

    [Fact]
    public void Register_WithEmptyName_Throws()
    {
        Assert.Throws<DomainValidationException>(() =>
            User.Register("customer@example.com", "customer1", "hashed-password", " "));
    }

    [Fact]
    public void Register_WithEmptyPasswordHash_Throws()
    {
        Assert.Throws<DomainValidationException>(() =>
            User.Register("customer@example.com", "customer1", "", "Nguyen Van A"));
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var user = User.Register("customer@example.com", "customer1", "hashed-password", "Nguyen Van A");

        user.Deactivate();

        Assert.False(user.IsActive);
    }

    [Fact]
    public void Deactivate_CalledTwice_IsIdempotent()
    {
        var user = User.Register("customer@example.com", "customer1", "hashed-password", "Nguyen Van A");

        user.Deactivate();
        var updatedAtAfterFirst = user.UpdatedAt;
        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.Equal(updatedAtAfterFirst, user.UpdatedAt);
    }
}
