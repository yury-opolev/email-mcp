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
| `setup_gmail` | Stores the Google OAuth Client ID and Client Secret used by every account - set once, not per account. Values are encrypted and stored locally. With no accounts configured, also creates one called `default`. Running it again rotates the client for every account; the response names any accounts whose stored sign-in is no longer valid |
| `auth_status` | Checks authentication status for one account. If not configured, explains how to set it up. If configured but not authenticated, initiates the OAuth flow. Supports `forceReauth` to start a fresh session. Run this first before using any other email tools |
| `list_emails` | Lists recent emails from the inbox. Optionally filter by label ID (e.g., `INBOX`, `SENT`, `DRAFT`). Returns email ID, subject, sender, date, and snippet |
| `read_email` | Reads a specific email by message ID. Returns full email content including body, headers, attachments info, and labels |
| `search_emails` | Searches emails using Gmail search syntax (e.g., `from:john subject:meeting after:2025/01/01`). Supports individual field filters: from, to, subject, date range, and label |
| `list_labels` | Lists all email labels/folders available in the account. Returns label IDs and names |
| `send_email` | Sends an email from the authenticated account. Supports plain text, HTML, or both (multipart/alternative), plus file attachments. Accepts comma-separated `to`/`cc`/`bcc` lists, with optional display names (`"Name <addr@example.com>"`). Requires the GmailSend OAuth scope, see Scopes section below |
| `reply_to_email` | Replies to an existing message, **properly threaded**. Takes a message ID and a body; the recipient, subject and `In-Reply-To`/`References` headers are derived from the original. Use this rather than `send_email` with a `Re:` subject — see [Replying](#replying) |
| `create_draft` | Saves an email as a draft in the mailbox without sending it, so it can be reviewed and sent by hand from the mail client. Same arguments as `send_email`. Requires the GmailCompose OAuth scope |
| `revoke_auth` | Fully revokes the OAuth token with Google and deletes locally stored tokens for one account. Does not remove stored client credentials |

### Account management

| Tool | Description |
|------|-------------|
| `add_account` | Adds an account under a short alias. Takes no credentials - every account uses the shared Client ID and Secret set by `setup_gmail`. Does not sign in; run `auth_status` for that alias afterwards |
| `list_accounts` | Lists configured accounts: alias, real address once known, which is default, and whether signed in. Never returns secrets |
| `set_default_account` | Chooses the account used when no `account` argument is given |
| `rename_account` | Renames an account, moving its stored sign-in with it |
| `remove_account` | Removes an account and its stored sign-in. Revokes the Google grant by default; pass `revokeRemote=false` to keep it |

## Multiple accounts

One server can manage several mailboxes. All accounts share the one Google OAuth client, set
once with `setup_gmail`; each account is just a short alias and its own encrypted token.

```
add_account("studio")
auth_status(account: "studio")   # sign in as the studio address

send_email(to: "...", subject: "...", body: "...", account: "studio")
list_emails()                     # uses the default account
```

**With exactly one account configured, nothing changes.** That account is always used, whatever
the default is set to, and the `account` argument can be ignored entirely. Existing single-account
setups are migrated automatically on first start and stay signed in.

Re-running `setup_gmail` with a different Client ID replaces the client for every account at
once, since it is shared. The response names any accounts whose stored sign-in is no longer
valid; run `auth_status` for each of them to sign in again.

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

## Replying

Use `reply_to_email`, not `send_email` with a hand-typed `Re:` subject.

The difference is invisible in Gmail and obvious everywhere else. Gmail infers conversations from
subject and participants, so a new message titled `Re: …` appears to thread correctly when you
check your own sent folder. Almost every other client — Outlook, Thunderbird, Apple Mail — threads
strictly on the `In-Reply-To` and `References` headers, and shows a subject-only "reply" as an
unrelated message dropped into the inbox.

`reply_to_email` takes the message ID and a body. Everything else is derived from the original:

- **Recipient** — the original sender. `replyAll` additionally Ccs the original's To and Cc,
  minus your own address. It is off by default, because reply-all is the more damaging mistake.
- **Subject** — the original's, prefixed `Re: ` unless it already carries a reply prefix.
  Non-English prefixes (`AW:`, `SV:`, `Rif:`, `Res:` …) are recognised, so replies to German or
  Italian correspondents do not come back as `Re: AW: …`.
- **Threading** — `In-Reply-To` is the original's `Message-ID`; `References` is the original's
  chain with that ID appended. Gmail's `threadId` is set too, so the sent copy files into the
  same conversation in your own mailbox.

The original's attachments are **not** carried over — that is forwarding, not replying.
Forwarding is not implemented yet.

## Scopes

The server requests three Gmail OAuth scopes:

- `gmail.readonly` — required by `list_emails`, `read_email`, `search_emails`, `list_labels`
- `gmail.send` — required by `send_email`
- `gmail.compose` — required by `create_draft`. `gmail.send` permits sending but **not** saving a draft, so drafts need their own scope

Scopes are granted at sign-in, and a stored token is never upgraded in place. If you authenticated against an older build that requested fewer scopes, run `revoke_auth` and then `auth_status` for that account to re-grant consent before the newer tools will succeed. Both tools report an HTTP 403 with this instruction if you hit it.

## Quick Start

1. **Prerequisites**: .NET 10 SDK, a Google Cloud project with Gmail API enabled
2. **Setup**: Get a Google OAuth Client ID and Secret, then use them with the `setup_gmail` tool once your MCP client is connected (step 4). See [docs/SETUP.md](docs/SETUP.md) for detailed instructions
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
5. **Add a second account (optional)** - the Client ID and Secret from step 2 are shared, so a
   new alias needs no credentials of its own, just its own sign-in:
   ```
   add_account("studio")
   auth_status(account: "studio")   # sign in as the studio address
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

## Upgrading a local install

When the server is registered with an MCP client that launches it from `publish/`, a plain
`dotnet publish -o publish` fails with **MSB3027**: the running server holds the assemblies,
and Windows will not overwrite a mapped image. Use the install script instead — it publishes
to a staging directory and swaps the files in without needing the client to be closed:

```powershell
pwsh scripts/install-local.ps1
```

Then restart the MCP client. Sessions already running keep executing the code they mapped at
launch, so nothing changes for them until they relaunch the server.

The swap works because Windows *does* allow renaming a mapped image even though it refuses to
delete or overwrite one. Each locked file is renamed to `<name>.locked-<stamp>` and the new
file is copied into the path it vacated, so a valid assembly sits at every path throughout —
a lazily loaded assembly resolves to the new copy rather than a hole. The `.locked-*` strays
are deleted by the next run, once the processes holding them have exited.

The script records the install time in `publish/.install-local-stamp` and reports any server
process that started *before* it — those are the ones still executing stale code, and an old
orphan among them is usually what keeps the `.locked-*` files pinned. A process started after
the stamp is already running the current build, so it is not reported.

Useful flags:

| Flag | Effect |
|---|---|
| `-DryRun` | Report what would change, touch nothing |
| `-SkipBuild` | Install from an existing `-StagingPath` instead of publishing |
| `-StagingPath` / `-TargetPath` | Override the staging and install directories |
| `-Configuration` | Build configuration, defaults to `Release` |

## License

BSD 3-Clause — see [LICENSE](LICENSE) for details.
