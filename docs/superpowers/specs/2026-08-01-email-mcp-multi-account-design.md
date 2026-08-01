# email-mcp: multi-account support

Design agreed 2026-08-01.

## Goal

Let one email-mcp server manage several email accounts, while a single-account setup keeps
behaving exactly as it does today. The driving case: sending outreach from a studio address
(`spacekubegames`) while a personal Gmail stays configured on the same machine.

Today the server is hard-wired to exactly one account. `GmailAuthenticator` bakes in
`UserId = "user"` and two fixed token keys, and `IEmailProvider` / `IEmailAuthenticator` are
registered as singletons that tools inject directly.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Account selection | Optional `account` parameter on every tool | Stateless. Each call states which account it means, so there is no hidden "current account" that can silently send mail from the wrong address. Omitting it keeps every existing call working. |
| Account identity | Short alias, real address recorded after auth | The address is not known until OAuth completes, so it cannot be the setup-time identifier. The alias is short to type and stable across re-auth; the recorded address makes a mis-bound alias visible. |
| Client credentials | Stored per account, always | Uniform with no fallback logic, and each account is fully isolated. Costs re-pasting credentials for accounts that share a Cloud project. |
| Legacy account | Auto-migrated once at startup | The existing session stays authenticated; nothing to redo. |
| Per-account resolution | Account registry handing out cached per-alias instances | Matches the existing code, which already caches a credential per authenticator instance. Leaves `IEmailProvider` / `IEmailAuthenticator` signatures and the whole Gmail provider layer untouched. |

Rejected: a `use_account` mode switch (hidden state, wrong-sender risk); threading `account`
through every interface method (churns every implementation and test, loses the credential
cache); .NET keyed DI (keys must be known at container build time, but aliases are created at
runtime).

## Storage

`ITokenStore` is unchanged. The account index is simply another key in it.

```
accounts--index                        { "defaultAlias": "studio",
                                         "accounts": [
                                           { "alias": "default", "email": "...", "createdUtc": "..." },
                                           { "alias": "studio",  "email": null,  "createdUtc": "..." }
                                         ] }
account--{alias}--client-credentials   per-account Client ID + Secret
account--{alias}--oauth-token-user     per-account OAuth token
```

### Why `--` and not `:`

`DataProtectionTokenStore.GetFilePath` replaces characters that are invalid in filenames, and
`:` is invalid on Windows. `account:studio:token` would land on disk as
`account_studio_token.enc`. That is safe only while the alias charset excludes `_`; if the
charset were ever widened, two aliases could sanitise to the same filename and silently
overwrite each other's tokens.

`--` survives sanitisation unchanged, so key isolation does not depend on the validation rule.
A test pins this: a key built for an alias must round-trip to a distinct file.

### Encryption is unchanged

Values go through `IDataProtector.Protect()` (purpose `EmailMcp.TokenStore.v1`) into
`~/.email-mcp/tokens/{key}.enc`. The Data Protection key ring lives in `~/.email-mcp/keys` and
on Windows is protected with `ProtectKeysWithDpapi(protectToLocalMachine: false)`, i.e.
current-user scope. Per-account namespacing adds more keys under the same protection; no crypto
changes.

**Known gap, out of scope here:** on Linux and macOS no `ProtectKeysWith*` is applied, so the
key ring is guarded only by filesystem permissions. Worth a separate issue.

## Alias rules

Lowercase letters, digits and hyphens; 1–32 characters; must not start or end with a hyphen.
Reserved: `index`. Aliases are compared case-sensitively after being lowercased on input.

## Default resolution

In order:

1. `account` supplied → use it. An unknown alias is an **error listing the known aliases**.
   Never auto-create: a typo that silently creates an empty account is exactly the failure that
   sends mail from the wrong place.
2. `account` omitted and **exactly one account exists** → that account, whatever the index says.
   This is single-account mode: with one account configured, every existing call behaves as it
   does today.
3. `account` omitted and several exist → `defaultAlias`. If unset, error asking which.

## Migration

Once, at startup, before any tool runs:

- If `accounts--index` exists, do nothing.
- Else if legacy `gmail-client-credentials` exists: copy it to
  `account--default--client-credentials`, copy `gmail-oauth-token-user` to
  `account--default--oauth-token-user` if present, write an index containing the single alias
  `default` with `defaultAlias: "default"`, then delete the two legacy keys.
- Else do nothing (fresh install).

Idempotent: a crash before the index is written leaves no index, so the next start simply
repeats the copy, which is overwrite-safe.

## Tool surface

Seven existing tools gain an optional `account` parameter and are otherwise untouched:
`auth_status`, `revoke_auth`, `list_emails`, `read_email`, `search_emails`, `list_labels`,
`send_email`.

