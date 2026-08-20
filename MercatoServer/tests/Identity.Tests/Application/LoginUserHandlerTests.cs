using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Application.Users;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace Identity.Tests.Application;

public class LoginUserHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher<User> _passwordHasher = new PasswordHasher<User>();
    private readonly IAccessTokenGenerator _accessTokenGenerator = Substitute.For<IAccessTokenGenerator>();
    private readonly LoginUserHandler _handler;

    public LoginUserHandlerTests()
    {
        _handler = new LoginUserHandler(_userRepository, _passwordHasher, _accessTokenGenerator);
    }

    private User CreateRegisteredUser(string password)
    {
        var hash = _passwordHasher.HashPassword(null!, password);
        return User.Register("customer@example.com", "customer1", hash, "Nguyen Van A");
    }

    [Fact]
    public async Task HandleAsync_WithCorrectPassword_ReturnsToken()
    {
        var user = CreateRegisteredUser("P@ssw0rd123");
        _userRepository.GetByEmailAsync("customer@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        _accessTokenGenerator.Generate(user).Returns(new AccessToken("token-value", expiresAt));

        var result = await _handler.HandleAsync(
            new LoginUserCommand("customer@example.com", "P@ssw0rd123"),
            CancellationToken.None);

        Assert.Equal("token-value", result.AccessToken);
        Assert.Equal(expiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_WithWrongPassword_ThrowsInvalidCredentials()
    {
        var user = CreateRegisteredUser("P@ssw0rd123");
        _userRepository.GetByEmailAsync("customer@example.com", Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _handler.HandleAsync(new LoginUserCommand("customer@example.com", "wrong-password"), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEmail_ThrowsInvalidCredentials()
    {
        _userRepository.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _handler.HandleAsync(new LoginUserCommand("nobody@example.com", "P@ssw0rd123"), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithDeactivatedUser_ThrowsInvalidCredentials()
    {
        var user = CreateRegisteredUser("P@ssw0rd123");
        user.Deactivate();
        _userRepository.GetByEmailAsync("customer@example.com", Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _handler.HandleAsync(new LoginUserCommand("customer@example.com", "P@ssw0rd123"), CancellationToken.None));
    }
}
