using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Application.Exceptions;
using Mercato.Identity.Application.Users;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace Identity.Tests.Application;

public class RegisterUserHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher<User> _passwordHasher = new PasswordHasher<User>();
    private readonly RegisterUserHandler _handler;

    public RegisterUserHandlerTests()
    {
        _handler = new RegisterUserHandler(_userRepository, _passwordHasher);
    }

    [Fact]
    public async Task HandleAsync_WithNewEmailAndUsername_AddsUserAndSaves()
    {
        _userRepository.ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _userRepository.ExistsByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var command = new RegisterUserCommand("customer@example.com", "customer1", "P@ssw0rd123", "Nguyen Van A");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal("customer@example.com", result.Email);
        Assert.Equal("customer1", result.Username);
        _userRepository.Received(1).Add(Arg.Is<User>(u => u.Email == "customer@example.com"));
        await _userRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithExistingEmail_ThrowsAndDoesNotAdd()
    {
        _userRepository.ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var command = new RegisterUserCommand("customer@example.com", "customer1", "P@ssw0rd123", "Nguyen Van A");

        await Assert.ThrowsAsync<EmailAlreadyExistsException>(() => _handler.HandleAsync(command, CancellationToken.None));

        _userRepository.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task HandleAsync_WithExistingUsername_ThrowsAndDoesNotAdd()
    {
        _userRepository.ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _userRepository.ExistsByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var command = new RegisterUserCommand("customer@example.com", "customer1", "P@ssw0rd123", "Nguyen Van A");

        await Assert.ThrowsAsync<UsernameAlreadyExistsException>(() => _handler.HandleAsync(command, CancellationToken.None));

        _userRepository.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task HandleAsync_StoresAHashNotThePlaintextPassword()
    {
        _userRepository.ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _userRepository.ExistsByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var command = new RegisterUserCommand("customer@example.com", "customer1", "P@ssw0rd123", "Nguyen Van A");

        await _handler.HandleAsync(command, CancellationToken.None);

        _userRepository.Received(1).Add(Arg.Is<User>(u => u.PasswordHash != "P@ssw0rd123" && u.PasswordHash.Length > 0));
    }
}
