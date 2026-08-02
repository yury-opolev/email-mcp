namespace EmailMcp.Abstractions;

/// <summary>
/// What changed when the shared client credentials were set.
/// </summary>
/// <param name="ClientIdChanged">
/// True when the incoming Client ID differs from the one already stored. Tokens issued by the
/// previous client stop working, so this is what makes a rotation worth reporting.
/// </param>
/// <param name="AuthenticatedAccounts">
/// Accounts holding an OAuth token at the moment of the change, in index order. Only meaningful
/// when <paramref name="ClientIdChanged"/> is true.
/// </param>
public sealed record CredentialUpdateResult(
    bool ClientIdChanged,
    IReadOnlyList<string> AuthenticatedAccounts);
