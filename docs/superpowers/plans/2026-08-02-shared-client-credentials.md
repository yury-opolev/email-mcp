# Shared OAuth Client Credentials Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store the Google OAuth Client ID and Secret once for the whole server instead of once per account.

**Architecture:** A Google OAuth client identifies the application, not the user, so one client authorises any number of accounts. The substance of the change is one line: `GmailAuthenticator.clientCredentialsKey` stops deriving from the account alias and becomes a shared constant. Everything else is consequence: a startup migration that promotes the existing per-account copy, two deletion paths that must stop deleting the now-shared key, and a tool surface that no longer asks for secrets per account. OAuth tokens stay per account and are never touched.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, Moq, ASP.NET Data Protection, Google.Apis.Auth.

**Spec:** [`docs/superpowers/specs/2026-08-02-shared-client-credentials-design.md`](../specs/2026-08-02-shared-client-credentials-design.md)

## Global Constraints

- Follow the C# style already in this repo: `this.` on every instance member access, braces on every block, one type per file, file-scoped namespaces, `sealed` where not designed for inheritance, `ConfigureAwait(false)` in library code, `Async` suffix on async methods.
- Tests are xUnit with FluentAssertions. Use `NullLogger<T>.Instance` and `NullLoggerFactory.Instance`.
- Never delete a stored OAuth token as a side effect of a credential operation. An already-authenticated account must stay authenticated through this entire change.
- The live install has `account--default--client-credentials` and `account--default--oauth-token-user` on disk. The migration must carry that install forward without a re-authentication.
- `IEmailProvider`, `IEmailAuthenticator`, `GmailEmailProvider`, `GmailMapper` and `MimeBuilder` are not modified.
- Run the full suite with `dotnet test` from the repo root. It is 111 tests before this work.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/EmailMcp.Abstractions/AccountKeys.cs` | Token-store key names | Add shared key, rename per-account credential key to legacy |
| `src/EmailMcp.Abstractions/Models/CredentialUpdateResult.cs` | Result of setting shared credentials | Create |
| `src/EmailMcp.Abstractions/AccountIndexStore.cs` | Index persistence and startup migration | Promote credentials to the shared key |
| `src/EmailMcp.Abstractions/IAccountRegistry.cs` | Registry contract | `AddAccountAsync` loses two parameters; `UpdateCredentialsAsync` becomes `SetSharedCredentialsAsync`; add `AreSharedCredentialsConfiguredAsync` |
| `src/EmailMcp.Gmail/GmailCredentialsPath.cs` | Resolve the credentials.json fallback path | Create, shared by authenticator and registry |
| `src/EmailMcp.Gmail/GmailAuthenticator.cs` | OAuth for one account | Read the shared credential key; delete dead `DeleteClientCredentialsAsync` |
| `src/EmailMcp.Gmail/GmailAccountRegistry.cs` | Owns accounts, hands out cached instances | Shared credential storage, rotation detection, stop touching credentials on remove/rename |
| `src/EmailMcp.Server/Tools/SetupGmailTool.cs` | Set credentials, create first account | Rewrite |
| `src/EmailMcp.Server/Tools/AddAccountTool.cs` | Add an account | Drop credential parameters |
| `src/EmailMcp.Server/Tools/UpdateAccountCredentialsTool.cs` | Per-account credential replacement | Delete |
| `src/EmailMcp.Server/Tools/RevokeAuthTool.cs` | Revoke one account | Description text only |
| `README.md` | User documentation | Rewrite three sections |

---

### Task 1: Shared credential key

**Files:**
- Modify: `src/EmailMcp.Abstractions/AccountKeys.cs`
- Modify: `src/EmailMcp.Gmail/GmailAuthenticator.cs:45`
- Modify: `src/EmailMcp.Gmail/GmailAccountRegistry.cs:119,152-154,348`
- Modify: `src/EmailMcp.Abstractions/AccountIndexStore.cs:83`
- Test: `tests/EmailMcp.Abstractions.Tests/AccountKeysTests.cs`

**Interfaces:**
- Produces: `AccountKeys.SharedClientCredentials` (const string, value `"shared--client-credentials"`), `AccountKeys.LegacyAccountClientCredentials(string alias)` (replaces `AccountKeys.ClientCredentials`).

This task is a rename plus a new constant. Behaviour does not change and every existing test must still pass.

- [ ] **Step 1: Write the failing test**

In `tests/EmailMcp.Abstractions.Tests/AccountKeysTests.cs`, replace the `Keys_ContainNoCharactersThatFilenameSanitisationWouldReplace` test body and add one new test:

```csharp
    [Fact]
    public void Keys_ContainNoCharactersThatFilenameSanitisationWouldReplace()
    {
        var keys = new[]
        {
            AccountKeys.Index,
            AccountKeys.SharedClientCredentials,
            AccountKeys.LegacyAccountClientCredentials("studio"),
            AccountKeys.OAuthToken("studio"),
            AccountKeys.OAuthTokenPrefix("studio"),
        };

        foreach (var key in keys)
        {
            AsFilename(key).Should().Be(key, "keys must survive filename sanitisation unchanged");
        }
    }

    [Fact]
    public void SharedClientCredentialsKey_DoesNotCollideWithAnyAccountKey()
    {
        AccountKeys.SharedClientCredentials
            .Should().NotBe(AccountKeys.OAuthToken("shared"))
            .And.NotBe(AccountKeys.LegacyAccountClientCredentials("shared"))
            .And.NotBe(AccountKeys.Index);
    }
