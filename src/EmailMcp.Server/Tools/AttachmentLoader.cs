using EmailMcp.Abstractions;

namespace EmailMcp.Server.Tools;

/// <summary>
/// Turns a comma-separated list of local file paths into <see cref="OutboundAttachment"/>s.
/// </summary>
/// <remarks>
/// Paths rather than inline content: the MCP server runs on the same machine as the
/// client, so pushing tens of megabytes of base64 through the protocol would be
/// wasteful. Reading is therefore bounded by whatever the server process can already
/// see, which is the same trust boundary the rest of the host's file tools operate in.
/// </remarks>
public static class AttachmentLoader
{
    /// <summary>
    /// Gmail refuses messages larger than 25 MB. Checked against the raw bytes before
    /// encoding, so the real limit is stricter still once base64 adds its ~33%; the
    /// point is to fail fast with a clear message instead of uploading and being
    /// rejected by the API.
    /// </summary>
    public const long MaxTotalBytes = 25L * 1024 * 1024;

    private static readonly Dictionary<string, string> MimeTypesByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".csv"] = "text/csv",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip",
            [".apk"] = "application/vnd.android.package-archive",
            [".aab"] = "application/octet-stream",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        };

    public sealed record Result(IReadOnlyList<OutboundAttachment> Attachments, string? Error);

    public static Result Load(string? paths)
    {
        if (string.IsNullOrWhiteSpace(paths))
        {
            return new Result([], null);
        }

        var tokens = paths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var attachments = new List<OutboundAttachment>(tokens.Length);
        long total = 0;

        foreach (var token in tokens)
        {
            var path = token.Trim().Trim('"');
            if (path.Length == 0)
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex)
            {
                return Failure($"Attachment path '{path}' is not usable: {ex.Message}");
            }

            if (!info.Exists)
            {
                return Failure($"Attachment not found: '{path}'.");
            }

            total += info.Length;
            if (total > MaxTotalBytes)
            {
                return Failure(
                    $"Attachments total {total / (1024.0 * 1024.0):F1} MB, over Gmail's 25 MB limit. " +
                    "Send fewer or smaller files, or share a link instead.");
            }

            byte[] content;
            try
            {
                content = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                return Failure($"Could not read attachment '{path}': {ex.Message}");
            }

            attachments.Add(new OutboundAttachment
            {
                Filename = info.Name,
                MimeType = MimeTypeFor(info.Name),
                Content = content,
            });
        }

        return new Result(attachments, null);
    }

    private static Result Failure(string error) => new([], error);

    private static string MimeTypeFor(string filename)
    {
        var extension = Path.GetExtension(filename);
        return MimeTypesByExtension.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }
}
