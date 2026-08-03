namespace EmailMcp.Abstractions;

/// <summary>
/// Describes a reply to an existing message, sent via <see cref="IEmailProvider.ReplyToEmailAsync"/>.
///
/// Deliberately minimal: the recipient, the subject and the threading headers are all derived
/// from the message being replied to, so a caller cannot accidentally send a "reply" that goes
/// to the wrong person or detaches from its conversation.
/// </summary>
public sealed class ReplyToEmailRequest
{
    /// <summary>
    /// Provider-assigned ID of the message being replied to — for Gmail, the value returned as
    /// <see cref="EmailMessage.Id"/> by list/search/read, not the RFC <c>Message-ID</c> header.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Plain-text body. At least one of <see cref="Body"/> or <see cref="BodyHtml"/> is required.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// HTML body. At least one of <see cref="Body"/> or <see cref="BodyHtml"/> is required.
    /// </summary>
    public string? BodyHtml { get; init; }

    /// <summary>
    /// When true, everyone on the original's To and Cc is carried onto the reply's Cc, minus
    /// the account's own address. Defaults to false — a reply goes only to the original sender,
    /// because reply-all is the more damaging mistake of the two.
    /// </summary>
    public bool ReplyAll { get; init; }

    /// <summary>
    /// Extra recipients to Cc, beyond anything <see cref="ReplyAll"/> brings in.
    /// </summary>
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    /// <summary>
    /// Files to attach to the reply. The original's attachments are NOT carried over —
    /// that is forwarding behaviour, not replying behaviour.
    /// </summary>
    public IReadOnlyList<OutboundAttachment> Attachments { get; init; } = [];
}
