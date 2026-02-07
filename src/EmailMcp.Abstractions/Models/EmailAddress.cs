namespace EmailMcp.Abstractions;

/// <summary>
/// Represents an email address with optional display name.
/// </summary>
public sealed record EmailAddress(string Address, string? DisplayName = null)
{
    public override string ToString() =>
        string.IsNullOrWhiteSpace(DisplayName) ? Address : $"{DisplayName} <{Address}>";
}
