# Email MCP Server — Setup Guide

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- A Google account
- A web browser for OAuth consent

---

## Step 1: Google Cloud Console Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. **Create a new project** (or select an existing one)
3. **Enable the Gmail API**:
   - Navigate to **APIs & Services → Library**
   - Search for "Gmail API"
   - Click **Enable**
4. **Create OAuth credentials**:
   - Navigate to **APIs & Services → Credentials**
   - Click **Create Credentials → OAuth client ID**
   - If prompted, configure the OAuth consent screen first:
     - Choose **External** user type
     - Fill in app name (e.g., "Email MCP")
     - Add your email as a test user
   - Application type: **Desktop app**
   - Name: "Email MCP" (or any name)
   - Click **Create**
5. **Download the credentials**:
   - Click the download icon next to your newly created OAuth client
   - Save the file as `credentials.json`
   - Move it to `~/.email-mcp/credentials.json`

> **⚠️ Security**: Never commit `credentials.json` to version control. It's already in `.gitignore`.

---

## Step 2: Configure Credentials

You have **two options**:

### Option A: Interactive Setup via MCP (Recommended)

Once the server is connected to your MCP client, simply use the tools:

1. Call `auth_status` — it will tell you credentials are not configured
2. The AI will ask for your **Client ID** and **Client Secret**
3. Call `setup_gmail` with those values — they're encrypted and stored locally
4. Call `auth_status` again — opens browser for Google sign-in

No files to create or move! Everything is handled through the chat.

### Option B: Manual credentials.json file

1. Download `credentials.json` from Google Cloud Console (OAuth client → Download)
2. Place it at `~/.email-mcp/credentials.json`

---

## Step 3: Build the Project

```bash
cd /path/to/email-mcp
dotnet build
```

---

## Step 3: First Run & Authentication

Run the MCP server directly to test authentication:

```bash
dotnet run --project src/EmailMcp.Server
```

On first use, the `auth_status` tool will:
1. Open your default browser
2. Ask you to sign in to your Google account
3. Request permission to read your emails (read-only)
4. Store the OAuth token (encrypted) in `~/.email-mcp/tokens/`

---

## Step 4: Configure Your MCP Client

### GitHub Copilot CLI

Add to your MCP configuration file (the path depends on your Copilot CLI setup):

**Windows**: `%USERPROFILE%\.copilot\github-copilot-cli\mcp.json`
**macOS/Linux**: `~/.copilot/github-copilot-cli/mcp.json`

```json
{
  "mcpServers": {
    "email-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Users\\yurio\\Documents\\github\\email-mcp\\src\\EmailMcp.Server"]
    }
  }
}
```

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

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

### Cursor

Add to `~/.cursor/mcp.json`:

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

### VS Code (Copilot Chat)

Add to your VS Code settings (`.vscode/settings.json` or user settings):

```json
{
  "github.copilot.chat.mcp.servers": {
    "email-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/email-mcp/src/EmailMcp.Server"]
    }
  }
}
```

> **Tip**: For faster startup, publish the project first and reference the compiled executable:
> ```bash
> dotnet publish src/EmailMcp.Server -c Release -o ./publish
> ```
> Then use `"command": "./publish/EmailMcp.Server"` (or `EmailMcp.Server.exe` on Windows).

---

## Available Tools

Once configured, the following tools are available to your AI assistant:

| Tool | Description |
|------|-------------|
| `setup_gmail` | Configure Gmail credentials interactively |
| `auth_status` | Check/initiate Gmail authentication |
| `list_emails` | List recent emails (with optional label filter) |
| `read_email` | Read a specific email by ID (full body content) |
| `search_emails` | Search emails using Gmail query syntax |
| `list_labels` | List all available labels/folders |

### Usage Examples

Ask your AI assistant:
- *"Check my email auth status"*
- *"List my 10 most recent emails"*
- *"Search for emails from alice@example.com"*
- *"Read email with ID 18f2a3b4c5d"*
- *"Show me unread emails in my inbox"*
- *"List my email labels"*

---

## Security

### Token Encryption

OAuth tokens are encrypted at rest using platform-native mechanisms:

| Platform | Encryption Method |
|----------|------------------|
| **Windows** | DPAPI (Data Protection API) — tied to your Windows user account |
| **Linux/macOS** | ASP.NET Data Protection with file-system key storage |

- Encrypted tokens: `~/.email-mcp/tokens/`
- Encryption keys: `~/.email-mcp/keys/`

### Permissions

The server requests **read-only** Gmail access by default (`GmailService.Scope.GmailReadonly`). It cannot send, delete, or modify emails.

### Revoking Access

To revoke the server's access to your Gmail:

1. Use the `auth_status` tool with `forceReauth: true`
2. Or visit [Google Account Security → Third-party apps](https://myaccount.google.com/permissions) and remove "Email MCP"
3. Or delete the token files: `rm -rf ~/.email-mcp/tokens/`

---

## Configuration

### Custom Credentials Path

Set in `appsettings.json` or environment variable:

```json
{
  "Gmail": {
    "CredentialsPath": "/custom/path/to/credentials.json"
  }
}
```

Or via environment variable:
```bash
Gmail__CredentialsPath=/custom/path/to/credentials.json
```

---

## Troubleshooting

### "Credentials file not found"
Ensure `credentials.json` is at `~/.email-mcp/credentials.json` or set the custom path in configuration.

### "Authentication failed"
1. Check that Gmail API is enabled in Google Cloud Console
2. Verify your email is listed as a test user (for unverified apps)
3. Try `auth_status` with `forceReauth: true` to re-authenticate

### "Token expired / invalid"
Delete tokens and re-authenticate:
```bash
# Windows
del %USERPROFILE%\.email-mcp\tokens\*
# Linux/macOS
rm ~/.email-mcp/tokens/*
```

### Server doesn't start
Check stderr output for error details. The MCP server logs to stderr (stdout is reserved for MCP protocol).

### Rate limiting (429 errors)
Gmail API has quotas. If you hit rate limits, wait a few seconds before retrying. The daily quota is typically 250 units/user/second.
