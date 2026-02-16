# Email MCP Server

A cross-platform MCP (Model Context Protocol) server that provides email integration tools for AI assistants. Built with .NET 10 and designed for extensibility.

## Features

- **Gmail Integration** — Read, search, and list emails via Google's official Gmail API
- **Secure Token Storage** — OAuth tokens encrypted at rest using DPAPI (Windows) or Data Protection keys (Linux/macOS)
- **MCP Protocol** — Exposes email operations as tools consumable by GitHub Copilot CLI, Claude, Cursor, and other MCP clients
- **Extensible Architecture** — Provider-based design makes it easy to add Outlook, Yahoo, or IMAP support
- **Cross-Platform** — Runs on Windows, Linux, and macOS

## Available Tools

| Tool | Description |
|------|-------------|
| `setup_gmail` | Sets up Gmail credentials (Client ID + Secret) — values are encrypted and stored locally |
| `auth_status` | Checks authentication status. If not configured, instructs to run `setup_gmail`. If configured but not authenticated, initiates OAuth flow. Supports `forceReauth` to start a fresh session. Run this first before using any other email tools |
| `list_emails` | Lists recent emails from the inbox. Optionally filter by label ID (e.g., `INBOX`, `SENT`, `DRAFT`). Returns email ID, subject, sender, date, and snippet |
| `read_email` | Reads a specific email by message ID. Returns full email content including body, headers, attachments info, and labels |
| `search_emails` | Searches emails using Gmail search syntax (e.g., `from:john subject:meeting after:2025/01/01`). Supports individual field filters: from, to, subject, date range, and label |
| `list_labels` | Lists all email labels/folders available in the account. Returns label IDs and names |
| `revoke_auth` | Fully revokes the OAuth token with Google and deletes locally stored tokens. Does not remove stored client credentials |

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
