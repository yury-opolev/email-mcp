namespace EmailMcp.Abstractions;

/// <summary>
/// Raised when an account cannot be resolved or an account operation is invalid.
/// The message is written to be shown directly to the user.
/// </summary>
public sealed class AccountException : Exception
{
    public AccountException(string message)
        : base(message)
    {
    }
}
