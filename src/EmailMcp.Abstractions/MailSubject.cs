namespace EmailMcp.Abstractions;

/// <summary>
/// Subject-line conventions for replies and forwards.
/// </summary>
public static class MailSubject
{
    private const string ReplyPrefix = "Re: ";
    private const string ForwardPrefix = "Fwd: ";

    /// <summary>
    /// Prefixes a subject with <c>Re: </c>, unless it already carries a reply prefix.
    ///
    /// Mail clients do not stack these — a five-message exchange is "Re: Subject", never
    /// "Re: Re: Re: Re: Subject" — so an existing prefix is left alone. Recognises the common
    /// non-English forms too, because a reply to a French or German correspondent will come
    /// back carrying theirs and stacking a "Re:" on top of "AW:" looks careless.
    /// </summary>
    public static string EnsureReplyPrefix(string? subject)
    {
        var trimmed = (subject ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            // An empty original subject is legal, if unusual. "Re: " alone reads as broken,
            // so send a bare "Re:" rather than a prefix with nothing after it.
            return "Re:";
        }

        return HasPrefix(trimmed, ["re:", "aw:", "sv:", "vs:", "antw:", "r:", "rif:", "res:"])
            ? trimmed
            : ReplyPrefix + trimmed;
    }

    /// <summary>
    /// Prefixes a subject with <c>Fwd: </c> unless it already carries a forward prefix.
    /// Not yet used by any tool; kept alongside <see cref="EnsureReplyPrefix"/> so the two
    /// conventions live together when forwarding lands.
    /// </summary>
    public static string EnsureForwardPrefix(string? subject)
    {
        var trimmed = (subject ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return "Fwd:";
        }

        return HasPrefix(trimmed, ["fwd:", "fw:", "wg:", "tr:", "rv:", "i:", "enc:"])
            ? trimmed
            : ForwardPrefix + trimmed;
    }

    private static bool HasPrefix(string subject, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
