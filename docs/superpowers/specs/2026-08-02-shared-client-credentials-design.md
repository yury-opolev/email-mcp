# email-mcp: one shared OAuth client for all accounts

Design agreed 2026-08-02. Supersedes the "client credentials stored per account" decision in
[2026-08-01-email-mcp-multi-account-design.md](2026-08-01-email-mcp-multi-account-design.md).

## Goal

Store the Google OAuth Client ID and Secret once, for the whole server, instead of once per
account.

A Google OAuth client identifies the *application*, not the user. One client can authorise any
number of Google accounts: you run the consent flow again while signed in as the other account.
Per-account storage therefore made the user paste the same secret for every account it applied
to, which is every account in practice.

The driving case is unchanged: send Clock Time outreach from a `spacekubegames` studio address
while a personal Gmail stays configured on the same machine.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Credential scope | One shared pair, no per-account override | Simplest model that matches reality. An override would add a second place a credential can live and a precedence rule to remember, to serve a case that has not arisen. |
| `setup_gmail` | Stores the shared pair; creates a `default` account only when none exist | Keeps the two-step first run, so the README quick start stays true. Adding a second account needs no secrets. |
| `add_account` | Takes an alias, no credentials | There is nothing per-account left to supply. |
| `update_account_credentials` | Deleted | Meaningless once credentials are not per account. It shipped the same morning and nothing depends on it. |
| Rotation | Keep existing tokens, report which accounts need re-auth | Re-entering the same credentials is normal. Destroying every account's authentication as a side effect of a no-op call would be a bad surprise. |
| OAuth tokens | Stay per account, unchanged | Tokens genuinely are per account. Only the client credential was ever shared in substance. |

Rejected: **shared with per-account override**. It covers a real but hypothetical case, a
second Cloud project such as a Workspace-Internal app for the studio domain. If that case
arrives, the override can be added then, and the shared key stays the default either way. Cost
now is a precedence rule in every credential read, plus the tool surface to manage it.

## Storage

`ITokenStore` is unchanged. One key is added and one stops being written.

```
shared--client-credentials             Client ID + Secret, the only copy      (NEW)
accounts--index                        { "defaultAlias": ..., "accounts": [...] }
account--{alias}--oauth-token-user     per-account OAuth token
account--{alias}--client-credentials   NO LONGER WRITTEN, read once by migration, then deleted
```

The `--` separator convention and its reason carry over unchanged from the multi-account
design: token-store keys become filenames, `:` is invalid on Windows, and `--` survives
filename sanitisation so key isolation does not depend on the alias validation rule.

`AccountKeys.ClientCredentials(alias)` is renamed `LegacyAccountClientCredentials(alias)` and
kept solely so migration can read and delete the old keys.

## Migration

Runs at startup in `AccountIndexStore`, alongside the existing legacy migration, and is
idempotent. The cases are ordered; the first that matches wins.

| On disk | Action |
|---|---|
| `shared--client-credentials` exists | Nothing. Already migrated. |
| `gmail-client-credentials` (pre-multi-account) | Promote straight to the shared key. Previously this went to `account--default--client-credentials`. |
| `account--{alias}--client-credentials` | Promote the **default** account's copy to the shared key, then delete every per-account copy. |

If several per-account copies exist and the default account has none, promote the first account
in index order. This cannot happen through any supported flow, but the rule must be total.

OAuth tokens are never touched by this migration, so an already-authenticated account stays
authenticated across the upgrade. That is the property to protect: the live install has
`account--default--client-credentials` and `account--default--oauth-token-user`, and the user
must not have to re-authenticate.

## Tool surface

