using EmailMcp.Abstractions;

namespace EmailMcp.Server.Tools;

/// <summary>
/// Parses the comma-separated recipient lists that the mail-composing tools accept, in either
/// bare (<c>addr@example.com</c>) or display-name (<c>Name &lt;addr@example.com&gt;</c>) form.
/// </summary>
internal static class AddressListParser
{
    /// <summary>
    /// Splits a comma-separated recipient list. Tokens that cannot be parsed are dropped, so a
    /// caller that needs at least one recipient must check the returned count.
    /// </summary>
    public static IReadOnlyList<EmailAddress> Parse(string? input)
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
}
