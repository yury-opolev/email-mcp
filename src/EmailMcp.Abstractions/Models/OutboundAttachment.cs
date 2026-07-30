namespace EmailMcp.Abstractions;

/// <summary>
/// A file to attach to an outbound message.
/// </summary>
/// <remarks>
/// Distinct from <see cref="EmailAttachment"/>, which describes an attachment on a
/// message that was <em>received</em> and carries only metadata plus an id used to
/// fetch the bytes later. This type carries the bytes themselves, because they must
/// be encoded into the MIME message at send time.
/// </remarks>
public sealed class OutboundAttachment
{
    /// <summary>Name shown to the recipient. May contain non-ASCII characters.</summary>
    public required string Filename { get; init; }

    /// <summary>MIME type, e.g. <c>application/pdf</c>.</summary>
    public required string MimeType { get; init; }

    /// <summary>The file's raw bytes.</summary>
    public required byte[] Content { get; init; }
}
