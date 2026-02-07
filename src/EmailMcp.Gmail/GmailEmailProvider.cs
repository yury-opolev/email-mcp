using EmailMcp.Abstractions;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;

namespace EmailMcp.Gmail;

/// <summary>
/// Gmail implementation of IEmailProvider using Google's Gmail API.
/// </summary>
public sealed class GmailEmailProvider : IEmailProvider
{
    private readonly GmailAuthenticator _authenticator;
    private readonly GmailOptions _options;
    private readonly ILogger<GmailEmailProvider> _logger;

    private GmailService? _service;

    public string ProviderName => "Gmail";

    public GmailEmailProvider(
        GmailAuthenticator authenticator,
        GmailOptions options,
        ILogger<GmailEmailProvider> logger)
    {
        _authenticator = authenticator;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EmailMessage>> ListEmailsAsync(
        int maxResults = 20,
        string? labelId = null,
        CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.Users.Messages.List("me");
        request.MaxResults = maxResults;

        if (!string.IsNullOrWhiteSpace(labelId))
            request.LabelIds = new List<string> { labelId };

        var response = await request.ExecuteAsync(cancellationToken);
        if (response.Messages is null || response.Messages.Count == 0)
            return [];

        var messages = new List<EmailMessage>();
        foreach (var stub in response.Messages)
        {
            var full = await service.Users.Messages.Get("me", stub.Id).ExecuteAsync(cancellationToken);
            messages.Add(GmailMapper.ToEmailMessage(full));
        }

        _logger.LogDebug("Listed {Count} emails", messages.Count);
        return messages;
    }

    public async Task<EmailMessage> GetEmailAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var service = await GetServiceAsync(cancellationToken);
        var message = await service.Users.Messages.Get("me", messageId).ExecuteAsync(cancellationToken);

        _logger.LogDebug("Retrieved email {MessageId}", messageId);
        return GmailMapper.ToEmailMessage(message, includeBody: true);
    }

    public async Task<IReadOnlyList<EmailMessage>> SearchEmailsAsync(
        EmailSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.Users.Messages.List("me");
        request.Q = BuildGmailQuery(query);
        request.MaxResults = query.MaxResults;

        if (!string.IsNullOrWhiteSpace(query.LabelId))
            request.LabelIds = new List<string> { query.LabelId };

        var response = await request.ExecuteAsync(cancellationToken);
        if (response.Messages is null || response.Messages.Count == 0)
            return [];

        var messages = new List<EmailMessage>();
        foreach (var stub in response.Messages)
        {
            var full = await service.Users.Messages.Get("me", stub.Id).ExecuteAsync(cancellationToken);
            messages.Add(GmailMapper.ToEmailMessage(full));
        }

        _logger.LogDebug("Search returned {Count} emails for query '{Query}'", messages.Count, request.Q);
        return messages;
    }

    public async Task<IReadOnlyList<EmailLabel>> ListLabelsAsync(
        CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(cancellationToken);
        var response = await service.Users.Labels.List("me").ExecuteAsync(cancellationToken);

        if (response.Labels is null)
            return [];

        var labels = response.Labels.Select(l => new EmailLabel
        {
            Id = l.Id,
            Name = l.Name,
            Type = l.Type,
            UnreadCount = (int?)(l.MessagesUnread),
            TotalCount = (int?)(l.MessagesTotal),
        }).ToList();

        _logger.LogDebug("Listed {Count} labels", labels.Count);
        return labels;
    }

    private static string BuildGmailQuery(EmailSearchQuery query)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Query))
            parts.Add(query.Query);
        if (!string.IsNullOrWhiteSpace(query.From))
            parts.Add($"from:{query.From}");
        if (!string.IsNullOrWhiteSpace(query.To))
            parts.Add($"to:{query.To}");
        if (!string.IsNullOrWhiteSpace(query.Subject))
            parts.Add($"subject:{query.Subject}");
        if (query.After.HasValue)
            parts.Add($"after:{query.After.Value:yyyy/MM/dd}");
        if (query.Before.HasValue)
            parts.Add($"before:{query.Before.Value:yyyy/MM/dd}");

        return string.Join(" ", parts);
    }

    private async Task<GmailService> GetServiceAsync(CancellationToken cancellationToken)
    {
        if (_service is not null)
            return _service;

        var credential = await _authenticator.GetCredentialAsync(cancellationToken);
        _service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });

        return _service;
    }
}
