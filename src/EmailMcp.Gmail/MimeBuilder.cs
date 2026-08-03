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

        // Threading headers. These are what make a reply attach to its conversation in
        // clients that thread properly; Gmail alone would guess from subject + participants,
        // but nothing else does. Message-IDs are already ASCII and angle-bracketed, so they
        // are written verbatim rather than run through EncodeHeaderValue.
        if (!string.IsNullOrWhiteSpace(request.InReplyTo))
        {
            sb.Append("In-Reply-To: ").Append(request.InReplyTo.Trim()).Append(Crlf);
        }

        if (!string.IsNullOrWhiteSpace(request.References))
        {
            sb.Append("References: ").Append(request.References.Trim()).Append(Crlf);
        }

        sb.Append("MIME-Version: 1.0").Append(Crlf);

        if (request.Attachments.Count > 0)
        {
            // multipart/mixed wraps the body (which is itself multipart/alternative
            // when both bodies are present) followed by one part per attachment.
            var mixedBoundary = NewBoundary();
            sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(mixedBoundary).Append('"').Append(Crlf);
            sb.Append(Crlf);

            sb.Append("--").Append(mixedBoundary).Append(Crlf);
            AppendBody(sb, request, asNestedPart: true);

            foreach (var attachment in request.Attachments)
            {
                sb.Append("--").Append(mixedBoundary).Append(Crlf);
                AppendAttachmentPart(sb, attachment);
            }

            sb.Append("--").Append(mixedBoundary).Append("--").Append(Crlf);
            return sb.ToString();
        }

        AppendBody(sb, request, asNestedPart: false);
        return sb.ToString();
    }

    private static string NewBoundary() => "=_EmailMcp_" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Writes the message body. When <paramref name="asNestedPart"/> the caller has
    /// already emitted the enclosing boundary line, so this writes only the part's
    /// own headers and content; otherwise the headers belong to the message itself.
    /// </summary>
    private static void AppendBody(StringBuilder sb, SendEmailRequest request, bool asNestedPart)
    {
        var hasText = !string.IsNullOrEmpty(request.Body);
        var hasHtml = !string.IsNullOrEmpty(request.BodyHtml);

        if (hasText && hasHtml)
        {
            var boundary = NewBoundary();
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

        _ = asNestedPart;
    }

    private static void AppendAttachmentPart(StringBuilder sb, OutboundAttachment attachment)
    {
        sb.Append("Content-Type: ").Append(attachment.MimeType)
          .Append("; name=\"").Append(EncodeHeaderValue(attachment.Filename)).Append('"').Append(Crlf);
        sb.Append("Content-Transfer-Encoding: base64").Append(Crlf);
        sb.Append("Content-Disposition: attachment; filename=\"")
          .Append(EncodeHeaderValue(attachment.Filename)).Append('"').Append(Crlf);
        sb.Append(Crlf);

        AppendWrappedBase64(sb, attachment.Content);
    }

    /// <summary>
    /// RFC 2045 caps encoded lines at 76 characters; Gmail rejects longer ones.
    /// </summary>
    private static void AppendWrappedBase64(StringBuilder sb, byte[] content)
    {
        const int LineLength = 76;
        var encoded = Convert.ToBase64String(content);

        for (var offset = 0; offset < encoded.Length; offset += LineLength)
        {
            var take = Math.Min(LineLength, encoded.Length - offset);
            sb.Append(encoded, offset, take).Append(Crlf);
        }
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
