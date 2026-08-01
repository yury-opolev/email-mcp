# Email MCP Server

A cross-platform MCP (Model Context Protocol) server that provides email integration tools for AI assistants. Built with .NET 10 and designed for extensibility.

## Features

- **Gmail Integration** — Read, search, and list emails via Google's official Gmail API
- **Secure Token Storage** — OAuth tokens encrypted at rest using DPAPI (Windows) or Data Protection keys (Linux/macOS)
- **MCP Protocol** — Exposes email operations as tools consumable by GitHub Copilot CLI, Claude, Cursor, and other MCP clients
- **Extensible Architecture** — Provider-based design makes it easy to add Outlook, Yahoo, or IMAP support
- **Cross-Platform** — Runs on Windows, Linux, and macOS

## Available Tools

### Email tools

Every tool below takes an optional `account` argument naming which configured account to use.
Omit it to use the default. See [Multiple accounts](#multiple-accounts).

| Tool | Description |
|------|-------------|
| `setup_gmail` | Sets up Gmail credentials (Client ID + Secret) for a single-account setup - values are encrypted and stored locally. Creates the `default` account, or replaces credentials when exactly one account exists |
| `auth_status` | Checks authentication status for one account. If not configured, explains how to set it up. If configured but not authenticated, initiates the OAuth flow. Supports `forceReauth` to start a fresh session. Run this first before using any other email tools |
| `list_emails` | Lists recent emails from the inbox. Optionally filter by label ID (e.g., `INBOX`, `SENT`, `DRAFT`). Returns email ID, subject, sender, date, and snippet |
| `read_email` | Reads a specific email by message ID. Returns full email content including body, headers, attachments info, and labels |
| `search_emails` | Searches emails using Gmail search syntax (e.g., `from:john subject:meeting after:2025/01/01`). Supports individual field filters: from, to, subject, date range, and label |
| `list_labels` | Lists all email labels/folders available in the account. Returns label IDs and names |
| `send_email` | Sends an email from the authenticated account. Supports plain text, HTML, or both (multipart/alternative), plus file attachments. Accepts comma-separated `to`/`cc`/`bcc` lists, with optional display names (`"Name <addr@example.com>"`). Requires the GmailSend OAuth scope, see Scopes section below |
| `revoke_auth` | Fully revokes the OAuth token with Google and deletes locally stored tokens for one account. Does not remove stored client credentials |

### Account management

| Tool | Description |
|------|-------------|
| `add_account` | Adds an account under a short alias with its own Client ID and Secret. Does not sign in; run `auth_status` for that alias afterwards |
| `list_accounts` | Lists configured accounts: alias, real address once known, which is default, and whether signed in. Never returns secrets |
| `set_default_account` | Chooses the account used when no `account` argument is given |
| `rename_account` | Renames an account, moving its credentials and sign-in with it |
| `update_account_credentials` | Replaces the Client ID and Secret for an account |
| `remove_account` | Removes an account and its stored secrets. Revokes the Google grant by default; pass `revokeRemote=false` to keep it |

## Multiple accounts

One server can manage several mailboxes. Each account has a short alias you choose, its own
OAuth client credentials, and its own encrypted token.

```
add_account(alias: "studio", clientId: "...", clientSecret: "...")
auth_status(account: "studio")

send_email(to: "...", subject: "...", body: "...", account: "studio")
list_emails()                     # uses the default account
```

**With exactly one account configured, nothing changes.** That account is always used, whatever
the default is set to, and the `account` argument can be ignored entirely. Existing single-account
setups are migrated automatically on first start and stay signed in.

Which account gets used when `account` is omitted:

1. Exactly one account configured, that one.
2. Several configured, whichever `set_default_account` chose.
3. Several configured with no default, an error asking which, rather than a guess.

An unknown alias is an error listing the known ones. It never silently creates an account, since
a typo that created an empty account would be a good way to send mail from the wrong address.

Aliases are lowercase letters, digits and hyphens, up to 32 characters. That charset is not
cosmetic: aliases become part of token-store keys and therefore filenames.

### Attachments

`send_email` takes an optional `attachments` parameter: a comma-separated list of
**local file paths** on the machine running the server.

```
attachments: "C:\reports\q3.pdf, C:\images\chart.png"
```

Paths rather than inline content, because the server runs alongside the client and
pushing megabytes of base64 through the MCP protocol would be wasteful.

The MIME type is inferred from the file extension, falling back to
`application/octet-stream`. With attachments present the message is built as
`multipart/mixed`, with the body as the first part — nested as
`multipart/alternative` when both `body` and `bodyHtml` are supplied.

**Gmail rejects messages over 25 MB**, so the combined size of the attachments is
checked up front and the tool returns a clear error rather than letting the API fail
after the upload. Note this checks the raw bytes; base64 adds roughly 33%, so the
practical ceiling is lower. For anything larger, share a link instead.

## Scopes

The server requests two Gmail OAuth scopes:

- `gmail.readonly` — required by `list_emails`, `read_email`, `search_emails`, `list_labels`
- `gmail.send` — required by `send_email`

If you previously authenticated against an older build that only requested `gmail.readonly`, you must run `revoke_auth` and then `auth_status` to re-grant consent with the broader scope before `send_email` will succeed.

## Quick Start

1. **Prerequisites**: .NET 10 SDK, a Google Cloud project with Gmail API enabled
2. **Setup**: See [docs/SETUP.md](docs/SETUP.md) for detailed instructions
3. **Run**:
   ```bash
   dotnet run --project src/EmailMcp.Server
   ```
4. **Configure your MCP client** — add to your client's MCP config:
   ```json
   {
     "mcpServers": {
       "email-mcp": {
         "command": "dotnet",
         "args": ["run", "--project", "/path/to/email-mcp/src/EmailMcp.Server"]
       }
     }
   }
   ```

## Project Structure

```
src/
├── EmailMcp.Abstractions/  # Interfaces & models (provider-agnostic)
├── EmailMcp.Security/      # Encrypted token storage
├── EmailMcp.Gmail/         # Gmail API provider
└── EmailMcp.Server/        # MCP server host & tools
tests/
├── EmailMcp.Abstractions.Tests/
├── EmailMcp.Security.Tests/
├── EmailMcp.Gmail.Tests/
└── EmailMcp.Server.Tests/
```

## Building

```bash
dotnet build
```

## Testing

```bash
dotnet test
```

## License

BSD 3-Clause — see [LICENSE](LICENSE) for details.
