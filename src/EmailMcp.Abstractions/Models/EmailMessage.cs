namespace EmailMcp.Abstractions;

/// <summary>
/// Represents an email message with metadata and content.
/// </summary>
public sealed class EmailMessage
{
    public required string Id { get; init; }
    public required string ThreadId { get; init; }
    public string? Subject { get; init; }
    public EmailAddress? From { get; init; }
    public IReadOnlyList<EmailAddress> To { get; init; } = [];
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public DateTimeOffset? Date { get; init; }
    public string? Snippet { get; init; }
    public string? Body { get; init; }
    public string? BodyHtml { get; init; }
    public bool IsUnread { get; init; }
    public IReadOnlyList<string> LabelIds { get; init; } = [];
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}
