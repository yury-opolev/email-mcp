<#
.SYNOPSIS
    Builds EmailMcp.Server and installs it over the directory Claude Code launches it from,
    without needing to quit Claude Code first.

.DESCRIPTION
    The MCP server is registered user-scope in ~/.claude.json as

        mcpServers.email-mcp.command = <repo>\publish\EmailMcp.Server.exe

    so `dotnet publish -o publish` fails with MSB3027 whenever a Claude Code session (or a
    stale one) is holding the assemblies. Windows refuses to DELETE or OVERWRITE a mapped
    image, but it does allow RENAMING one, so this script publishes to a staging directory
    and then, per file:

        1. skips the file when it is already byte-identical,
        2. copies it straight over when the target is unlocked,
        3. otherwise renames the locked target aside to <name>.locked-<stamp> and copies the
           new file into the path it just vacated.

    Step 3 keeps a valid assembly at every path at all times, so a running server that
    lazily loads an assembly after the swap finds the new one rather than a hole. Running
    sessions keep executing the OLD code they already mapped; the new code is picked up when
    Claude Code restarts and relaunches the server.

    Leftover .locked-* files are deleted on the next run, once the processes holding them
    are gone.

.PARAMETER Configuration
    Build configuration to publish. Defaults to Release.

.PARAMETER StagingPath
    Directory the build is published into, relative to the repo root unless absolute.
    Defaults to publish-staging.

.PARAMETER TargetPath
    Directory Claude Code launches the server from, relative to the repo root unless
    absolute. Defaults to publish.

.PARAMETER SkipBuild
    Install from an existing StagingPath instead of publishing first.

.PARAMETER DryRun
    Report what would change without touching TargetPath.

.EXAMPLE
    pwsh scripts/install-local.ps1
    Publish and install, then restart Claude Code.

.EXAMPLE
    pwsh scripts/install-local.ps1 -SkipBuild -StagingPath publish-new
    Install a build that was staged earlier.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $StagingPath = 'publish-staging',
    [string] $TargetPath = 'publish',
    [switch] $SkipBuild,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\EmailMcp.Server\EmailMcp.Server.csproj'

function Resolve-UnderRepo([string] $path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $path))
}

$staging = Resolve-UnderRepo $StagingPath
$target = Resolve-UnderRepo $TargetPath

if ($staging -eq $target) {
    throw "StagingPath and TargetPath resolve to the same directory ($target). The whole point is to publish somewhere the running server does not hold locks."
}

# --- build -----------------------------------------------------------------------------

