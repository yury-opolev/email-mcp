namespace EmailMcp.Abstractions;

/// <summary>
/// The persisted list of configured accounts and which one is default.
/// </summary>
public sealed class AccountIndex
{
    /// <summary>
    /// Alias of the default account, or null if none has been chosen.
    /// Ignored when exactly one account exists, since that account is always the default.
    /// </summary>
    public string? DefaultAlias { get; set; }

    /// <summary>Configured accounts.</summary>
    public List<EmailAccount> Accounts { get; set; } = [];
}
