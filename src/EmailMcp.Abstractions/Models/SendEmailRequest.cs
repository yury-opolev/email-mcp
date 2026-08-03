namespace EmailMcp.Abstractions;

/// <summary>
/// Describes an outbound email message to be sent via <see cref="IEmailProvider.SendEmailAsync"/>.
/// </summary>
public sealed class SendEmailRequest
{
    public required IReadOnlyList<EmailAddress> To { get; init; }
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public required string Subject { get; init; }

    /// <summary>
    /// Plain-text body. At least one of <see cref="Body"/> or <see cref="BodyHtml"/> must be supplied.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// HTML body. At least one of <see cref="Body"/> or <see cref="BodyHtml"/> must be supplied.
    /// </summary>
    public string? BodyHtml { get; init; }

    /// <summary>
    /// Files to attach. When non-empty the message is built as <c>multipart/mixed</c>,
    /// with the body (itself <c>multipart/alternative</c> if both bodies are supplied)
    /// as the first part.
    /// </summary>
    public IReadOnlyList<OutboundAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// RFC 5322 <c>In-Reply-To</c>: the <c>Message-ID</c> of the message being replied to,
    /// angle brackets included. Set this and <see cref="References"/> together — a reply
    /// carrying only a <c>Re:</c> subject threads in Gmail (which infers threads from
    /// subject and participants) but appears as an unrelated message in clients that
    /// thread strictly on headers, which is most of them.
    /// </summary>
    public string? InReplyTo { get; init; }

    /// <summary>
    /// RFC 5322 <c>References</c>: the ancestor <c>Message-ID</c> chain, space separated,
    /// ending with the parent. Built by appending the parent's Message-ID to whatever
    /// <c>References</c> the parent carried.
    /// </summary>
    public string? References { get; init; }
}
