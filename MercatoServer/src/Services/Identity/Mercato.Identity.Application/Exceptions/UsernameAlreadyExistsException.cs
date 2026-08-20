namespace Mercato.Identity.Application.Exceptions;

public sealed class UsernameAlreadyExistsException : Exception
{
    public UsernameAlreadyExistsException(string username) : base($"Username '{username}' is already taken.")
    {
    }
}