```

Also update `ClientCredentialsAndToken_DoNotCollideForTheSameAlias` to call `AccountKeys.LegacyAccountClientCredentials("studio")`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/EmailMcp.Abstractions.Tests --filter AccountKeysTests`
Expected: FAIL to compile, `'AccountKeys' does not contain a definition for 'SharedClientCredentials'`.

- [ ] **Step 3: Write minimal implementation**

In `src/EmailMcp.Abstractions/AccountKeys.cs`, add the shared key and rename the per-account accessor:

```csharp
    /// <summary>
    /// Key holding the one Client ID and Secret shared by every account. A Google OAuth client
    /// identifies the application, not the user, so one client authorises any number of accounts.
    /// </summary>
    public const string SharedClientCredentials = "shared--client-credentials";

    /// <summary>
    /// Key that held client credentials for one account, before they were shared. Read and
    /// deleted by migration; never written.
    /// </summary>
    public static string LegacyAccountClientCredentials(string alias) =>
        $"account--{alias}--client-credentials";
```

Delete the old `ClientCredentials(string alias)` method. Then fix the four call sites so the solution compiles, changing `AccountKeys.ClientCredentials(` to `AccountKeys.LegacyAccountClientCredentials(` in:
- `GmailAuthenticator.cs:45`
- `GmailAccountRegistry.cs:119` (in `RemoveAccountAsync`)
- `GmailAccountRegistry.cs:152-154` (both arguments in `RenameAccountAsync`)
- `GmailAccountRegistry.cs:348` (in `StoreClientCredentialsAsync`)
- `AccountIndexStore.cs:83`

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: PASS, 112 tests (111 plus the new collision test).

- [ ] **Step 5: Commit**

```bash
git add src/EmailMcp.Abstractions/AccountKeys.cs src/EmailMcp.Gmail src/EmailMcp.Abstractions/AccountIndexStore.cs tests/EmailMcp.Abstractions.Tests/AccountKeysTests.cs
git commit -m "refactor: name the shared client-credential key, mark the per-account one legacy"
```

---

### Task 2: Migrate credentials to the shared key

**Files:**
- Modify: `src/EmailMcp.Abstractions/AccountIndexStore.cs`
- Test: `tests/EmailMcp.Abstractions.Tests/AccountIndexStoreTests.cs`

**Interfaces:**
- Consumes: `AccountKeys.SharedClientCredentials`, `AccountKeys.LegacyAccountClientCredentials(alias)` from Task 1.
- Produces: `AccountIndexStore.LoadAsync` guarantees that, after it returns, client credentials live at `AccountKeys.SharedClientCredentials` and nowhere else.

The existing `MigrateLegacyIfNeededAsync` handles the pre-multi-account keys and returns early when an index exists. Credential promotion must run in **both** cases, so it is a second, independent migration step.

- [ ] **Step 1: Write the failing tests**

Add to `tests/EmailMcp.Abstractions.Tests/AccountIndexStoreTests.cs`:

