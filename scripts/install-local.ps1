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
foreach ($stale in @(Get-ChildItem $target -Recurse -File -Filter '*.locked-*' -ErrorAction SilentlyContinue)) {
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
    Write-Host ''
    Write-Host 'RESTART CLAUDE CODE to load the new build. Sessions running right now keep' -ForegroundColor Yellow
    Write-Host 'executing the old code they already mapped.' -ForegroundColor Yellow
}
elseif (-not $DryRun) {
    $running = @(Get-Process EmailMcp.Server -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Host ''
        Write-Host "RESTART CLAUDE CODE - $($running.Count) EmailMcp.Server process(es) are still running the old build." -ForegroundColor Yellow
    }
}
