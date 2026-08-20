using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Application.Users;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Identity.Tests.Application;

public class LoginUserHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher<User> _passwordHasher = new PasswordHasher<User>();
    private readonly IAccessTokenGenerator _accessTokenGenerator = Substitute.For<IAccessTokenGenerator>();
    private readonly IRefreshTokenGenerator _refreshTokenGenerator = Substitute.For<IRefreshTokenGenerator>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly LoginUserHandler _handler;

    public LoginUserHandlerTests()
    {
        _refreshTokenGenerator.Generate().Returns(new GeneratedRefreshToken("raw-refresh-token", "refresh-token-hash"));

        _handler = new LoginUserHandler(
            _userRepository,
            _refreshTokenRepository,
            _passwordHasher,
            _accessTokenGenerator,
            _refreshTokenGenerator,
            _timeProvider);
    }

    private User CreateRegisteredUser(string password)
    {
        var hash = _passwordHasher.HashPassword(null!, password);
        return User.Register("customer@example.com", "customer1", hash, "Nguyen Van A");
    }

    [Fact]
    public async Task HandleAsync_WithCorrectPassword_ReturnsAccessAndRefreshTokens()
    {
        var user = CreateRegisteredUser("P@ssw0rd123");
        _userRepository.GetByEmailAsync("customer@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        _accessTokenGenerator.Generate(user).Returns(new AccessToken("token-value", expiresAt));

        var result = await _handler.HandleAsync(
            new LoginUserCommand("customer@example.com", "P@ssw0rd123", "test-agent", null),
            CancellationToken.None);

        Assert.Equal("token-value", result.AccessToken);
        Assert.Equal(expiresAt, result.AccessTokenExpiresAt);
        Assert.Equal("raw-refresh-token", result.RefreshToken);
        Assert.Equal(_timeProvider.GetUtcNow() + Mercato.Identity.Domain.Tokens.RefreshToken.IdleLifetime, result.RefreshTokenExpiresAt);
        _refreshTokenRepository.Received(1).Add(Arg.Is<Mercato.Identity.Domain.Tokens.RefreshToken>(t => t.UserId == user.Id));
        await _refreshTokenRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithWrongPassword_ThrowsInvalidCredentials()
    {
        var user = CreateRegisteredUser("P@ssw0rd123");
        _userRepository.GetByEmailAsync("customer@example.com", Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _handler.HandleAsync(
                new LoginUserCommand("customer@example.com", "wrong-password", null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEmail_ThrowsInvalidCredentials()
    {
        _userRepository.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _handler.HandleAsync(
                new LoginUserCommand("nobody@example.com", "P@ssw0rd123", null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithDeactivatedUser_ThrowsInvalidCredentials()
    {
        var user = CreateRegisteredUser("P@ssw0rd123");
        user.Deactivate();
        _userRepository.GetByEmailAsync("customer@example.com", Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            _handler.HandleAsync(
                new LoginUserCommand("customer@example.com", "P@ssw0rd123", null, null),
                CancellationToken.None));
    }
}
