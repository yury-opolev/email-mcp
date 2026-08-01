namespace EmailMcp.Abstractions;

/// <summary>
/// Validation and normalisation for account aliases.
/// </summary>
/// <remarks>
/// The charset is deliberately narrow. Aliases become part of token-store keys, which in turn
/// become filenames, so anything that could collide after filename sanitisation is rejected here.
/// </remarks>
public static class AccountAlias
{
    /// <summary>Maximum alias length.</summary>
    public const int MaxLength = 32;

    /// <summary>
    /// Reserved aliases that would collide with the account index key.
    /// </summary>
    private static readonly HashSet<string> reserved = new(StringComparer.Ordinal) { "index" };

    /// <summary>
    /// Human-readable statement of the rule, for error messages.
    /// </summary>
    public const string Rule =
        "An account alias must be 1-32 characters of lowercase letters, digits or hyphens, " +
        "must not start or end with a hyphen, and must not be 'index'.";

    /// <summary>
    /// Normalises an alias by trimming and lowercasing it.
    /// </summary>
    public static string Normalize(string alias) => alias?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Returns true if the normalised alias is valid.
    /// </summary>
    public static bool IsValid(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        var normalized = Normalize(alias);

        if (normalized.Length is 0 or > MaxLength)
        {
            return false;
        }

        if (reserved.Contains(normalized))
        {
            return false;
        }

        if (normalized[0] == '-' || normalized[^1] == '-')
        {
            return false;
        }

        foreach (var character in normalized)
        {
            var isAllowed = character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9'
                || character == '-';

            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
