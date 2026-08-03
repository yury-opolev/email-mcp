using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class ReplyToEmailTool
{
    [McpServerTool(Name = "reply_to_email"), Description(
        "Replies to an existing email, properly threaded. Use this instead of 'send_email' " +
        "whenever responding to a message: it sets the In-Reply-To and References headers, so " +
        "the reply attaches to its conversation in every mail client. Sending a new message with " +
        "a 'Re:' subject only threads in Gmail, which guesses from subject and participants. " +
        "The recipient, the subject and the threading headers are all taken from the original, " +
        "so only a body is required. Pass the message ID from 'list_emails', 'search_emails' or " +
        "'read_email'. Replies to the original sender alone unless replyAll is set.")]
    public static async Task<string> ReplyToEmail(
        IAccountRegistry accounts,
        [Description("ID of the message being replied to, as returned by list_emails, search_emails or read_email.")] string messageId,
        [Description("Plain-text body of the reply. Provide either body, bodyHtml, or both.")] string? body = null,
        [Description("HTML body of the reply. Provide either body, bodyHtml, or both.")] string? bodyHtml = null,
        [Description("Set true to Cc everyone on the original's To and Cc (minus your own address). Defaults to false - replies to the sender only.")] bool replyAll = false,
        [Description("Extra Cc recipients beyond replyAll, comma-separated (e.g. 'alice@example.com, Bob <bob@example.com>').")] string? cc = null,
        [Description("Comma-separated local file paths to attach (optional). The original's attachments are NOT carried over - that would be forwarding.")] string? attachments = null,
        [Description(AccountParameter.Description)] string? account = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return ToolResponse.Error("The 'messageId' field is required.");
        }

        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(bodyHtml))
        {
            return ToolResponse.Error("At least one of 'body' or 'bodyHtml' must be supplied.");
        }

        var loaded = AttachmentLoader.Load(attachments);
        if (loaded.Error is not null)
        {
            return ToolResponse.Error(loaded.Error);
        }

        var request = new ReplyToEmailRequest
        {
            MessageId = messageId,
            Body = body,
            BodyHtml = bodyHtml,
            ReplyAll = replyAll,
            Cc = AddressListParser.Parse(cc),
            Attachments = loaded.Attachments,
        };

        string alias;
        IEmailProvider emailProvider;
        try
        {
            alias = await accounts.ResolveAliasAsync(account, cancellationToken);
            emailProvider = await accounts.GetProviderAsync(alias, cancellationToken);
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }

        try
        {
            var sentId = await emailProvider.ReplyToEmailAsync(request, cancellationToken);

            return ToolResponse.Json(new
            {
                Success = true,
                Account = alias,
                MessageId = sentId,
                InReplyTo = messageId,
                ReplyAll = replyAll,
                Attachments = request.Attachments.Select(a => new { a.Filename, a.MimeType, Bytes = a.Content.Length }),
            });
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ToolResponse.Error(
                $"No message with ID '{messageId}' was found in account '{alias}'. Message IDs are " +
                "per-account, so check you are replying from the account that received it. " +
                "Underlying error: " + ex.Message);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return ToolResponse.Error(
                $"Gmail rejected the reply for account '{alias}' with HTTP 403. The most common " +
                "cause is insufficient OAuth scope. Run 'revoke_auth' then 'auth_status' for " +
                $"account '{alias}' to re-authenticate. Underlying error: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ToolResponse.Error("Failed to send reply: " + ex.Message);
        }
    }
}
