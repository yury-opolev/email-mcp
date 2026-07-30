using System.Text;
using EmailMcp.Abstractions;
using EmailMcp.Gmail;
using FluentAssertions;

namespace EmailMcp.Gmail.Tests;

public sealed class MimeBuilderAttachmentTests
{
    private static SendEmailRequest Request(
        string? body = "hello",
        string? bodyHtml = null,
        IReadOnlyList<OutboundAttachment>? attachments = null) => new()
        {
            To = [new EmailAddress("to@example.com")],
            Subject = "Subject",
            Body = body,
            BodyHtml = bodyHtml,
            Attachments = attachments ?? [],
        };

    private static OutboundAttachment Attachment(
        string filename = "note.txt",
        string mimeType = "text/plain",
        byte[]? content = null) => new()
        {
            Filename = filename,
            MimeType = mimeType,
            Content = content ?? Encoding.UTF8.GetBytes("file body"),
        };

    [Fact]
    public void Build_WithAttachment_UsesMultipartMixed()
    {
        var mime = MimeBuilder.Build(Request(attachments: [Attachment()]));

        mime.Should().Contain("Content-Type: multipart/mixed; boundary=");
    }

    [Fact]
    public void Build_WithAttachment_EncodesContentAsBase64()
    {
        var mime = MimeBuilder.Build(Request(
            attachments: [Attachment(content: Encoding.UTF8.GetBytes("file body"))]));

        mime.Should().Contain("Content-Transfer-Encoding: base64");
        mime.Should().Contain(Convert.ToBase64String(Encoding.UTF8.GetBytes("file body")));
    }

    [Fact]
    public void Build_WithAttachment_SetsContentDispositionWithFilename()
    {
        var mime = MimeBuilder.Build(Request(attachments: [Attachment(filename: "report.pdf")]));

        mime.Should().Contain("Content-Disposition: attachment; filename=\"report.pdf\"");
    }

    [Fact]
    public void Build_WithoutAttachments_DoesNotUseMultipartMixed()
    {
        var mime = MimeBuilder.Build(Request());

        mime.Should().NotContain("multipart/mixed");
    }

    [Fact]
    public void Build_WithAttachmentAndBothBodies_NestsAlternativeInsideMixed()
    {
        var mime = MimeBuilder.Build(Request(
            body: "plain",
            bodyHtml: "<p>html</p>",
            attachments: [Attachment()]));

        var mixedIndex = mime.IndexOf("multipart/mixed", StringComparison.Ordinal);
        var alternativeIndex = mime.IndexOf("multipart/alternative", StringComparison.Ordinal);

        mixedIndex.Should().BeGreaterThanOrEqualTo(0);
        alternativeIndex.Should().BeGreaterThan(mixedIndex,
            "the alternative part must be nested inside the mixed container");
    }

    [Fact]
    public void Build_WithNonAsciiFilename_EncodesFilenameHeader()
    {
        var mime = MimeBuilder.Build(Request(attachments: [Attachment(filename: "отчёт.pdf")]));

        mime.Should().NotContain("отчёт.pdf", "a raw non-ASCII filename is not valid in a MIME header");
        mime.Should().Contain("=?UTF-8?B?");
    }

    [Fact]
    public void Build_WrapsBase64PayloadAtSeventySixCharacters()
    {
        // 1000 bytes encodes to ~1336 base64 chars, so an unwrapped payload would be
        // a single line far over the RFC 2045 limit. Header lines are exempt — the
        // boundary header alone exceeds 76 — so assert on the payload only.
        var big = new byte[1000];
        Random.Shared.NextBytes(big);

        var mime = MimeBuilder.Build(Request(attachments: [Attachment(content: big)]));

        var payloadLines = Base64PayloadLines(mime);

        payloadLines.Should().HaveCountGreaterThan(1, "a 1000-byte attachment must wrap onto several lines");
        payloadLines.Should().OnlyContain(l => l.Length <= 76);
    }

    /// <summary>
    /// Extracts the encoded lines of the first base64 part: everything between the
    /// blank line that ends that part's headers and the next boundary marker.
    /// </summary>
    private static List<string> Base64PayloadLines(string mime)
    {
        var lines = mime.Split("\r\n");
        var start = Array.FindIndex(lines, l => l == "Content-Transfer-Encoding: base64");
        start.Should().BeGreaterThanOrEqualTo(0, "the attachment part must declare base64 encoding");

        var blank = Array.FindIndex(lines, start, l => l.Length == 0);
        var payload = new List<string>();
        for (var i = blank + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("--", StringComparison.Ordinal) || lines[i].Length == 0)
            {
                break;
            }

            payload.Add(lines[i]);
        }

        return payload;
    }
}
