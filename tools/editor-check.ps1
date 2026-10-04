<#
.SYNOPSIS
  Checks the game in the real, windowed Unity editor. Everything else in tools/ drives Unity headless;
  this opens the project the way Unity Hub does, enters Play Mode on a level, lets the built-in bot
  solve it, and closes the editor again. Prints the play check's report, then "RESULT: OK" or
  "RESULT: FAILED".

  It runs on the private build copy (see build-clone.ps1), so the background editor in the main folder
  keeps serving tests meanwhile. A Unity window is on screen for a minute or two.

.EXAMPLE
  .\tools\editor-check.ps1                # level 1
  .\tools\editor-check.ps1 -Level 3
  .\tools\editor-check.ps1 -Level 1,2,3,4
#>
param(
    [int[]]$Level = @(1),
    [switch]$NoSync,
    [int]$TimeoutMin = 12
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cache = $env:TOYBOX_CACHE
if (-not $cache) { $cache = Join-Path $env:USERPROFILE '.cache\tinkerstoybox' }
$clone = Join-Path $cache 'buildclone'

if (-not $NoSync) {
    & (Join-Path $PSScriptRoot 'build-clone.ps1') -SyncOnly
    if ($LASTEXITCODE -ne 0) { Write-Output 'RESULT: FAILED - could not sync the build copy'; exit 1 }
}

$versionLine = Get-Content (Join-Path $clone 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
$version = ($versionLine -split ':')[1].Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
if (-not (Test-Path $unity)) { Write-Output "RESULT: FAILED - Unity $version not found at $unity"; exit 2 }

$outDir = Join-Path $clone 'tools\out'
$checkDir = Join-Path $outDir 'playcheck'
$result = Join-Path $checkDir 'result.txt'
New-Item -ItemType Directory -Force $checkDir | Out-Null

# The same lock tools/unity.ps1 takes in the copy, so a build there and this check never overlap.
$sha = New-Object System.Security.Cryptography.SHA1Managed
$rootHash = [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($clone.ToLowerInvariant()))).Replace('-', '').Substring(0, 12)
$mutex = New-Object System.Threading.Mutex($false, "TinkersToyboxUnityBatch-$rootHash")
try { [void]$mutex.WaitOne() } catch [System.Threading.AbandonedMutexException] { }

$failed = 0
try {
    foreach ($id in $Level) {
        if (Test-Path $result) { Remove-Item $result -Force }
        $log = Join-Path $outDir "editor-check-$id.log"
        $unityArgs = @('-projectPath', "`"$clone`"", '-logFile', "`"$log`"",
            '-executeMethod', 'Toybox.EditorTools.PlayCheck.Run', '-toyboxExclusive', '-toyboxQuit',
            '-toyboxUrl', "`"?level=$id`"") -join ' '   # the play check switches the bot on itself
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru
        $exited = $proc.WaitForExit($TimeoutMin * 60000)
        if (-not $exited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

        Write-Output "--- level $id in the windowed editor ($([int]$watch.Elapsed.TotalSeconds) s)"
        if (-not $exited) { Write-Output "TIMED OUT after $TimeoutMin min; the editor was closed. Log: $log" }
        if (Test-Path $result) {
            Get-Content $result | ForEach-Object { Write-Output "  $_" }
            if (-not ((Get-Content $result -TotalCount 1) -eq 'PLAYCHECK: OK')) { $failed++ }
        }
        else {
            $failed++
            Write-Output "  no result was written. Log: $log"
            if (Test-Path $log) {
                $lines = Get-Content $log
                $errors = @($lines | Where-Object { $_ -match '\): error (CS|BCE|US)\d+|another Unity instance is running|No valid Unity Editor license' } | Sort-Object -Unique)
                $errors | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
            }
        }
    }
}
finally {
    $mutex.ReleaseMutex()
}

if ($failed -eq 0) { Write-Output 'RESULT: OK'; exit 0 }
Write-Output "RESULT: FAILED ($failed of $($Level.Count))"
exit 1
