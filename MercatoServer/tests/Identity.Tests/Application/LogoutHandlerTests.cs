using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Tokens;
using Mercato.Identity.Domain.Tokens;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Identity.Tests.Application;

public class LogoutHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IRefreshTokenGenerator _refreshTokenGenerator = Substitute.For<IRefreshTokenGenerator>();
    private readonly LogoutHandler _handler;

    public LogoutHandlerTests()
    {
        _handler = new LogoutHandler(_refreshTokenRepository, _refreshTokenGenerator);
    }

    [Fact]
    public async Task HandleAsync_WithKnownToken_RevokesTheFamily()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = RefreshToken.StartNewFamily(Guid.CreateVersion7(), "hashed-token", null, null, timeProvider);
        _refreshTokenGenerator.Hash("raw-token").Returns("hashed-token");
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns(token);

        await _handler.HandleAsync(new LogoutCommand("raw-token"), CancellationToken.None);

        await _refreshTokenRepository.Received(1).RevokeFamilyAsync(token.FamilyId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownToken_DoesNotThrow_AndDoesNotRevokeAnything()
    {
        _refreshTokenGenerator.Hash("raw-token").Returns("hashed-token");
        _refreshTokenRepository.GetByTokenHashAsync("hashed-token", Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        await _handler.HandleAsync(new LogoutCommand("raw-token"), CancellationToken.None);

        await _refreshTokenRepository.DidNotReceive().RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
