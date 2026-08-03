namespace EmailMcp.Abstractions;

/// <summary>
/// Provides email operations for a specific provider (Gmail, Outlook, etc.).
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// Gets the provider name (e.g., "Gmail", "Outlook").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Lists recent emails.
    /// </summary>
    Task<IReadOnlyList<EmailMessage>> ListEmailsAsync(
        int maxResults = 20,
        string? labelId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single email by its ID with full content.
    /// </summary>
    Task<EmailMessage> GetEmailAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches emails using the provided query.
    /// </summary>
    Task<IReadOnlyList<EmailMessage>> SearchEmailsAsync(
        EmailSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available labels/folders.
    /// </summary>
    Task<IReadOnlyList<EmailLabel>> ListLabelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email from the authenticated account. Returns the provider-assigned message ID
    /// of the sent message.
    /// </summary>
    Task<string> SendEmailAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replies to an existing message, threaded. Returns the provider-assigned message ID of
    /// the sent reply.
    ///
    /// Unlike <see cref="SendEmailAsync"/> this derives the recipient, the subject and the
    /// <c>In-Reply-To</c> / <c>References</c> headers from the message being replied to, so the
    /// reply attaches to its conversation in any client rather than only in Gmail — which
    /// guesses threads from subject and participants, and is close to alone in doing so.
    /// </summary>
    Task<string> ReplyToEmailAsync(
        ReplyToEmailRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an email as a draft without sending it. Returns the provider-assigned draft ID.
    /// Takes the same request shape as <see cref="SendEmailAsync"/>, so a draft that is later
    /// sent from the mail client's own UI is the same message that would have been sent here.
    /// </summary>
    Task<string> CreateDraftAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken = default);
}
