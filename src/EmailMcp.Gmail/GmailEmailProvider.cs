using System.Text;
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

    public async Task<string> SendEmailAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await GetServiceAsync(cancellationToken);

        var mime = MimeBuilder.Build(request);
        var raw = Base64UrlEncode(mime);

        var message = new Google.Apis.Gmail.v1.Data.Message { Raw = raw };
        var sent = await service.Users.Messages.Send(message, "me").ExecuteAsync(cancellationToken);

        _logger.LogInformation(
            "Sent email; messageId={MessageId}, to={ToCount}, cc={CcCount}, bcc={BccCount}",
            sent.Id,
            request.To.Count,
            request.Cc.Count,
            request.Bcc.Count);

        return sent.Id;
    }

    public async Task<string> ReplyToEmailAsync(
        ReplyToEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MessageId);

        if (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.BodyHtml))
        {
            throw new ArgumentException(
                "ReplyToEmailRequest must include Body and/or BodyHtml.", nameof(request));
        }

        var service = await GetServiceAsync(cancellationToken);

        var originalRaw = await service.Users.Messages
            .Get("me", request.MessageId)
            .ExecuteAsync(cancellationToken);
        var original = GmailMapper.ToEmailMessage(originalRaw);

        if (original.From is null)
        {
            throw new InvalidOperationException(
                $"Cannot reply to message '{request.MessageId}': it has no From header, so there is " +
                "nobody to reply to.");
        }

        var cc = await BuildReplyCcAsync(request, original, cancellationToken);

        var reply = new SendEmailRequest
        {
            To = [original.From],
            Cc = cc,
            Subject = MailSubject.EnsureReplyPrefix(original.Subject),
            Body = request.Body,
            BodyHtml = request.BodyHtml,
            Attachments = request.Attachments,
            InReplyTo = original.MessageIdHeader,
            References = BuildReferences(original),
        };

        if (original.MessageIdHeader is null)
        {
            // Rare, and worth knowing about: without a Message-ID there is nothing to thread
            // against, so this degrades to a plain message carrying a "Re:" subject.
            _logger.LogWarning(
                "Replying to message {MessageId} which has no Message-ID header; the reply cannot " +
                "carry threading headers and will not thread outside Gmail.",
                request.MessageId);
        }

        var mime = MimeBuilder.Build(reply);
        var message = new Google.Apis.Gmail.v1.Data.Message
        {
            Raw = Base64UrlEncode(mime),
            // Gmail files the sent message into the same conversation server-side. The MIME
            // headers above are what make it thread for the *recipient*; this is what makes it
            // thread in the sender's own mailbox.
            ThreadId = original.ThreadId,
        };

        var sent = await service.Users.Messages.Send(message, "me").ExecuteAsync(cancellationToken);

        _logger.LogInformation(
            "Sent reply; messageId={MessageId}, inReplyTo={InReplyTo}, threadId={ThreadId}, cc={CcCount}",
            sent.Id,
            original.MessageIdHeader,
            original.ThreadId,
            cc.Count);

        return sent.Id;
    }

    /// <summary>
    /// The <c>References</c> chain for a reply: the parent's own chain with the parent's
    /// Message-ID appended. A first reply has no parent chain and so is just the parent's ID.
    /// </summary>
    private static string? BuildReferences(EmailMessage original)
    {
        if (original.MessageIdHeader is null)
        {
            return original.References;
        }

        return string.IsNullOrWhiteSpace(original.References)
            ? original.MessageIdHeader
            : original.References.Trim() + " " + original.MessageIdHeader;
    }

    /// <summary>
    /// Cc for a reply: the caller's explicit additions, plus — only when ReplyAll is set — the
    /// original's To and Cc with this account's own address removed, so replying all does not
    /// mail yourself. De-duplicated case-insensitively.
    /// </summary>
    private async Task<IReadOnlyList<EmailAddress>> BuildReplyCcAsync(
        ReplyToEmailRequest request,
        EmailMessage original,
        CancellationToken cancellationToken)
    {
        var result = new List<EmailAddress>(request.Cc);

        if (request.ReplyAll)
        {
            var service = await GetServiceAsync(cancellationToken);
            var profile = await service.Users.GetProfile("me").ExecuteAsync(cancellationToken);
            var self = profile.EmailAddress;

            foreach (var candidate in original.To.Concat(original.Cc))
            {
                if (!string.IsNullOrWhiteSpace(self)
                    && string.Equals(candidate.Address, self, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(candidate);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // The reply already goes To the original sender; repeating them in Cc is noise.
            original.From!.Address,
        };

        var deduped = new List<EmailAddress>();
        foreach (var address in result)
        {
            if (seen.Add(address.Address))
            {
                deduped.Add(address);
            }
        }

        return deduped;
    }

    public async Task<string> CreateDraftAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await GetServiceAsync(cancellationToken);

        var mime = MimeBuilder.Build(request);
        var raw = Base64UrlEncode(mime);

        var draft = new Google.Apis.Gmail.v1.Data.Draft
        {
            Message = new Google.Apis.Gmail.v1.Data.Message { Raw = raw },
        };
        var created = await service.Users.Drafts.Create(draft, "me").ExecuteAsync(cancellationToken);

        _logger.LogInformation(
            "Created draft; draftId={DraftId}, to={ToCount}, cc={CcCount}, bcc={BccCount}",
            created.Id,
            request.To.Count,
            request.Cc.Count,
            request.Bcc.Count);

        return created.Id;
    }

    private static string Base64UrlEncode(string mime)
    {
        var bytes = Encoding.UTF8.GetBytes(mime);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
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
