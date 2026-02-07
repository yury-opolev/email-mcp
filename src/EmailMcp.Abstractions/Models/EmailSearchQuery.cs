namespace EmailMcp.Abstractions;

/// <summary>
/// Represents criteria for searching emails.
/// </summary>
public sealed class EmailSearchQuery
{
    /// <summary>
    /// Free-text query string (provider-specific syntax, e.g. Gmail search operators).
    /// </summary>
    public string? Query { get; init; }

    /// <summary>
    /// Filter by sender address.
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// Filter by recipient address.
    /// </summary>
    public string? To { get; init; }

    /// <summary>
    /// Filter by subject text.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Only return emails after this date.
    /// </summary>
    public DateTimeOffset? After { get; init; }

    /// <summary>
    /// Only return emails before this date.
    /// </summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>
    /// Filter by label/folder ID.
    /// </summary>
    public string? LabelId { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int MaxResults { get; init; } = 20;
}