```csharp
    [Fact]
    public async Task Load_PromotesPerAccountCredentialsToTheSharedKey()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.Index, """
            {"DefaultAlias":"studio","Accounts":[
              {"Alias":"personal","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"},
              {"Alias":"studio","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"}]}
            """);
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("personal"), "PERSONAL");
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("studio"), "STUDIO");
        await store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials))
            .Should().Be("STUDIO", "the default account's copy wins");
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials("personal")))
            .Should().BeFalse();
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials("studio")))
            .Should().BeFalse();
        (await store.LoadTokenAsync(AccountKeys.OAuthToken("studio")))
            .Should().Be("TOKEN", "OAuth tokens are never touched by credential migration");
    }

    [Fact]
    public async Task Load_WithNoDefaultRecorded_PromotesTheFirstAccountsCredentials()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.Index, """
            {"DefaultAlias":null,"Accounts":[
              {"Alias":"personal","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"},
              {"Alias":"studio","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"}]}
            """);
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("studio"), "STUDIO");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("STUDIO");
    }

    [Fact]
    public async Task Load_MigratesPreMultiAccountCredentialsStraightToTheSharedKey()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "LEGACY");
        await store.SaveTokenAsync(AccountKeys.LegacyOAuthToken, "TOKEN");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        var index = await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("LEGACY");
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials(AccountKeys.LegacyAlias)))
            .Should().BeFalse("the per-account key is skipped entirely now");
        (await store.LoadTokenAsync(AccountKeys.OAuthToken(AccountKeys.LegacyAlias)))
            .Should().Be("TOKEN");
        index.Accounts.Should().ContainSingle(a => a.Alias == AccountKeys.LegacyAlias);
    }

    [Fact]
    public async Task Load_IsIdempotent()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "LEGACY");
        await store.SaveTokenAsync(AccountKeys.LegacyOAuthToken, "TOKEN");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("LEGACY");
        (await store.LoadTokenAsync(AccountKeys.OAuthToken(AccountKeys.LegacyAlias))).Should().Be("TOKEN");
    }

    [Fact]
    public async Task Load_WithSharedCredentialsAlreadyPresent_LeavesThemAlone()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.SharedClientCredentials, "SHARED");
        await store.SaveTokenAsync(AccountKeys.Index, """
            {"DefaultAlias":"studio","Accounts":[
              {"Alias":"studio","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"}]}
            """);
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("studio"), "STALE");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("SHARED");
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials("studio")))
            .Should().BeFalse("stale per-account copies are cleaned up even when shared already exists");
    }
```

If `AccountIndexStoreTests.cs` does not already have them, add `using EmailMcp.Abstractions;`, `using FluentAssertions;` and `using Microsoft.Extensions.Logging.Abstractions;`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/EmailMcp.Abstractions.Tests --filter AccountIndexStoreTests`
Expected: FAIL. The promotion tests fail because nothing writes `SharedClientCredentials`.

- [ ] **Step 3: Write the implementation**

In `src/EmailMcp.Abstractions/AccountIndexStore.cs`, change `LoadAsync` to run both migrations, in order:

```csharp
    public async Task<AccountIndex> LoadAsync(CancellationToken cancellationToken = default)
    {
        await this.MigrateLegacyIfNeededAsync(cancellationToken).ConfigureAwait(false);

        var json = await this.tokenStore.LoadTokenAsync(AccountKeys.Index, cancellationToken).ConfigureAwait(false);
        var index = json is null ? new AccountIndex() : Deserialize(json, this.logger);

        await this.MigrateCredentialsToSharedIfNeededAsync(index, cancellationToken).ConfigureAwait(false);

        return index;
    }

    private static AccountIndex Deserialize(string json, ILogger<AccountIndexStore> logger)
    {
        try
        {
            return JsonSerializer.Deserialize<AccountIndex>(json, serializerOptions) ?? new AccountIndex();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Account index is corrupt; treating it as empty");
            return new AccountIndex();
        }
    }
```

In `MigrateLegacyIfNeededAsync`, change the credential write at line 83 to target the shared key, and drop the now-pointless per-account write:

```csharp
        await this.tokenStore.SaveTokenAsync(
            AccountKeys.SharedClientCredentials,
            legacyCredentials,
            cancellationToken).ConfigureAwait(false);
