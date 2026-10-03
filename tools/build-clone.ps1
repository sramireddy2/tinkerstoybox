<#
.SYNOPSIS
  Makes a WebGL build from a private copy of the project, so the build (which needs the project to
  itself for several minutes) never blocks the headless editor that serves tests in the main folder.

  Each run mirrors Assets, Packages, ProjectSettings and the tools scripts into the clone, then builds
  there. The clone keeps its own Library, so builds after the first are incremental.

.EXAMPLE
  .\tools\build-clone.ps1                 # sync + build
  .\tools\build-clone.ps1 -Deploy         # sync + build + publish to gh-pages
  .\tools\build-clone.ps1 -SeedLibrary    # first run only: copy the main Library (stop the batch server first)
#>
param(
    [string]$Opt = 'BuildTimes',
    [switch]$Dev,
    [switch]$Deploy,
    [switch]$SeedLibrary,
    [switch]$SyncOnly,
    [int]$TimeoutMin = 100
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cache = $env:TOYBOX_CACHE
if (-not $cache) { $cache = Join-Path $env:USERPROFILE '.cache\tinkerstoybox' }
$clone = Join-Path $cache 'buildclone'
New-Item -ItemType Directory -Force $clone | Out-Null

function Sync-Folder([string]$name, [string[]]$extra) {
    $robocopyArgs = @((Join-Path $root $name), (Join-Path $clone $name), '/MIR', '/COPY:DT', '/DCOPY:T', '/R:2', '/W:1', '/NFL', '/NDL', '/NP', '/NJH', '/NJS') + $extra
    & robocopy @robocopyArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $name (exit $LASTEXITCODE)" }
}

Sync-Folder 'Assets' @('/XD', (Join-Path $root 'Assets\Resources'))
Sync-Folder 'Packages' @()
Sync-Folder 'ProjectSettings' @()
Sync-Folder 'tools' @('/XD', (Join-Path $root 'tools\out'))

if ($SeedLibrary -and -not (Test-Path (Join-Path $clone 'Library\PackageCache'))) {
    & robocopy (Join-Path $root 'Library') (Join-Path $clone 'Library') /E /COPY:DT /DCOPY:T /R:1 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Output "BUILD-CLONE: Library seed had copy failures (exit $LASTEXITCODE); Unity will re-import what is missing." }
}
$global:LASTEXITCODE = 0
Write-Output "BUILD-CLONE: synced to $clone"
if ($SyncOnly) { exit 0 }

$buildArgs = @{ Opt = $Opt; TimeoutMin = $TimeoutMin }
if ($Dev) { $buildArgs.Dev = $true }
& (Join-Path $clone 'tools\unity.ps1') build @buildArgs
if ($LASTEXITCODE -ne 0) { Write-Output 'BUILD-CLONE: build failed.'; exit 1 }

if ($Deploy) {
    & (Join-Path $root 'tools\deploy.ps1') -SkipBuild
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
exit 0
