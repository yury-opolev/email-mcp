namespace EmailMcp.Abstractions;

/// <summary>
/// A configured email account, identified by a short user-chosen alias.
/// </summary>
/// <param name="Alias">Short identifier used to select this account, e.g. "studio".</param>
/// <param name="EmailAddress">
/// The real address behind the alias. Null until the first successful authentication, because
/// the address is not known until OAuth consent completes.
/// </param>
/// <param name="CreatedUtc">When the account was added.</param>
public sealed record EmailAccount(
    string Alias,
    string? EmailAddress,
    DateTimeOffset CreatedUtc);