`setup_gmail` keeps its current behaviour, targeting the default account, so existing
documentation and habits do not break.

New account-management tools:

| Tool | Op | Behaviour |
|---|---|---|
| `add_account(alias, clientId, clientSecret, setDefault?)` | C | Validates the alias, rejects duplicates, stores credentials, adds to the index. Does **not** authenticate; `auth_status(account)` does, as today. |
| `list_accounts()` | R | alias, email, isDefault, isAuthenticated. Never returns secrets. |
| `set_default_account(alias)` | U | |
| `rename_account(alias, newAlias)` | U | Re-keys both stored secrets, updates the index, moves the default flag if it pointed at the old alias. |
| `update_account_credentials(alias, clientId, clientSecret)` | U | Replaces client credentials. Leaves the OAuth token alone, which may then be invalid; the response says so. |
| `remove_account(alias, revokeRemote?)` | D | `revokeRemote` defaults **true**, matching existing `revoke_auth`: deleting locally while leaving the Google grant live is a security wart. Deletes both keys and the index entry. If the removed account was default and exactly one account remains, that one becomes default. |

## Components

**EmailMcp.Abstractions** (new)
- `EmailAccount` — `Alias`, `EmailAddress?`, `CreatedUtc`
- `IAccountRegistry` — `ListAsync`, `AddAsync`, `RemoveAsync`, `RenameAsync`, `SetDefaultAsync`,
  `UpdateCredentialsAsync`, `ResolveAliasAsync(string? requested)`,
  `GetProviderAsync(string? account)`, `GetAuthenticatorAsync(string? account)`
- `IEmailProvider` and `IEmailAuthenticator` signatures are **unchanged**.

**EmailMcp.Security** (new)
- `AccountIndexStore` — reads and writes `accounts--index` through `ITokenStore`, serialising
  the index as JSON. Single writer; no concurrency guarantees beyond the process.

**EmailMcp.Gmail** (changed)
- `GmailAccountRegistry : IAccountRegistry` — owns the index store, performs migration on first
  use, and caches one `GmailAuthenticator` + `GmailEmailProvider` per alias.
- `GmailAuthenticator` — takes an alias in its constructor. The `UserId` constant and the two
  key constants become alias-derived. Its private `EncryptedDataStore.NormalizeKey` becomes
  alias-aware.
- After a successful `AuthenticateAsync`, fetch `users.getProfile` and record the address on the
  account. Failure to fetch is logged and ignored; it must not fail authentication.
- `GmailEmailProvider`, `GmailMapper`, `MimeBuilder` — **no changes**.

**EmailMcp.Server** (changed)
- Tools inject `IAccountRegistry` instead of `IEmailProvider` / `IEmailAuthenticator`.
- New tool classes for the six account-management tools.

## Error handling

All errors keep the existing shape: `{ "Success": false, "Error": "..." }`.

- Unknown alias → lists known aliases.
- No accounts configured → tells the user to run `add_account`.
- Several accounts, none requested, no default → asks which, listing aliases.
- Invalid alias → states the rule.
- Duplicate alias on add → names the conflict.
- The existing Gmail 403 insufficient-scope message is preserved and gains the account alias, so
  it is clear which account needs re-consent.

## Testing

**Correction to an earlier claim in this spec:** existing tests do *not* all pass unchanged.
Tool tests call tools directly, e.g. `AuthStatusTool.AuthStatus(authMock.Object)`, so changing a
tool's first parameter from `IEmailAuthenticator` to `IAccountRegistry` necessarily breaks them.
They are updated mechanically via a `TestRegistry` helper that returns a registry resolving to a
single fixed account, which keeps the assertions themselves untouched. `SetupGmailToolTests` is
rewritten rather than patched, because `setup_gmail` no longer writes the token store itself and
its credential validation moved into the registry.

Abstractions, Gmail and Security tests are unaffected.

New tests:
- Alias validation: accepted and rejected forms, including leading/trailing hyphen and the
  reserved `index`.
- Key building: distinct aliases produce distinct sanitised filenames (pins the `--` decision).
- Index round-trip through a fake `ITokenStore`.
- Default resolution across all three rules, including zero accounts and several-without-default.
- Migration: legacy keys present, index absent → migrated and legacy keys deleted; running twice
  changes nothing; fresh install is a no-op.
- Unknown alias produces the listing error rather than creating an account.
- Registry returns the same instance for a repeated alias.
- `remove_account` of the default with one remaining promotes the survivor.

No network in tests. `ITokenStore` is faked, following the existing test style.

## Out of scope

- Non-Windows key-ring protection.
- Concurrent access from multiple server processes to one token directory.
- Providers other than Gmail, though the registry interface does not preclude them.
- Per-account OAuth scopes; `GmailOptions.Scopes` stays global.
