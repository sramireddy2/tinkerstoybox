<#
.SYNOPSIS
  Compiles the game's C# with Unity's own Roslyn compiler, without starting Unity.

  A full Unity run takes a minute or more and only one can run at a time. This takes a few seconds,
  needs no lock, and reports the same compiler errors - so run it after every edit and only go to
  .\tools\unity.ps1 once it is clean.

  It reuses the reference and define lists Unity generated on its last compile, and globs the source
  files fresh, so newly added files are included. If you add an assembly reference to an .asmdef, run
  .\tools\unity.ps1 compile once to refresh those lists.

.EXAMPLE
  .\tools\typecheck.ps1
#>
param()

$root = Split-Path -Parent $PSScriptRoot
$versionLine = Get-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
$version = ($versionLine -split ':')[1].Trim()
$data = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Data"
$dotnet = Join-Path $data 'DotNetSdk\dotnet.exe'
$csc = Get-ChildItem (Join-Path $data 'DotNetSdk\sdk') -Recurse -Filter 'csc.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not (Test-Path $dotnet) -or -not $csc) { Write-Output 'TYPECHECK: FAILED - Unity compiler not found'; exit 2 }

$dag = Get-ChildItem (Join-Path $root 'Library\Bee\artifacts') -Directory -Filter '*.dag' -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'Toybox.Editor.rsp') } |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $dag) { Write-Output 'TYPECHECK: FAILED - no Unity compile artifacts yet; run .\tools\unity.ps1 compile once'; exit 2 }

$work = Join-Path $env:TEMP "toybox-typecheck\$PID"
New-Item -ItemType Directory -Force $work | Out-Null

$assemblies = @(
    @{ Name = 'Toybox'; Dir = 'Assets\Toybox\Runtime' },
    @{ Name = 'Toybox.Editor'; Dir = 'Assets\Toybox\Editor' },
    @{ Name = 'Toybox.Tests.EditMode'; Dir = 'Assets\Toybox\Tests\EditMode' }
)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$errorCount = 0
$blocked = $false
Push-Location $root
try {
    foreach ($a in $assemblies) {
        $rspSource = Join-Path $dag.FullName "$($a.Name).rsp"
        $dir = Join-Path $root $a.Dir
        if (-not (Test-Path $rspSource) -or -not (Test-Path $dir)) { continue }
        $files = @(Get-ChildItem $dir -Recurse -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })
        if ($files.Count -eq 0) { continue }
        if ($blocked) { Write-Output "  ($($a.Name) skipped: an assembly it depends on failed)"; continue }

        $keep = Get-Content $rspSource | Where-Object { $_ -match '^-r:|^-define:|^-langversion|^/nowarn|^/unsafe' } | ForEach-Object {
            if ($_ -match 'artifacts/[^/"]+/(Toybox(\.Editor)?)\.ref\.dll') { '-r:"' + (Join-Path $work ($Matches[1] + '.dll')) + '"' } else { $_ }
        }
        $outDll = Join-Path $work "$($a.Name).dll"
        $rsp = Join-Path $work "$($a.Name).rsp"
        $content = @('-target:library', '/nologo', '/deterministic', '/utf8output', '/preferreduilang:en-US',
            '/RuntimeMetadataVersion:v4.0.30319', ('-out:"' + $outDll + '"')) + $keep + $files
        [System.IO.File]::WriteAllLines($rsp, [string[]]$content)

        $output = & $dotnet $csc.FullName "@$rsp"
        $errors = @($output | Where-Object { $_ -match ': error ' } | Sort-Object -Unique)
        if ($errors.Count -gt 0) {
            $errorCount += $errors.Count
            Write-Output "$($a.Name): $($errors.Count) error(s)"
            $errors | Select-Object -First 50 | ForEach-Object { Write-Output ("  " + $_.Replace($root + '\', '')) }
            # Later assemblies reference this one, so their errors would only be noise.
            $blocked = $true
        }
    }
}
finally { Pop-Location }
$sw.Stop()

$seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
if ($errorCount -gt 0) { Write-Output "TYPECHECK: FAILED ($errorCount errors, ${seconds}s)"; exit 1 }
Write-Output "TYPECHECK: OK (${seconds}s)"
exit 0
