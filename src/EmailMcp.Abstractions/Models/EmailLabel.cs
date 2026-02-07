namespace EmailMcp.Abstractions;

/// <summary>
/// Represents an email label or folder.
/// </summary>
public sealed class EmailLabel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Type { get; init; }
    public int? UnreadCount { get; init; }
    public int? TotalCount { get; init; }
}
