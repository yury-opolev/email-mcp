# Email MCP Server — Implementation Plan

## Problem Statement

Build a cross-platform, local MCP (Model Context Protocol) server in C# (.NET 10) that integrates with Gmail via Google's official API. The server exposes email operations (read, search, list, etc.) as MCP tools that AI assistants (e.g., GitHub Copilot CLI) can invoke. Sensitive data at rest (OAuth tokens) must be encrypted using platform-native mechanisms (DPAPI on Windows, ASP.NET Data Protection on Linux/macOS). The design must be extensible so additional email providers (Outlook, Yahoo, etc.) can be added in the future.

## Proposed Approach

- Use the **official C# MCP SDK** (`ModelContextProtocol` NuGet package) with stdio transport for local use.
- Use **Google.Apis.Gmail.v1** for Gmail integration with OAuth 2.0 authorization code flow.
- Use **ASP.NET Core Data Protection** (`Microsoft.AspNetCore.DataProtection`) for encryption at rest — it automatically uses DPAPI on Windows and key-file-based encryption on Linux/macOS.
- Follow **clean architecture** with clear separation of concerns: abstractions, provider implementations, encryption, and MCP tool layer.
- Target **.NET 10** (latest LTS-track, installed on machine).

---

## Solution Structure

```
email-mcp/
├── docs/
│   ├── PLAN.md                          # This plan
│   └── SETUP.md                         # User-facing setup guide
├── src/
│   ├── EmailMcp.Abstractions/           # Interfaces & models (provider-agnostic)
│   │   ├── IEmailProvider.cs            # Core email provider interface
│   │   ├── IEmailAuthenticator.cs       # Authentication abstraction
│   │   ├── ITokenStore.cs              # Token persistence abstraction
│   │   ├── Models/
│   │   │   ├── EmailMessage.cs          # Email message model
│   │   │   ├── EmailAddress.cs          # Email address value object
│   │   │   ├── EmailAttachment.cs       # Attachment model
│   │   │   ├── EmailSearchQuery.cs      # Search criteria model
│   │   │   └── EmailLabel.cs            # Label/folder model
│   │   └── EmailMcp.Abstractions.csproj
│   │
│   ├── EmailMcp.Security/              # Encryption & token storage
│   │   ├── DataProtectionTokenStore.cs  # Encrypts tokens via Data Protection API
│   │   ├── ServiceCollectionExtensions.cs
│   │   └── EmailMcp.Security.csproj
│   │
│   ├── EmailMcp.Gmail/                 # Gmail provider implementation
│   │   ├── GmailEmailProvider.cs        # IEmailProvider for Gmail
│   │   ├── GmailAuthenticator.cs        # OAuth 2.0 flow for Gmail
│   │   ├── GmailMapper.cs              # Maps Gmail API models → EmailMessage
│   │   ├── ServiceCollectionExtensions.cs
│   │   └── EmailMcp.Gmail.csproj
│   │
│   └── EmailMcp.Server/               # MCP server host (entry point)
│       ├── Program.cs                   # Host builder & DI setup
│       ├── Tools/
│       │   ├── ListEmailsTool.cs        # MCP tool: list emails
│       │   ├── ReadEmailTool.cs         # MCP tool: read single email
│       │   ├── SearchEmailsTool.cs      # MCP tool: search emails
│       │   ├── ListLabelsTool.cs        # MCP tool: list labels/folders
│       │   └── AuthStatusTool.cs        # MCP tool: check auth status
│       ├── appsettings.json             # Configuration
│       └── EmailMcp.Server.csproj
│
├── tests/
│   ├── EmailMcp.Abstractions.Tests/     # Model & interface tests
│   ├── EmailMcp.Security.Tests/         # Token store encryption tests
│   ├── EmailMcp.Gmail.Tests/            # Gmail provider unit tests (mocked)
│   └── EmailMcp.Server.Tests/           # MCP tool integration tests
│
├── email-mcp.sln                        # Solution file
├── .gitignore
├── README.md                            # Project overview & quick start
└── LICENSE
```

---

## Workplan

### Phase 1: Project Scaffolding
- [x] Create solution file and all project skeletons (.csproj files)
- [x] Add NuGet package references to each project
- [x] Create `.gitignore` for .NET
- [x] Create `README.md` with project overview

### Phase 2: Abstractions Layer (`EmailMcp.Abstractions`)
- [x] Define `IEmailProvider` interface (ListEmails, GetEmail, SearchEmails, ListLabels)
- [x] Define `IEmailAuthenticator` interface (Authenticate, IsAuthenticated, RevokeAuth)
- [x] Define `ITokenStore` interface (SaveToken, LoadToken, DeleteToken)
- [x] Create model classes: `EmailMessage`, `EmailAddress`, `EmailAttachment`, `EmailSearchQuery`, `EmailLabel`

### Phase 3: Security Layer (`EmailMcp.Security`)
- [x] Implement `DataProtectionTokenStore` using ASP.NET Data Protection API
- [x] Add DI extension method `AddEmailSecurity()`
- [x] Write unit tests for token store (encrypt/decrypt round-trip)

