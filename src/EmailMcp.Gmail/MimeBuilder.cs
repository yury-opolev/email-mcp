using System.Text;
using EmailMcp.Abstractions;

namespace EmailMcp.Gmail;

/// <summary>
/// Builds a minimal RFC 5322 / MIME message suitable for submission via the Gmail
/// <c>Users.Messages.Send</c> API. Supports plain text, HTML, or both (multipart/alternative).
/// </summary>
internal static class MimeBuilder
{
    private const string Crlf = "\r\n";

    public static string Build(SendEmailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.To.Count == 0)
        {
            throw new ArgumentException("SendEmailRequest.To must contain at least one recipient.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.BodyHtml))
        {
            throw new ArgumentException("SendEmailRequest must include Body and/or BodyHtml.", nameof(request));
        }

        var sb = new StringBuilder();

        AppendAddressHeader(sb, "To", request.To);
        AppendAddressHeader(sb, "Cc", request.Cc);
        // Bcc is intentionally NOT included in the visible MIME headers; Gmail
        // applies it server-side based on the recipient list it sees in the SMTP
        // envelope. To get Bcc delivery via Users.Messages.Send we still include
        // it in the raw message — Google strips it from the delivered headers.
        AppendAddressHeader(sb, "Bcc", request.Bcc);
        sb.Append("Subject: ").Append(EncodeHeaderValue(request.Subject)).Append(Crlf);
        sb.Append("MIME-Version: 1.0").Append(Crlf);

        var hasText = !string.IsNullOrEmpty(request.Body);
        var hasHtml = !string.IsNullOrEmpty(request.BodyHtml);

        if (hasText && hasHtml)
        {
            var boundary = "=_EmailMcp_" + Guid.NewGuid().ToString("N");
            sb.Append("Content-Type: multipart/alternative; boundary=\"").Append(boundary).Append('"').Append(Crlf);
            sb.Append(Crlf);

            AppendPart(sb, boundary, "text/plain", request.Body!);
            AppendPart(sb, boundary, "text/html", request.BodyHtml!);

            sb.Append("--").Append(boundary).Append("--").Append(Crlf);
        }
        else if (hasHtml)
        {
            sb.Append("Content-Type: text/html; charset=UTF-8").Append(Crlf);
            sb.Append("Content-Transfer-Encoding: 8bit").Append(Crlf);
            sb.Append(Crlf);
            sb.Append(request.BodyHtml).Append(Crlf);
        }
        else
        {
            sb.Append("Content-Type: text/plain; charset=UTF-8").Append(Crlf);
            sb.Append("Content-Transfer-Encoding: 8bit").Append(Crlf);
            sb.Append(Crlf);
            sb.Append(request.Body).Append(Crlf);
        }

        return sb.ToString();
    }

    private static void AppendAddressHeader(StringBuilder sb, string headerName, IReadOnlyList<EmailAddress> addresses)
    {
        if (addresses.Count == 0)
        {
            return;
        }

        sb.Append(headerName).Append(": ");
        for (var i = 0; i < addresses.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var addr = addresses[i];
            if (!string.IsNullOrWhiteSpace(addr.DisplayName))
            {
                sb.Append(EncodeHeaderValue(addr.DisplayName)).Append(" <").Append(addr.Address).Append('>');
            }
            else
            {
                sb.Append(addr.Address);
            }
        }
        sb.Append(Crlf);
    }

    private static void AppendPart(StringBuilder sb, string boundary, string contentType, string body)
    {
        sb.Append("--").Append(boundary).Append(Crlf);
        sb.Append("Content-Type: ").Append(contentType).Append("; charset=UTF-8").Append(Crlf);
        sb.Append("Content-Transfer-Encoding: 8bit").Append(Crlf);
        sb.Append(Crlf);
        sb.Append(body).Append(Crlf);
    }

    /// <summary>
    /// Encodes a header value using RFC 2047 (encoded-word) if it contains any
    /// non-ASCII characters; otherwise returns it unchanged.
    /// </summary>
    private static string EncodeHeaderValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsEncoding = false;
        foreach (var c in value)
        {
            if (c > 127 || c == '\r' || c == '\n')
            {
                needsEncoding = true;
                break;
            }
        }

        if (!needsEncoding)
        {
            return value;
        }

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return "=?UTF-8?B?" + base64 + "?=";
    }
}
