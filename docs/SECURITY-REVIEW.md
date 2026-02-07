---
Security Audit Report — EmailMcp
Overview
Project: Email MCP Server — a .NET 10 MCP server providing AI assistants with Gmail read-only access via stdio transport.
Dependency Scan: No known vulnerable NuGet packages detected. All dependencies are up-to-date.
Overall Assessment: The codebase has a low-to-moderate security risk profile. No critical vulnerabilities were found. The architecture is well-designed with clean abstractions, encrypted token storage, and read-only Gmail scope by default. However, there are several medium-severity issues worth addressing.
---
CRITICAL Findings: 0
None.
---
MEDIUM Severity Findings: 9
1. Race Condition on Singleton Credential — GmailAuthenticator.cs:110-126
GmailAuthenticator is a singleton with a mutable _credential field and no synchronization. Concurrent MCP tool calls could both see _credential as null and launch duplicate browser auth flows, or both call RefreshTokenAsync simultaneously.
Recommendation: Add a SemaphoreSlim to serialize credential access:
private readonly SemaphoreSlim _lock = new(1, 1);
2. Race Condition on Singleton GmailService — GmailEmailProvider.cs:140-153
Same pattern — _service field is lazily initialized without synchronization. Could create duplicate GmailService instances, leaking the first.
Recommendation: Same SemaphoreSlim pattern or Lazy<Task<GmailService>>.
3. IsAuthenticatedAsync Doesn't Check Token Validity — GmailAuthenticator.cs:40-46
Returns true if a credential object exists or a token file exists on disk, without checking if the token is expired, revoked, or corrupted. Leads to auth_status tool reporting "authenticated" for stale tokens.
Recommendation: Check _credential.Token.IsStale when the credential exists. When checking the file, attempt to load and validate.
4. No Exception Handling in Tool Methods — All tool files
ReadEmailTool, ListEmailsTool, SearchEmailsTool, and ListLabelsTool have no try-catch. If the Gmail API throws (401, 403, 404, 429), full exception details (including stack traces) propagate to the MCP client.
Recommendation: Add try-catch for GoogleApiException in each tool, returning structured JSON errors instead.
5. Broad Exception Catch Swallows All Errors — GmailAuthenticator.cs:87-92
catch (Exception ex) { _logger.LogError(...); return false; }
Catches all exceptions including OutOfMemoryException, losing the root cause. Caller gets a generic "Authentication failed" with no actionable detail.
Recommendation: Catch only expected types (TokenResponseException, HttpRequestException, IOException) and propagate the error reason.
6. Filesystem Path Exposed in Exception — GmailAuthenticator.cs:148-151
FileNotFoundException message includes the full credentials path (e.g., C:\Users\yurio\.email-mcp\credentials.json), revealing username and directory structure to the MCP client.
Recommendation: Use a generic message; log the path at Debug level only.
7. Gmail Query Injection — GmailEmailProvider.cs:120-138
Structured search fields (From, To, Subject) are interpolated directly into the Gmail query without escaping. Passing from:"alice subject:secret" injects additional operators. While impact is limited to the user's own mailbox, this is an unvalidated input pattern relevant in LLM prompt-injection scenarios.
Recommendation: Quote field values:
parts.Add($"from:(\"{query.From}\")");
8. Token File Permissions Not Set on Unix — DataProtectionTokenStore.cs:44
Encrypted token files are written with default permissions (typically 644 — world-readable). Other users on the machine could read the encrypted blobs.
Recommendation: On Linux/macOS, set 600 permissions:
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
9. Data Protection Keys Unprotected on Linux/macOS — ServiceCollectionExtensions.cs:28-31
On Windows, DPAPI provides user-scoped key protection. On Linux/macOS, master encryption keys are stored as plaintext XML in ~/.email-mcp/keys/. Any process running as the same user can read these and decrypt all tokens.
Recommendation: At minimum, set restrictive directory/file permissions. Consider ProtectKeysWithCertificate() for stronger protection. Document this limitation.
---
LOW Severity Findings: 7
| # | Finding | Location | Recommendation |
|---|---------|----------|----------------|
| 1 | ReadEmailTool has no messageId null/empty check at tool level | ReadEmailTool.cs:14 | Add validation returning structured JSON error |
| 2 | SetupGmailTool uses Contains instead of EndsWith for client ID format | SetupGmailTool.cs:34 | Use EndsWith(".apps.googleusercontent.com") |
| 3 | Invalid dates silently ignored, returning unfiltered results | SearchEmailsTool.cs:56-65 | Return error when date string can't be parsed |
| 4 | No string length limits on search parameters | SearchEmailsTool.cs:17-24 | Add reasonable max lengths (e.g., 1000 chars) |
| 5 | Token revocation doesn't reach Google when _credential is null | GmailAuthenticator.cs:95-105 | Load credential from store before attempting remote revoke |
| 6 | Path traversal incomplete in key sanitization (no canonicalization) | DataProtectionTokenStore.cs:91-94 | Add Path.GetFullPath check ensuring path stays within _storageDirectory |
| 7 | Decryption failure silently returns null | DataProtectionTokenStore.cs:59-68 | Throw a specific exception to give actionable feedback |
---
INFORMATIONAL Findings
| # | Finding | Notes |
|---|---------|-------|
| 1 | Test files use realistic-looking fake credentials (GOCSPX-secret123) | Not real secrets, but consider adding comments for clarity |
| 2 | publish/ directory with PDB files tracked in git | PDB files contain local paths; use CI/CD releases instead |
| 3 | .gitignore does not include .env | Add .env and .env.* as preventive measure |
| 4 | Debug-level logging of search queries could leak PII | Acceptable for local tool; redact if logs are shipped remotely |
| 5 | GmailOptions.Scopes is mutable (set accessor) | Consider init-only to prevent runtime scope escalation |
| 6 | Log threshold set to Trace (all messages to stderr) | Consider Information as default for production |
---
Dependency Security
| Check | Result |
|-------|--------|
| Known vulnerable packages | None found |
| Outdated packages | ModelContextProtocol 0.8.0-preview.1 (latest available at sources) |
| No .env files committed | Confirmed |
| No real secrets in source code | Confirmed |
| .gitignore covers credentials | Confirmed (credentials.json, client_secret*.json, *.pfx, *.key, .email-mcp/) |
---
Priority Recommendations (Top 5)
1. Add SemaphoreSlim synchronization to GmailAuthenticator.GetCredentialAsync and GmailEmailProvider.GetServiceAsync to prevent race conditions on the singleton services.
2. Add try-catch blocks in all MCP tool methods to catch GoogleApiException and return structured JSON errors instead of leaking stack traces.
3. Set restrictive file permissions (600) on token and key files when running on Linux/macOS.
4. Escape/quote Gmail query parameters in BuildGmailQuery to prevent query operator injection, especially relevant for LLM prompt-injection defense.
5. Narrow exception handling in GmailAuthenticator.AuthenticateAsync — catch specific exception types and propagate actionable error information.