### Phase 4: Gmail Provider (`EmailMcp.Gmail`)
- [x] Implement `GmailAuthenticator` with OAuth 2.0 authorization code flow
- [x] Implement `GmailEmailProvider`
- [x] Implement `GmailMapper` to convert Gmail API types to `EmailMessage`
- [x] Add DI extension method `AddGmailProvider()`
- [x] Write unit tests with mocked Gmail API service

### Phase 5: MCP Server (`EmailMcp.Server`)
- [x] Configure `Program.cs` with Generic Host, DI, logging to stderr
- [x] Wire up MCP server with stdio transport
- [x] Implement MCP tools (list_emails, read_email, search_emails, list_labels, auth_status)
- [x] Add `appsettings.json` with configurable options
- [x] Write integration tests for MCP tools

### Phase 6: Documentation
- [x] Create `docs/SETUP.md`
- [x] Update `README.md` with badges, feature list, quick start

### Phase 7: Final Validation
- [x] Ensure all projects build successfully
- [x] Ensure all unit tests pass (41/41)
- [x] Verify MCP server starts and responds to tool listing
- [ ] Test end-to-end with Copilot CLI config (manual step)

---

## Key Design Decisions

### 1. Provider Abstraction
All email operations go through `IEmailProvider`, making it trivial to add Outlook, Yahoo, or IMAP providers later without touching the MCP tool layer.

### 2. Encryption at Rest
Using `Microsoft.AspNetCore.DataProtection`:
- **Windows**: Automatically uses DPAPI (user-scope) — tokens are encrypted with the Windows user's credentials.
- **Linux/macOS**: Uses file-system-based key storage in `~/.aspnet/DataProtection-Keys/`. Keys are protected by file-system permissions. For additional security, the `ProtectKeysWithDpapi()` call is conditionally applied only on Windows.
- Token files are stored in `~/.email-mcp/tokens/` directory.

### 3. OAuth 2.0 Flow
- Uses Google's authorization code flow with PKCE.
- Opens the user's default browser for consent.
- Spins up a temporary local HTTP listener to receive the callback.
- Refresh tokens are persisted (encrypted) for subsequent runs.

### 4. stdio Transport
The MCP server uses stdio transport (not HTTP), which is the standard for local MCP servers invoked by CLI tools. The host process starts the server as a child process and communicates via stdin/stdout.

### 5. Clean Code Principles
- **Single Responsibility**: Each class has one job (e.g., `GmailMapper` only maps, `DataProtectionTokenStore` only stores).
- **Dependency Inversion**: All dependencies are on abstractions (`IEmailProvider`, `ITokenStore`), not concrete implementations.
- **Interface Segregation**: Small, focused interfaces rather than one large interface.
- **Open/Closed**: New providers can be added without modifying existing code.
- **Testability**: All external dependencies are behind interfaces and injectable via DI.

---

## NuGet Packages

| Project | Package | Purpose |
|---------|---------|---------|
| Abstractions | _(none)_ | Pure interfaces & models |
| Security | `Microsoft.AspNetCore.DataProtection` | Cross-platform encryption |
| Gmail | `Google.Apis.Gmail.v1` | Gmail API client |
| Gmail | `Google.Apis.Auth` | OAuth 2.0 authentication |
| Server | `ModelContextProtocol` (prerelease) | MCP SDK |
| Server | `Microsoft.Extensions.Hosting` | Generic Host |
| Tests | `xunit` | Test framework |
| Tests | `Moq` | Mocking framework |
| Tests | `FluentAssertions` | Assertion library |
| Tests | `Microsoft.NET.Test.Sdk` | Test runner |

---

## MCP Config Sample (GitHub Copilot CLI)

```json
{
  "mcpServers": {
    "email-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Users\\yurio\\Documents\\github\\email-mcp\\src\\EmailMcp.Server"],
      "env": {}
    }
  }
}
```

Or, after publishing:

```json
{
  "mcpServers": {
    "email-mcp": {
      "command": "C:\\Users\\yurio\\Documents\\github\\email-mcp\\src\\EmailMcp.Server\\bin\\Release\\net10.0\\EmailMcp.Server.exe",
      "args": [],
      "env": {}
    }
  }
}
```

---

## Notes & Considerations

1. **Google Cloud Console setup is required** — The user must create a Google Cloud project, enable the Gmail API, and download OAuth client credentials (`credentials.json`). This is documented in `SETUP.md`.
2. **Scopes** — Default scope is `Gmail.Readonly` for safety. Send/modify scopes can be added later.
3. **Rate limits** — Gmail API has a daily quota (250 quota units/user/second). The provider should handle `429 Too Many Requests` gracefully.
4. **Token refresh** — Google OAuth tokens expire after ~1 hour. The `GmailAuthenticator` handles automatic refresh using the stored refresh token.
5. **Future providers** — To add Outlook: create `EmailMcp.Outlook` project implementing `IEmailProvider`, register it in DI. No changes to `EmailMcp.Server` needed.
6. **Security warning** — The `credentials.json` file from Google should never be committed to git. It's listed in `.gitignore`.
