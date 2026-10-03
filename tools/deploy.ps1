<#
.SYNOPSIS
  Builds the WebGL player and publishes it to the gh-pages branch, which GitHub Pages serves.

  The published branch always holds exactly one commit (the latest build), so the repository does not
  grow by a full build on every deploy. Nothing is checked out or copied: the build directory is
  written straight into a commit with git plumbing.

.EXAMPLE
  .\tools\deploy.ps1                 # build (optimized for runtime speed) and publish
  .\tools\deploy.ps1 -Opt BuildTimes # faster build, slower game
  .\tools\deploy.ps1 -SkipBuild      # publish the build that is already in the cache directory
#>
param(
    [string]$Opt = 'RuntimeSpeed',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cache = $env:TOYBOX_CACHE
if (-not $cache) { $cache = Join-Path $env:USERPROFILE '.cache\tinkerstoybox' }
$build = Join-Path $cache 'Build\WebGL'

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'unity.ps1') build -Out $build -Opt $Opt
    if ($LASTEXITCODE -ne 0) { Write-Output 'DEPLOY: build failed, nothing published.'; exit 1 }
}
if (-not (Test-Path (Join-Path $build 'index.html'))) { Write-Output "DEPLOY: no build found at $build"; exit 1 }

# GitHub rejects files over 100 MB.
$tooBig = Get-ChildItem $build -Recurse -File | Where-Object { $_.Length -gt 95MB }
if ($tooBig) { Write-Output "DEPLOY: file too large for GitHub: $($tooBig[0].FullName)"; exit 1 }

# Tell Pages not to run Jekyll (it would drop files and folders that start with an underscore).
Set-Content -Path (Join-Path $build '.nojekyll') -Value '' -NoNewline

$sha = (git -C $root rev-parse --short HEAD).Trim()
$sizeMb = [math]::Round(((Get-ChildItem $build -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)

$env:GIT_DIR = Join-Path $root '.git'
$env:GIT_WORK_TREE = $build
$env:GIT_INDEX_FILE = Join-Path $cache 'pages.index'
Push-Location $build
try {
    git -c core.autocrlf=false -c core.safecrlf=false add -A .
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    $tree = (git write-tree).Trim()
    $commit = (git commit-tree $tree -m "Deploy WebGL build of $sha").Trim()
    git push --force origin "${commit}:refs/heads/gh-pages"
    if ($LASTEXITCODE -ne 0) { throw 'git push failed' }
}
finally {
    Pop-Location
    Remove-Item Env:GIT_DIR, Env:GIT_WORK_TREE, Env:GIT_INDEX_FILE -ErrorAction SilentlyContinue
}

Write-Output "DEPLOY: published $sizeMb MB (source $sha) as $commit to gh-pages."
Write-Output 'DEPLOY: https://sramireddy2.github.io/tinkerstoybox/ updates within a minute or two.'