```

Add the new migration:

```csharp
    /// <summary>
    /// Promotes per-account client credentials to the single shared key, once.
    /// </summary>
    /// <remarks>
    /// The shared copy is written before any per-account copy is deleted, so a crash part-way
    /// leaves the credentials readable and the next run simply repeats the promotion.
    /// </remarks>
    private async Task MigrateCredentialsToSharedIfNeededAsync(
        AccountIndex index,
        CancellationToken cancellationToken)
    {
        if (index.Accounts.Count == 0)
        {
            return;
        }

        var hasShared = await this.tokenStore
            .ExistsAsync(AccountKeys.SharedClientCredentials, cancellationToken)
            .ConfigureAwait(false);

        if (!hasShared)
        {
            // The default account's copy wins. Falling back to index order keeps the rule total
            // for an index that records no default, which no supported flow produces.
            var preferred = index.Accounts.FirstOrDefault(a => a.Alias == index.DefaultAlias)
                ?? index.Accounts[0];

            var promoted = await this.tokenStore
                .LoadTokenAsync(
                    AccountKeys.LegacyAccountClientCredentials(preferred.Alias),
                    cancellationToken)
                .ConfigureAwait(false);

            promoted ??= await this.FindAnyAccountCredentialsAsync(index, cancellationToken)
                .ConfigureAwait(false);

            if (promoted is null)
            {
                return;
            }

            this.logger.LogInformation(
                "Promoting the client credentials of account '{Alias}' to the shared key",
                preferred.Alias);

            await this.tokenStore
                .SaveTokenAsync(AccountKeys.SharedClientCredentials, promoted, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var account in index.Accounts)
        {
            await this.tokenStore
                .DeleteTokenAsync(
                    AccountKeys.LegacyAccountClientCredentials(account.Alias),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string?> FindAnyAccountCredentialsAsync(
        AccountIndex index,
        CancellationToken cancellationToken)
    {
        foreach (var account in index.Accounts)
        {
            var value = await this.tokenStore
                .LoadTokenAsync(
                    AccountKeys.LegacyAccountClientCredentials(account.Alias),
                    cancellationToken)
                .ConfigureAwait(false);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: PASS. The five new tests pass and nothing regresses.

- [ ] **Step 5: Commit**

```bash
git add src/EmailMcp.Abstractions/AccountIndexStore.cs tests/EmailMcp.Abstractions.Tests/AccountIndexStoreTests.cs
git commit -m "feat: promote per-account client credentials to a single shared key at startup"
```

---

### Task 3: The shared credential, across the Gmail layer and the tools

**Files:**
- Create: `src/EmailMcp.Gmail/GmailCredentialsPath.cs`
- Create: `src/EmailMcp.Abstractions/Models/CredentialUpdateResult.cs`
- Modify: `src/EmailMcp.Abstractions/IAccountRegistry.cs`
- Modify: `src/EmailMcp.Gmail/GmailAuthenticator.cs`
- Modify: `src/EmailMcp.Gmail/GmailAccountRegistry.cs`
- Modify: `src/EmailMcp.Server/Tools/SetupGmailTool.cs`
- Modify: `src/EmailMcp.Server/Tools/AddAccountTool.cs`
- Modify: `src/EmailMcp.Server/Tools/RevokeAuthTool.cs`
- Delete: `src/EmailMcp.Server/Tools/UpdateAccountCredentialsTool.cs`
- Test: `tests/EmailMcp.Gmail.Tests/GmailAccountRegistryTests.cs`
- Test: `tests/EmailMcp.Server.Tests/SetupGmailToolTests.cs`

> **Why this is one task and not two.** Changing `IAccountRegistry` breaks every tool that calls
> it, so a commit that stops at the Gmail layer leaves the solution not building. The tools move
> in the same commit so the tree is green at every boundary. There is no
> `UpdateAccountCredentialsToolTests.cs` to delete: that tool shipped without tool tests.

**Interfaces:**
- Consumes: `AccountKeys.SharedClientCredentials` from Task 1.
- Produces:
  - `CredentialUpdateResult(bool ClientIdChanged, IReadOnlyList<string> AuthenticatedAccounts)`
  - `IAccountRegistry.AddAccountAsync(string alias, bool setDefault, CancellationToken ct = default)`
  - `IAccountRegistry.SetSharedCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default) -> Task<CredentialUpdateResult>`
  - `IAccountRegistry.AreSharedCredentialsConfiguredAsync(CancellationToken ct = default) -> Task<bool>`
  - `GmailCredentialsPath.Resolve(GmailOptions options) -> string`
  - Final tool surface: `setup_gmail(clientId, clientSecret)`, `add_account(alias, setDefault?)`, and no `update_account_credentials`.

This is the task that turns "delete this account's credentials" into "delete everyone's credentials" if done carelessly. Both deletion paths are handled here, with a regression test.

- [ ] **Step 1: Write the failing tests**

Add to `tests/EmailMcp.Gmail.Tests/GmailAccountRegistryTests.cs`:

```csharp
    [Fact]
    public async Task AddAccount_WithNoSharedCredentials_PointsAtSetupGmail()
    {
        var act = () => NewSubject().AddAccountAsync("studio", setDefault: false);

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*setup_gmail*");
    }

    [Fact]
    public async Task SetSharedCredentials_StoresOneCopyUnderTheSharedKey()
    {
        var subject = NewSubject();

        await subject.SetSharedCredentialsAsync(ClientId, Secret);

        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
        _store.Keys.Should().NotContain(k => k.EndsWith("--client-credentials", StringComparison.Ordinal)
            && k != AccountKeys.SharedClientCredentials);
    }

    [Fact]
    public async Task SetSharedCredentials_WithTheSameClientId_ReportsNoChange()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);

        var result = await subject.SetSharedCredentialsAsync(ClientId, "GOCSPX-rotated-secret");

        result.ClientIdChanged.Should().BeFalse();
    }

    [Fact]
    public async Task SetSharedCredentials_WithADifferentClientId_ListsAccountsNeedingReauth()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: true);
        await subject.AddAccountAsync("studio", setDefault: false);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        var result = await subject.SetSharedCredentialsAsync(
            "999-xyz.apps.googleusercontent.com",
            Secret);

        result.ClientIdChanged.Should().BeTrue();
        result.AuthenticatedAccounts.Should().BeEquivalentTo(["studio"]);
    }

    [Fact]
    public async Task RemoveAccount_LeavesTheSharedCredentialsIntact()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: true);
        await subject.AddAccountAsync("studio", setDefault: false);

        await subject.RemoveAccountAsync("studio", revokeRemote: false);

        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
        (await subject.ResolveAliasAsync(null)).Should().Be("personal");
    }

    [Fact]
    public async Task RenameAccount_MovesTheTokenButNotTheCredentials()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: true);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        await subject.RenameAccountAsync("studio", "work");

        (await _store.LoadTokenAsync(AccountKeys.OAuthToken("work"))).Should().Be("TOKEN");
        (await _store.ExistsAsync(AccountKeys.OAuthToken("studio"))).Should().BeFalse();
        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
    }

    [Fact]
    public async Task SetSharedCredentials_RejectsAClientIdThatIsNotAnOAuthClientId()
    {
        var act = () => NewSubject().SetSharedCredentialsAsync("my-project-id", Secret);

        (await act.Should().ThrowAsync<AccountException>())
            .WithMessage("*apps.googleusercontent.com*");
    }
```

Then update **every** existing call in this file of the form
`AddAccountAsync("x", ClientId, Secret, setDefault: b)` to `AddAccountAsync("x", setDefault: b)`, and add
`await subject.SetSharedCredentialsAsync(ClientId, Secret);` as the first line after `NewSubject()` in each of those tests, because adding an account now requires configured credentials.

Change `NewSubject()` so the credentials.json fallback cannot make the suite depend on the
machine it runs on. Without this, `AddAccount_WithNoSharedCredentials_PointsAtSetupGmail` passes
or fails according to whether the developer happens to have `~/.email-mcp/credentials.json`:

```csharp
    private GmailAccountRegistry NewSubject() => new(
        _store,
        new AccountIndexStore(_store, NullLogger<AccountIndexStore>.Instance),
        new GmailOptions { CredentialsPath = Path.Combine(Path.GetTempPath(), "email-mcp-tests-no-such-credentials.json") },
        NullLoggerFactory.Instance,
        NullLogger<GmailAccountRegistry>.Instance);
```

Also add a test for the fallback itself, so the behaviour is pinned rather than accidental:

```csharp
    [Fact]
    public async Task AddAccount_WithACredentialsFileButNoStoredPair_IsAllowed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"email-mcp-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{}");
        try
        {
            var subject = new GmailAccountRegistry(
                _store,
                new AccountIndexStore(_store, NullLogger<AccountIndexStore>.Instance),
                new GmailOptions { CredentialsPath = path },
                NullLoggerFactory.Instance,
                NullLogger<GmailAccountRegistry>.Instance);

            var act = () => subject.AddAccountAsync("studio", setDefault: false);

            await act.Should().NotThrowAsync();
        }
        finally
        {
            File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/EmailMcp.Gmail.Tests`
Expected: FAIL to compile, no `SetSharedCredentialsAsync` and wrong `AddAccountAsync` arity.

- [ ] **Step 3: Write the implementation**

Create `src/EmailMcp.Abstractions/Models/CredentialUpdateResult.cs`:

```csharp
namespace EmailMcp.Abstractions;

/// <summary>
/// What changed when the shared client credentials were set.
/// </summary>
/// <param name="ClientIdChanged">
/// True when the incoming Client ID differs from the one already stored. Tokens issued by the
/// previous client stop working, so this is what makes a rotation worth reporting.
/// </param>
/// <param name="AuthenticatedAccounts">
/// Accounts holding an OAuth token at the moment of the change, in index order. Only meaningful
/// when <paramref name="ClientIdChanged"/> is true.
/// </param>
public sealed record CredentialUpdateResult(
    bool ClientIdChanged,
    IReadOnlyList<string> AuthenticatedAccounts);
```

Create `src/EmailMcp.Gmail/GmailCredentialsPath.cs`:

```csharp
namespace EmailMcp.Gmail;

/// <summary>
/// Resolves the credentials.json fallback location, used when no credentials are in the token
/// store. Shared so the authenticator and the registry cannot disagree about where it lives.
/// </summary>
internal static class GmailCredentialsPath
{
    public static string Resolve(GmailOptions options) =>
        options.CredentialsPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".email-mcp",
                "credentials.json");
}
```

In `src/EmailMcp.Gmail/GmailAuthenticator.cs`:
- Change line 45 to `this.clientCredentialsKey = AccountKeys.SharedClientCredentials;`
- Replace the body of `ResolveCredentialsPath()` with `GmailCredentialsPath.Resolve(this.options);`
- **Delete `DeleteClientCredentialsAsync` entirely** (lines 142-148). It is `internal`, has no call site, and after this change would delete every account's credentials.
- In `LoadClientSecretsAsync`, change the `FileNotFoundException` message from "Use the 'add_account' tool to provide a Google OAuth Client ID and Client Secret" to "Run 'setup_gmail' with a Google OAuth Client ID and Client Secret".

In `src/EmailMcp.Abstractions/IAccountRegistry.cs`, change three members:

```csharp
    /// <summary>
    /// Adds an account. Does not authenticate, and does not take credentials: every account uses
    /// the shared client.
    /// </summary>
    /// <exception cref="AccountException">
    /// Thrown when the alias is invalid, already exists, or no shared credentials are configured.
    /// </exception>
    Task AddAccountAsync(string alias, bool setDefault, CancellationToken cancellationToken = default);

    /// <summary>Returns true once a shared Client ID and Secret are available.</summary>
    Task<bool> AreSharedCredentialsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the client credentials shared by every account. Stored OAuth tokens are left
    /// alone; the result says which accounts a changed Client ID has invalidated.
    /// </summary>
    Task<CredentialUpdateResult> SetSharedCredentialsAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);
```

Delete the old `UpdateCredentialsAsync` declaration.

In `src/EmailMcp.Gmail/GmailAccountRegistry.cs`:

```csharp
    public async Task AddAccountAsync(
        string alias,
        bool setDefault,
        CancellationToken cancellationToken = default)
    {
        var normalized = RequireValidAlias(alias);

        if (!await this.AreSharedCredentialsConfiguredAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new AccountException(
                "No OAuth client credentials are configured. Run 'setup_gmail' with your Google " +
                "OAuth Client ID and Secret first; every account shares them.");
        }

        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (index.Accounts.Any(a => a.Alias == normalized))
        {
            throw new AccountException(
                $"An account named '{normalized}' already exists. Pick another alias.");
        }

        index.Accounts.Add(new EmailAccount(normalized, EmailAddress: null, DateTimeOffset.UtcNow));

        if (setDefault || index.Accounts.Count == 1)
        {
            index.DefaultAlias = normalized;
        }

        await this.indexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Added account '{Alias}'", normalized);
    }

    public async Task<bool> AreSharedCredentialsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        if (await this.tokenStore
            .ExistsAsync(AccountKeys.SharedClientCredentials, cancellationToken)
            .ConfigureAwait(false))
        {
            return true;
        }

        return File.Exists(GmailCredentialsPath.Resolve(this.options));
    }

    public async Task<CredentialUpdateResult> SetSharedCredentialsAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        var previousClientId = await this.TryReadStoredClientIdAsync(cancellationToken)
            .ConfigureAwait(false);

        await this.StoreSharedCredentialsAsync(clientId, clientSecret, cancellationToken)
            .ConfigureAwait(false);

        // Every cached authenticator holds a credential built from the old client.
        this.authenticators.Clear();
        this.providers.Clear();

        var changed = previousClientId is not null
            && !string.Equals(previousClientId, clientId.Trim(), StringComparison.Ordinal);

        var authenticated = new List<string>();
        if (changed)
        {
            var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var account in index.Accounts)
            {
                if (await this.IsAuthenticatedAsync(account.Alias, cancellationToken).ConfigureAwait(false))
                {
                    authenticated.Add(account.Alias);
                }
            }
        }

        return new CredentialUpdateResult(changed, authenticated);
    }

    private async Task<string?> TryReadStoredClientIdAsync(CancellationToken cancellationToken)
    {
        var json = await this.tokenStore
            .LoadTokenAsync(AccountKeys.SharedClientCredentials, cancellationToken)
            .ConfigureAwait(false);

        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("installed", out var installed)
                && installed.TryGetProperty("client_id", out var id))
            {
                return id.GetString();
            }
        }
        catch (JsonException ex)
        {
            this.logger.LogWarning(ex, "Stored client credentials are not readable JSON");
        }

        return null;
    }
```

Rename `StoreClientCredentialsAsync` to `StoreSharedCredentialsAsync`, drop its `alias` parameter, and change its final write to `AccountKeys.SharedClientCredentials`. Its validation is unchanged.

Delete `UpdateCredentialsAsync`.

In `RemoveAccountAsync`, delete the two lines that remove `AccountKeys.LegacyAccountClientCredentials(normalized)` so only the OAuth token is deleted. In `RenameAccountAsync`, delete the `MoveTokenAsync` call for the credential keys so only the OAuth token moves.

Add `using System.Text.Json;` if not already present.

- [ ] **Step 4: Check the Gmail layer in isolation**

Run: `dotnet test tests/EmailMcp.Gmail.Tests tests/EmailMcp.Abstractions.Tests`
Expected: PASS.

`dotnet test` on its own still fails at this point, because `EmailMcp.Server` calls the registry
members that just changed. That is fixed in the next step, and nothing is committed until it is.

- [ ] **Step 5: Write the failing tool tests**

Replace `tests/EmailMcp.Server.Tests/SetupGmailToolTests.cs` with:

```csharp
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class SetupGmailToolTests
{
    private const string ClientId = "123456789-abc.apps.googleusercontent.com";
    private const string Secret = "GOCSPX-secret";

    private static Mock<IAccountRegistry> NewRegistry(
        IReadOnlyList<EmailAccount> accounts,
        CredentialUpdateResult result)
    {
        var registry = new Mock<IAccountRegistry>();
        registry
            .Setup(r => r.ListAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        registry
            .Setup(r => r.SetSharedCredentialsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        registry
            .Setup(r => r.AddAccountAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return registry;
    }

    [Fact]
    public async Task WithNoAccounts_StoresCredentialsAndCreatesDefault()
    {
        var registry = NewRegistry([], new CredentialUpdateResult(false, []));

        var response = await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        registry.Verify(r => r.AddAccountAsync(
            AccountKeys.LegacyAlias, true, It.IsAny<CancellationToken>()), Times.Once);
        response.Should().Contain("auth_status");
    }

    [Fact]
    public async Task WithAccountsAlready_DoesNotCreateAnother()
    {
        var accounts = new[] { new EmailAccount("studio", null, DateTimeOffset.UtcNow) };
        var registry = NewRegistry(accounts, new CredentialUpdateResult(false, []));

        await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        registry.Verify(r => r.AddAccountAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithSeveralAccounts_IsAllowedBecauseRotationIsLegitimate()
    {
        var accounts = new[]
        {
            new EmailAccount("personal", null, DateTimeOffset.UtcNow),
            new EmailAccount("studio", null, DateTimeOffset.UtcNow),
        };
        var registry = NewRegistry(accounts, new CredentialUpdateResult(false, []));

        var response = await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        response.Should().NotContain("cannot tell which one");
    }

    [Fact]
    public async Task WhenTheClientIdChanged_NamesTheAccountsNeedingReauth()
    {
        var accounts = new[] { new EmailAccount("studio", null, DateTimeOffset.UtcNow) };
        var registry = NewRegistry(accounts, new CredentialUpdateResult(true, ["studio"]));

        var response = await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        response.Should().Contain("studio").And.Contain("auth_status");
    }

    [Fact]
    public async Task WhenTheRegistryRejectsTheCredentials_ReturnsTheError()
    {
        var registry = new Mock<IAccountRegistry>();
        registry
            .Setup(r => r.ListAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        registry
            .Setup(r => r.SetSharedCredentialsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountException("Client ID doesn't look right."));

        var response = await SetupGmailTool.SetupGmail(registry.Object, "nope", Secret);

        response.Should().Contain("doesn't look right");
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/EmailMcp.Server.Tests --filter SetupGmailToolTests`
Expected: FAIL to compile.

- [ ] **Step 7: Write the tool implementations**

Replace the body of `SetupGmailTool.SetupGmail` and its description:

```csharp
    [McpServerTool(Name = "setup_gmail"), Description(
        "Stores the Google OAuth Client ID and Client Secret used by every account. " +
        "One OAuth client authorises any number of Google accounts, so this is set once, " +
        "not per account. With no accounts configured this also creates one called 'default'. " +
        "Running it again rotates the client for every account. " +
        "These values are encrypted and stored locally - they never leave your machine. " +
        "How to get these values: " +
        "1) Go to https://console.cloud.google.com " +
        "2) Create or select a project " +
        "3) Enable the Gmail API (APIs & Services -> Library -> search 'Gmail API' -> Enable) " +
        "4) Configure OAuth consent screen (APIs & Services -> OAuth consent screen -> External -> add your email as test user) " +
        "5) Create credentials (APIs & Services -> Credentials -> Create Credentials -> OAuth client ID -> Desktop app) " +
        "6) Copy the Client ID and Client Secret from the popup.")]
    public static async Task<string> SetupGmail(
        IAccountRegistry accounts,
        [Description("Google OAuth Client ID (looks like: 123456789-abc.apps.googleusercontent.com)")] string clientId,
        [Description("Google OAuth Client Secret")] string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await accounts.ListAccountsAsync(cancellationToken);
            var result = await accounts.SetSharedCredentialsAsync(clientId, clientSecret, cancellationToken);

            if (existing.Count == 0)
            {
                await accounts.AddAccountAsync(AccountKeys.LegacyAlias, setDefault: true, cancellationToken);

                return ToolResponse.Json(new
                {
                    Success = true,
                    Account = AccountKeys.LegacyAlias,
                    Message = "Credentials saved and encrypted, and the 'default' account was created. " +
                        "Now use the 'auth_status' tool to authenticate with your Google account. " +
                        "This will open a browser window for you to sign in.",
                });
            }

            if (result.ClientIdChanged && result.AuthenticatedAccounts.Count > 0)
            {
                return ToolResponse.Json(new
                {
                    Success = true,
                    ClientIdChanged = true,
                    NeedsReauthentication = result.AuthenticatedAccounts,
                    Message = "Credentials replaced for every account. The Client ID changed, so the " +
                        "stored sign-in for these accounts is no longer valid: " +
                        string.Join(", ", result.AuthenticatedAccounts) +
                        ". Run 'auth_status' for each of them to sign in again.",
                });
            }

            return ToolResponse.Json(new
            {
                Success = true,
                ClientIdChanged = result.ClientIdChanged,
                Message = "Credentials saved and encrypted. They apply to every configured account.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
```

Rewrite `AddAccountTool`:

```csharp
    [McpServerTool(Name = "add_account"), Description(
        "Adds an email account under a short alias you choose. No credentials are needed: every " +
        "account uses the one OAuth client configured by 'setup_gmail'. " +
        "This does not sign in: run 'auth_status' with the same alias afterwards to open the " +
        "browser consent flow, signing in as the Google account you want bound to this alias. " +
        "The first account added becomes the default automatically.")]
    public static async Task<string> AddAccount(
        IAccountRegistry accounts,
        [Description("Short alias for this account, e.g. 'studio'. Lowercase letters, digits and hyphens only.")] string alias,
        [Description("Make this the default account used when no alias is given.")] bool setDefault = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.AddAccountAsync(alias, setDefault, cancellationToken);
            var normalized = AccountAlias.Normalize(alias);

            return ToolResponse.Json(new
            {
                Success = true,
                Account = normalized,
                Message = $"Account '{normalized}' added. " +
                    $"Run auth_status with account '{normalized}' to sign in.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
```

In `RevokeAuthTool`, change the last sentence of the description to:

```csharp
        "This does NOT delete the stored client credentials (Client ID / Secret) - " +
        "use 'setup_gmail' to change those, or 'remove_account' to delete the account.")]
```

Delete `src/EmailMcp.Server/Tools/UpdateAccountCredentialsTool.cs`.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS, everything green across all four test projects.

- [ ] **Step 9: Commit**

```bash
git add -A src tests
git commit -m "feat: one shared OAuth client for every account"
```

---

### Task 4: Documentation and installed verification

**Files:**
- Modify: `README.md` (sections "Available Tools" / "Account management", "Multiple accounts", "Quick Start")

- [ ] **Step 1: Rewrite the README sections**

Update the three sections so they describe the new flows. The tool list must drop `update_account_credentials` and show `add_account(alias, setDefault?)` without credentials. "Multiple accounts" must state that one OAuth client serves every account and that `setup_gmail` sets it once. "Quick Start" keeps its two steps, and gains a third showing how a second account is added:

```
add_account("studio")
auth_status(account: "studio")   # sign in as the studio address
```

Add a short note that re-running `setup_gmail` with a different Client ID invalidates every account's stored sign-in, and that the response names the accounts to re-authenticate.

- [ ] **Step 2: Run the full suite one more time**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: describe the shared OAuth client in the README"
```

- [ ] **Step 4: Install and verify against the running server**

Run: `pwsh scripts/install-local.ps1`
Expected: files written, and a "RESTART CLAUDE CODE" notice.

Restart Claude Code, then check the migration carried the live install forward without a re-authentication:

- `list_accounts` shows `default` with `IsAuthenticated: true` and the recorded address.
- `list_labels` succeeds, proving the shared credential is being read.
- The token directory has `shared--client-credentials.enc` and no `account--default--client-credentials.enc`:

```powershell
Get-ChildItem "$env:USERPROFILE\.email-mcp\tokens" -Name
```

**Do not proceed to adding a second account until `list_labels` succeeds.** A failure here means the migration did not promote the credential, and the fix is in Task 2.

---

## Notes for the implementer

**Why the shared key is `shared--client-credentials` and not `client-credentials`.** Token-store keys become filenames and `DataProtectionTokenStore.GetFilePath` replaces characters that are invalid in filenames. The `--` convention is what makes account keys survive that sanitisation unchanged; the shared key follows it for consistency, and `AccountKeysTests` pins the property.

**The one-line change.** Everything in Task 3 follows from `clientCredentialsKey` no longer depending on the alias. If you find yourself changing `IEmailProvider`, `GmailEmailProvider` or `MimeBuilder`, stop: the multi-account design deliberately kept those out of the blast radius and nothing here should reach them.

**Migration ordering.** `MigrateCredentialsToSharedIfNeededAsync` writes the shared copy before deleting any per-account copy. Reversing that order turns a crash between the two into a permanently de-configured install with no way back.