| Tool | Change |
|---|---|
| `setup_gmail(clientId, clientSecret)` | Stores the shared pair. Creates a `default` account when no accounts exist. No longer errors when several accounts exist, since that call is now a legitimate rotation. |
| `add_account(alias, setDefault?)` | Loses `clientId` and `clientSecret`. Fails with a message pointing at `setup_gmail` when no shared credentials are configured. |
| `update_account_credentials` | Removed. |
| `revoke_auth` | Behaviour unchanged. Its description text points at `update_account_credentials` and must be rewritten to point at `setup_gmail`. |
| `auth_status`, `list_accounts`, `list_emails`, `read_email`, `search_emails`, `list_labels`, `send_email`, `rename_account`, `set_default_account`, `remove_account` | Unchanged. |

First run, unchanged in shape:

```
setup_gmail(clientId, clientSecret)    stores shared credentials, creates "default"
auth_status()                          browser consent
```

Second account, no secrets involved:

```
add_account("studio")
auth_status(account: "studio")         browser consent, signed in as the studio address
```

## Interfaces

`IAccountRegistry`:

- `AddAccountAsync(alias, setDefault, ct)` loses `clientId` and `clientSecret`.
- `UpdateCredentialsAsync(alias, clientId, clientSecret, ct)` becomes
  `SetSharedCredentialsAsync(clientId, clientSecret, ct)`.
- `RemoveAccountAsync` stops deleting a per-account credential key.
- `RenameAccountAsync` stops moving one.

`GmailAuthenticator` keeps its `accountAlias`, which still derives the OAuth token key, but its
`clientCredentialsKey` becomes the shared constant rather than a value derived from the alias.
That single line is the substance of the change; everything else follows from it.

`IEmailProvider`, `IEmailAuthenticator`, `GmailEmailProvider`, `GmailMapper` and `MimeBuilder`
are untouched, as in the multi-account work.

### Deletion paths that must be re-pointed

Repointing `clientCredentialsKey` at a shared key silently converts every existing "delete this
account's credentials" call into "delete everyone's credentials". Both known call sites must be
handled in the same change, not left for a follow-up:

- **`GmailAccountRegistry.RemoveAccountAsync`** deletes
  `AccountKeys.ClientCredentials(alias)` today. It must delete only the OAuth token. Left as
  is, removing one account de-configures the whole server.
- **`GmailAuthenticator.DeleteClientCredentialsAsync`** deletes `clientCredentialsKey`. It is
  `internal` and currently has no call site, so it is dead code that would become a loaded gun.
  Delete the method rather than repoint it.

A regression test covers the first: remove one of two accounts, then assert the shared
credentials still exist and the surviving account still resolves.

Credential validation, currently in `GmailAccountRegistry.StoreClientCredentialsAsync`, moves to
the shared setter unchanged: both fields required, and the Client ID must contain
`.apps.googleusercontent.com`.

## Rotation

`setup_gmail` now affects every account at once. Tokens issued by the previous client stop
working, so the behaviour has to be deliberate:

1. Compare the incoming Client ID with the stored one.
2. Store the new pair either way.
3. Clear the cached authenticators and providers, so the next call picks the new credential up.
4. When the Client ID **changed**, list in the response the accounts that are currently
   authenticated and will therefore need `auth_status` run again.

Stored OAuth tokens are not deleted. A user re-entering the same credentials loses nothing, and
a user genuinely rotating gets told exactly what to fix rather than discovering it later.

## Testing

- `AccountKeysTests`: the shared key survives filename sanitisation unchanged; the per-account
  credential key is no longer produced by any non-legacy path.
- Migration tests, one per row of the table above, plus idempotency: running twice is a no-op,
  and OAuth tokens survive.
- Registry tests: `add_account` refused when no shared credentials exist; rename and remove
  leave credentials alone; rotation clears the caches; a changed Client ID is detected and an
  unchanged one is not.
- `SetupGmailToolTests` updated for the new behaviour, including rotation with several accounts
  configured.
- `UpdateAccountCredentialsToolTests` deleted.

The suite is 111 tests before this work. Expect it to shrink slightly where per-account
credential handling is removed, and grow on migration coverage.

## Documentation

The README sections "Account management", "Multiple accounts" and "Quick Start" all describe
per-account credentials and need rewriting against the flows above.