if ($SkipBuild) {
    Write-Host "Skipping build, installing from $staging" -ForegroundColor Yellow
}
else {
    if (Test-Path $staging) {
        Remove-Item $staging -Recurse -Force
    }
    Write-Host "Publishing $Configuration to $staging" -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -o $staging
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

$serverExe = Join-Path $staging 'EmailMcp.Server.exe'
if (-not (Test-Path $serverExe)) {
    throw "No EmailMcp.Server.exe in $staging - nothing to install."
}

if (-not (Test-Path $target)) {
    if ($DryRun) {
        Write-Host "[dry run] would create $target"
    }
    else {
        New-Item -ItemType Directory -Path $target | Out-Null
    }
}

# --- sweep leftovers from earlier runs --------------------------------------------------

$sweptCount = 0
$stillLockedCount = 0
$newestDisplacement = $null
foreach ($stale in @(Get-ChildItem $target -Recurse -File -Filter '*.locked-*' -ErrorAction SilentlyContinue)) {
    # The suffix encodes when that displacement happened, which is the one honest record of
    # the last install time when no stamp file exists yet. Read it BEFORE the sweep, because
    # a successful sweep destroys the evidence.
    if ($stale.Name -match '\.locked-(\d{8}-\d{6})$') {
        $parsed = [datetime]::MinValue
        if ([datetime]::TryParseExact($Matches[1], 'yyyyMMdd-HHmmss', $null, 'None', [ref] $parsed)) {
            if ($null -eq $newestDisplacement -or $parsed -gt $newestDisplacement) {
                $newestDisplacement = $parsed
            }
        }
    }

    if ($DryRun) {
        Write-Host "[dry run] would try to delete leftover $($stale.Name)"
        continue
    }
    try {
        Remove-Item $stale.FullName -Force -ErrorAction Stop
        $sweptCount++
    }
    catch {
        # A session started before the previous upgrade is still running. Harmless: it will
        # be swept by whichever run happens after that process exits.
        $stillLockedCount++
    }
}

# --- install ----------------------------------------------------------------------------

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$copiedCount = 0
$skippedCount = 0
$displacedNames = [System.Collections.Generic.List[string]]::new()

foreach ($source in @(Get-ChildItem $staging -Recurse -File)) {
    $relative = $source.FullName.Substring($staging.Length).TrimStart('\', '/')
    $destination = Join-Path $target $relative

    if (Test-Path $destination) {
        $sourceHash = (Get-FileHash $source.FullName -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash $destination -Algorithm SHA256).Hash
        if ($sourceHash -eq $destinationHash) {
            $skippedCount++
            continue
        }
    }

    if ($DryRun) {
        Write-Host "[dry run] would install $relative"
        $copiedCount++
        continue
    }

    $destinationDirectory = Split-Path -Parent $destination
    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    try {
        Copy-Item $source.FullName $destination -Force -ErrorAction Stop
    }
    catch [System.IO.IOException] {
        # Locked by a running server. Rename it out of the way - Windows permits renaming a
        # mapped image even though it refuses to delete or overwrite one - then copy into
        # the path that just became free.
        $displaced = "$destination.locked-$stamp"
        Move-Item $destination $displaced -Force -ErrorAction Stop
        Copy-Item $source.FullName $destination -Force -ErrorAction Stop
        $displacedNames.Add($relative)
    }

    $copiedCount++
}

# --- record when this content was installed ---------------------------------------------

# A server process is running stale code iff it started BEFORE the currently installed files
# were put in place. Copy-Item preserves the source's timestamps, so the target's mtime is
# when the build was produced rather than when it was installed - those differ, and the gap
# is exactly where a wrong answer would come from. Record the install time explicitly, and
# only when something actually changed, so the stamp keeps describing the content on disk.

$stampPath = Join-Path $target '.install-local-stamp'

if ($copiedCount -gt 0 -and -not $DryRun) {
    Set-Content -Path $stampPath -Value (Get-Date).ToString('o') -Encoding utf8
}

$installedAt = $null
if (Test-Path $stampPath) {
    try {
        $installedAt = [datetime]::Parse((Get-Content $stampPath -Raw).Trim())
    }
    catch {
        $installedAt = $null
    }
}
if ($null -eq $installedAt -and $null -ne $newestDisplacement) {
    # No stamp, but files were displaced by an earlier run and the suffix records when.
    $installedAt = $newestDisplacement
}
if ($null -eq $installedAt) {
    # Last resort: the server assembly's own mtime. This is WEAK - Copy-Item preserves the
    # source timestamps, so it dates the build rather than the install, and a process that
    # started between those two moments is stale but will not look it.
    $installedExe = Join-Path $target 'EmailMcp.Server.exe'
    if (Test-Path $installedExe) {
        $installedAt = (Get-Item $installedExe).LastWriteTime
    }
}

# Seed the stamp from whatever evidence was used, so later runs do not have to guess again.
if ($null -ne $installedAt -and -not (Test-Path $stampPath) -and -not $DryRun) {
    Set-Content -Path $stampPath -Value $installedAt.ToString('o') -Encoding utf8
}

$staleProcesses = @()
if ($null -ne $installedAt) {
    $staleProcesses = @(
        Get-Process EmailMcp.Server -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -lt $installedAt }
    )
}

# --- report -----------------------------------------------------------------------------

Write-Host ''
Write-Host "Installed to $target" -ForegroundColor Green
Write-Host "  $copiedCount file(s) written, $skippedCount already current"
if ($sweptCount -gt 0) {
    Write-Host "  $sweptCount leftover .locked-* file(s) swept"
}
if ($stillLockedCount -gt 0) {
    Write-Host "  $stillLockedCount leftover .locked-* file(s) still held by a running process - they will be swept on a later run"
}

if ($displacedNames.Count -gt 0) {
    Write-Host ''
    Write-Host "$($displacedNames.Count) file(s) were locked and had to be displaced:" -ForegroundColor Yellow
    foreach ($name in $displacedNames) {
        Write-Host "  $name"
    }
}

if ($DryRun) {
    return
}

Write-Host ''
if ($staleProcesses.Count -gt 0) {
    Write-Host "RESTART CLAUDE CODE - $($staleProcesses.Count) EmailMcp.Server process(es) started before this build was installed:" -ForegroundColor Yellow
    foreach ($process in $staleProcesses) {
        Write-Host ("  PID {0}, started {1}" -f $process.Id, $process.StartTime.ToString('yyyy-MM-dd HH:mm:ss'))
    }
    Write-Host 'They keep executing the code they mapped at launch.' -ForegroundColor Yellow
    if ($stillLockedCount -gt 0) {
        Write-Host 'One of them is what still holds the .locked-* files above. If a PID predates' -ForegroundColor Yellow
        Write-Host 'your current session it is an orphan and can be ended to free them.' -ForegroundColor Yellow
    }
}
elseif ($copiedCount -gt 0) {
    Write-Host 'RESTART CLAUDE CODE to load the new build.' -ForegroundColor Yellow
}
else {
    Write-Host 'Already current - nothing to do.' -ForegroundColor Green
}
