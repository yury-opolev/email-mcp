namespace EmailMcp.Server.Tools;

/// <summary>
/// The shared description for the optional <c>account</c> parameter carried by every email tool.
/// </summary>
internal static class AccountParameter
{
    public const string Description =
        "Which configured account to use, by alias (e.g. 'studio'). " +
        "Omit to use the default account. With exactly one account configured, " +
        "that account is always used and this can be ignored. " +
        "Use 'list_accounts' to see what is configured.";
}
