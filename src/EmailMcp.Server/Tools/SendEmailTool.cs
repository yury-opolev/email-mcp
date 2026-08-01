using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class SendEmailTool
{
    [McpServerTool(Name = "send_email"), Description(
        "Sends an email from the authenticated account. " +
        "Requires the GmailSend OAuth scope. If you previously authenticated with read-only " +
        "access, you must run 'revoke_auth' and then 'auth_status' to re-grant consent with " +
        "the broader scope before send_email will work. " +
        "Recipient fields accept a comma-separated list. Display names are supported using the " +
        "form 'Display Name <addr@example.com>'. Provide either body, bodyHtml, or both — " +
        "supplying both produces a multipart/alternative message. " +
        "Attachments are given as local file paths on the machine running this server; " +
        "the total must stay under Gmail's 25 MB limit.")]
    public static async Task<string> SendEmail(
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
            return Error("The 'to' field is required.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Error("The 'subject' field is required.");
        }

        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(bodyHtml))
        {
            return Error("At least one of 'body' or 'bodyHtml' must be supplied.");
        }

        var loaded = AttachmentLoader.Load(attachments);
        if (loaded.Error is not null)
        {
            return Error(loaded.Error);
        }

        var request = new SendEmailRequest
        {
            To = ParseAddresses(to),
            Cc = ParseAddresses(cc),
            Bcc = ParseAddresses(bcc),
            Subject = subject,
            Body = body,
            BodyHtml = bodyHtml,
            Attachments = loaded.Attachments,
        };

        if (request.To.Count == 0)
        {
            return Error("Could not parse any valid recipient addresses from 'to'.");
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
            return Error(ex.Message);
        }

        try
        {
            var messageId = await emailProvider.SendEmailAsync(request, cancellationToken);

            return Json(new
            {
                Success = true,
                Account = alias,
                MessageId = messageId,
                To = request.To.Select(a => a.ToString()),
                Cc = request.Cc.Select(a => a.ToString()),
                Bcc = request.Bcc.Select(a => a.ToString()),
                request.Subject,
                Attachments = request.Attachments.Select(a => new { a.Filename, a.MimeType, Bytes = a.Content.Length }),
            });
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return Error(
                $"Gmail rejected the send request for account '{alias}' with HTTP 403. The most " +
                "common cause is insufficient OAuth scope: the stored token was granted before " +
                $"send capability was added. Run 'revoke_auth' then 'auth_status' for account " +
                $"'{alias}' to re-authenticate with the broader scope. " +
                "Underlying error: " + ex.Message);
        }
        catch (Exception ex)
        {
            return Error("Failed to send email: " + ex.Message);
        }
    }

    private static IReadOnlyList<EmailAddress> ParseAddresses(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var tokens = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<EmailAddress>(tokens.Length);
        foreach (var token in tokens)
        {
            var parsed = TryParseSingle(token);
            if (parsed is not null)
            {
                result.Add(parsed);
            }
        }
        return result;
    }

    private static EmailAddress? TryParseSingle(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var lt = token.IndexOf('<');
        var gt = token.IndexOf('>');
        if (lt > 0 && gt > lt)
        {
            var name = token.Substring(0, lt).Trim().Trim('"').Trim();
            var addr = token.Substring(lt + 1, gt - lt - 1).Trim();
            if (string.IsNullOrWhiteSpace(addr))
            {
                return null;
            }
            return new EmailAddress(addr, string.IsNullOrWhiteSpace(name) ? null : name);
        }

        var bare = token.Trim();
        return string.IsNullOrWhiteSpace(bare) ? null : new EmailAddress(bare);
    }

    private static string Error(string message) =>
        Json(new { Success = false, Error = message });

    private static string Json(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
