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
}
