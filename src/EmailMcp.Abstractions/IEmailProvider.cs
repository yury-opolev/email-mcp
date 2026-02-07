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
}
