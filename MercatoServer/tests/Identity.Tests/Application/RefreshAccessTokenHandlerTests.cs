using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Application.Tokens;
using Mercato.Identity.Domain.Tokens;
using Mercato.Identity.Domain.Users;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Identity.Tests.Application;

public class RefreshAccessTokenHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenGenerator _refreshTokenGenerator = Substitute.For<IRefreshTokenGenerator>();
    private readonly IAccessTokenGenerator _accessTokenGenerator = Substitute.For<IAccessTokenGenerator>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly RefreshAccessTokenHandler _handler;

    public RefreshAccessTokenHandlerTests()
    {
        _refreshTokenGenerator.Hash("raw-token").Returns("hashed-token");
        _handler = new RefreshAccessTokenHandler(
            _refreshTokenRepository, _userRepository, _refreshTokenGenerator, _accessTokenGenerator, _timeProvider);
    }

    private User CreateActiveUser()
    {
        var user = User.Register("customer@example.com", "customer1", "hash", "Nguyen Van A");
        return user;
    }

    private RefreshToken CreateFreshToken(Guid userId) =>
        RefreshToken.StartNewFamily(userId, "hashed-token", null, null, _timeProvider);

    [Fact]
    public async Task HandleAsync_WithUnknownToken_Throws()
    {
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithRevokedToken_Throws()
    {
        var user = CreateActiveUser();
        var token = CreateFreshToken(user.Id);
        token.Revoke();
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithAlreadyRotatedToken_Throws()
    {
        var user = CreateActiveUser();
        var token = CreateFreshToken(user.Id);
        token.Rotate("hashed-successor", null, null, _timeProvider);
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithIdleExpiredToken_Throws()
    {
        var user = CreateActiveUser();
        var token = CreateFreshToken(user.Id);
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);
        _timeProvider.SetUtcNow(token.ExpiresAt);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithFamilyExpiredToken_Throws()
    {
        var user = CreateActiveUser();
        var token = CreateFreshToken(user.Id);
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);
        _timeProvider.SetUtcNow(token.FamilyExpiresAt);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WithDeactivatedUser_Throws()
    {
        var user = CreateActiveUser();
        user.Deactivate();
        var token = CreateFreshToken(user.Id);
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<InvalidRefreshTokenException>(() =>
            _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_HappyPath_RotatesAndReturnsNewTokenPair()
    {
        var user = CreateActiveUser();
        var token = CreateFreshToken(user.Id);
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _refreshTokenGenerator.Generate().Returns(new GeneratedRefreshToken("raw-successor", "hashed-successor"));
        var accessExpiresAt = _timeProvider.GetUtcNow().AddMinutes(10);
        _accessTokenGenerator.Generate(user).Returns(new AccessToken("new-access-token", accessExpiresAt));

        var result = await _handler.HandleAsync(new RefreshAccessTokenCommand("raw-token", "agent", null), CancellationToken.None);

        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal(accessExpiresAt, result.AccessTokenExpiresAt);
        Assert.Equal("raw-successor", result.RefreshToken);
        Assert.True(token.IsRotated);
        _refreshTokenRepository.Received(1).Add(Arg.Is<RefreshToken>(t => t.TokenHash == "hashed-successor" && t.FamilyId == token.FamilyId));
        await _refreshTokenRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
