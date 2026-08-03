namespace EmailMcp.Abstractions;

/// <summary>
/// Represents an email message with metadata and content.
/// </summary>
public sealed class EmailMessage
{
    public required string Id { get; init; }
    public required string ThreadId { get; init; }

    /// <summary>
    /// The RFC 5322 <c>Message-ID</c> header, including its angle brackets. This is the
    /// globally unique identifier other mail clients thread on, and is NOT the same thing
    /// as <see cref="Id"/> (a Gmail-internal handle) or <see cref="ThreadId"/>. Required to
    /// build a reply that threads correctly outside Gmail. Null if the header was absent.
    /// </summary>
    public string? MessageIdHeader { get; init; }

    /// <summary>
    /// The raw <c>References</c> header: the chain of ancestor Message-IDs, space separated.
    /// A reply appends the parent's <see cref="MessageIdHeader"/> to this. Null if absent,
    /// which is normal for the first message in a thread.
    /// </summary>
    public string? References { get; init; }

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
