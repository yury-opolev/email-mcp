namespace EmailMcp.Abstractions;

/// <summary>
/// Represents an email attachment metadata.
/// </summary>
public sealed class EmailAttachment
{
    public required string Filename { get; init; }
    public required string MimeType { get; init; }
    public long Size { get; init; }
    public string? AttachmentId { get; init; }
}
