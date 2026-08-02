using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class CreateDraftTool
{
    [McpServerTool(Name = "create_draft"), Description(
        "Saves an email as a draft in the account's mailbox without sending it, so it can be " +
        "reviewed and sent by hand from the mail client. " +
        "Requires the GmailCompose OAuth scope. If the stored sign-in predates draft support, " +
        "run 'revoke_auth' then 'auth_status' for the account to re-grant consent. " +
        "Takes the same arguments as 'send_email': recipient fields accept a comma-separated " +
        "list, display names are supported using the form 'Display Name <addr@example.com>', " +
        "and supplying both body and bodyHtml produces a multipart/alternative message. " +
        "Attachments are given as local file paths on the machine running this server; " +
        "the total must stay under Gmail's 25 MB limit.")]
    public static async Task<string> CreateDraft(
        IAccountRegistry accounts,
        [Description("Comma-separated list of recipient email addresses (e.g. 'alice@example.com, Bob <bob@example.com>').")] string to,
        [Description("Email subject line.")] string subject,
        [Description("Plain-text body. Provide either body, bodyHtml, or both.")] string? body = null,
        [Description("HTML body. Provide either body, bodyHtml, or both.")] string? bodyHtml = null,
        [Description("Comma-separated list of Cc recipients (optional).")] string? cc = null,
        [Description("Comma-separated list of Bcc recipients (optional).")] string? bcc = null,
        [Description("Comma-separated list of local file paths to attach (optional), e.g. 'C:\\\\reports\\\\q3.pdf, C:\\\\img\\\\chart.png'. Total size must be under 25 MB.")] string? attachments = null,
        [Description(AccountParameter.Description)] string? account = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            return ToolResponse.Error("The 'to' field is required.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return ToolResponse.Error("The 'subject' field is required.");
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

        var request = new SendEmailRequest
        {
            To = AddressListParser.Parse(to),
            Cc = AddressListParser.Parse(cc),
            Bcc = AddressListParser.Parse(bcc),
            Subject = subject,
            Body = body,
            BodyHtml = bodyHtml,
            Attachments = loaded.Attachments,
        };

        if (request.To.Count == 0)
        {
            return ToolResponse.Error("Could not parse any valid recipient addresses from 'to'.");
        }

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
            var draftId = await emailProvider.CreateDraftAsync(request, cancellationToken);

            return ToolResponse.Json(new
            {
                Success = true,
                Account = alias,
                DraftId = draftId,
                To = request.To.Select(a => a.ToString()),
                Cc = request.Cc.Select(a => a.ToString()),
                Bcc = request.Bcc.Select(a => a.ToString()),
                request.Subject,
                Attachments = request.Attachments.Select(a => new { a.Filename, a.MimeType, Bytes = a.Content.Length }),
            });
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return ToolResponse.Error(
                $"Gmail rejected the draft request for account '{alias}' with HTTP 403. The most " +
                "common cause is insufficient OAuth scope: the stored token was granted before " +
                $"draft capability was added. Run 'revoke_auth' then 'auth_status' for account " +
                $"'{alias}' to re-authenticate with the broader scope. " +
                "Underlying error: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ToolResponse.Error("Failed to create draft: " + ex.Message);
        }
    }
}
