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
| `setup_gmail` | Configure Gmail credentials (Client ID + Secret) — encrypted locally |
| `list_emails` | List recent emails with optional label filter |
| `read_email` | Read a specific email by ID |
| `search_emails` | Search emails using query syntax |
| `list_labels` | List available email labels/folders |
| `auth_status` | Check authentication status |

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

MIT
