<#
.SYNOPSIS
  Drives the Unity editor headlessly for this project and prints a compact result that always ends in
  "RESULT: OK" or "RESULT: FAILED".

  compile / test / exec go through a persistent background editor (the "batch server"), so they cost a
  script reload (seconds) instead of a full editor start (over a minute). The server is started on
  demand and keeps running; `stop` shuts it down. `build` always uses a fresh editor.

  Only one Unity instance can have the project open, so calls are serialized with a mutex: concurrent
  callers queue instead of failing. The project must not be open in the Unity editor GUI meanwhile.

.EXAMPLE
  .\tools\unity.ps1 compile
  .\tools\unity.ps1 test
  .\tools\unity.ps1 test -Filter "Toybox.Tests.PerspectiveTests"      # regex on the full test name
  .\tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run
  .\tools\unity.ps1 exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','3'
  .\tools\unity.ps1 playcheck                                          # Play Mode smoke run: the bot autoplays a level
  .\tools\unity.ps1 build
  .\tools\unity.ps1 status
  .\tools\unity.ps1 stop
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('compile', 'test', 'exec', 'build', 'playcheck', 'stop', 'status')]
    [string]$Command,
    [string]$Filter = '',
    [string]$Method = '',
    [string]$Out = '',
    [string]$Opt = 'BuildTimes',
    [switch]$Dev,
    [switch]$Cold,
    [string[]]$UnityArgs = @(),
    [int]$TimeoutMin = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$versionLine = Get-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
$version = ($versionLine -split ':')[1].Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
if (-not (Test-Path $unity)) { Write-Output "RESULT: FAILED - Unity $version not found at $unity"; exit 2 }

$cache = $env:TOYBOX_CACHE
if (-not $cache) { $cache = Join-Path $env:USERPROFILE '.cache\tinkerstoybox' }
$outDir = Join-Path $root 'tools\out'
$serverDir = Join-Path $outDir 'server'
New-Item -ItemType Directory -Force $serverDir | Out-Null
$alivePath = Join-Path $serverDir 'alive.json'
$serverLog = Join-Path $serverDir 'server.log'

# Functions below print their findings as ordinary output, so none of them may also *return* a value
# (PowerShell would fold the printed lines into it). They report through these instead.
$script:ok = $false
$script:hadCompileErrors = $false
$script:hadShaderErrors = $false
$script:serverPid = 0

function Quote([string]$s) { '"' + $s + '"' }

function Find-Server {
    $script:serverPid = 0
    if (-not (Test-Path $alivePath)) { return }
    try {
        $state = Get-Content $alivePath -Raw | ConvertFrom-Json
        $proc = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
        if ($proc -and $proc.ProcessName -eq 'Unity') { $script:serverPid = [int]$state.pid }
    }
    catch { }
}

function Send-Request([hashtable]$request) {
    $json = $request | ConvertTo-Json -Compress
    $tmp = Join-Path $serverDir "tmp-$($request.id).json"
    [System.IO.File]::WriteAllText($tmp, $json)
    Move-Item $tmp (Join-Path $serverDir "req-$($request.id).json")
}

function Stop-Server {
    Find-Server
    if ($script:serverPid -eq 0) { return }
    $target = $script:serverPid
    Send-Request @{ id = [guid]::NewGuid().ToString('N').Substring(0, 12); command = 'quit'; filter = ''; method = ''; args = @() }
    $proc = Get-Process -Id $target -ErrorAction SilentlyContinue
    if ($proc) {
        $exited = $proc.WaitForExit(45000)
        if (-not $exited) { Stop-Process -Id $target -Force -ErrorAction SilentlyContinue }
    }
    Write-Output 'Batch server stopped.'
}

function Write-LogDiagnosis([string[]]$lines) {
    $script:hadCompileErrors = $false
    if ($lines -match 'another Unity instance is running with this project open') {
        Write-Output 'PROJECT LOCKED: the project is open in another Unity instance (close the editor and retry).'
    }
    if ($lines -match 'No valid Unity Editor license found') {
        Write-Output 'LICENSE: Unity found no valid editor license - sign in via Unity Hub.'
    }
    $compileErrors = @($lines | Where-Object { $_ -match '\): error (CS|BCE|US)\d+' } | Sort-Object -Unique)
    if ($compileErrors.Count -gt 0) {
        $script:hadCompileErrors = $true
        Write-Output "COMPILE ERRORS ($($compileErrors.Count)):"
        $compileErrors | Select-Object -First 40 | ForEach-Object { Write-Output "  $_" }
    }
    $shaderErrors = @($lines | Where-Object { $_ -match "^Shader error in '" } | Sort-Object -Unique)
    if ($shaderErrors.Count -gt 0) {
        Write-Output "SHADER ERRORS ($($shaderErrors.Count)):"
        $shaderErrors | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    }
}

function Write-ServerLogTail {
    if (-not (Test-Path $serverLog)) { return }
    $lines = Get-Content $serverLog
    Write-LogDiagnosis $lines
    if (-not $script:hadCompileErrors) { $lines | Select-Object -Last 25 | ForEach-Object { Write-Output "  $_" } }
}

function Start-Server {
    $script:serverPid = 0
    $proc = Start-Process -FilePath $unity -PassThru -WindowStyle Hidden -ArgumentList (
        @('-batchmode', '-projectPath', (Quote $root), '-logFile', (Quote $serverLog), '-toyboxServer') -join ' ')
    $deadline = (Get-Date).AddMinutes(25)
    while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
        Find-Server
        if ($script:serverPid -eq $proc.Id) { return }
    }
    $script:serverPid = 0
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Write-Output 'The Unity batch server failed to start.'
    Write-ServerLogTail
}

function Get-ServerLogLength {
    if (Test-Path $serverLog) { return (Get-Item $serverLog).Length }
    return 0
}

# Shader compile errors only ever show up in the editor log, so surface the ones logged since $offset.
function Write-NewShaderErrors([long]$offset) {
    $script:hadShaderErrors = $false
    if (-not (Test-Path $serverLog)) { return }
    $text = ''
    try {
        $stream = [System.IO.File]::Open($serverLog, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            if ($offset -gt $stream.Length) { $offset = 0 }
            $null = $stream.Seek($offset, [System.IO.SeekOrigin]::Begin)
            $reader = New-Object System.IO.StreamReader($stream)
            $text = $reader.ReadToEnd()
        }
        finally { $stream.Dispose() }
    }
    catch { return }
    $errors = @($text -split "`r?`n" | Where-Object { $_ -match "^Shader error in '" } | Sort-Object -Unique)
    if ($errors.Count -gt 0) {
        $script:hadShaderErrors = $true
        Write-Output "SHADER ERRORS ($($errors.Count)):"
        $errors | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    }
}

function Invoke-Server([string]$serverCommand = $Command, [string]$serverMethod = $Method, [string[]]$serverArgs = $UnityArgs) {
    $script:ok = $false
    Find-Server
    if ($script:serverPid -eq 0) { Start-Server }
    if ($script:serverPid -eq 0) { return }
    $target = $script:serverPid
    $logOffset = Get-ServerLogLength

    $id = [guid]::NewGuid().ToString('N').Substring(0, 12)
    Send-Request @{ id = $id; command = $serverCommand; filter = $Filter; method = $serverMethod; args = [string[]]$serverArgs }
    $responsePath = Join-Path $serverDir "res-$id.json"
    $deadline = (Get-Date).AddMinutes($TimeoutMin)
    while (-not (Test-Path $responsePath)) {
        Start-Sleep -Milliseconds 200
        if (-not (Get-Process -Id $target -ErrorAction SilentlyContinue)) {
            Write-Output 'The Unity batch server exited while handling the request.'
            Write-ServerLogTail
            return
        }
        if ((Get-Date) -gt $deadline) {
            Write-Output "TIMEOUT after $TimeoutMin min - stopping the batch server."
            Stop-Process -Id $target -Force -ErrorAction SilentlyContinue
            return
        }
    }
    Start-Sleep -Milliseconds 50
    $response = Get-Content $responsePath -Raw | ConvertFrom-Json
    foreach ($line in $response.lines) { Write-Output $line }
    Write-NewShaderErrors $logOffset
    $script:ok = [bool]$response.ok -and -not $script:hadShaderErrors
}

# Enters Play Mode in the batch server, lets the game autoplay a level through the real Update loop,
# and reports what Toybox.EditorTools.PlayCheck wrote. The caller's mutex is held for the whole
# session so no other request is served while the editor is playing.
function Invoke-PlayCheck {
    $resultPath = Join-Path $outDir 'playcheck\result.txt'
    if (Test-Path $resultPath) { Remove-Item $resultPath -Force }
    Invoke-Server 'exec' 'Toybox.EditorTools.PlayCheck.Run' (@('-toyboxExclusive') + $UnityArgs)
    if (-not $script:ok) { return }
    $script:ok = $false

    $deadline = (Get-Date).AddSeconds(180)
    while (-not (Test-Path $resultPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path $resultPath)) {
        Write-Output 'No result after 180 s - asking the editor to leave Play Mode.'
        Invoke-Server 'exec' 'Toybox.EditorTools.PlayCheck.Abort' @()
        $script:ok = $false
        $deadline = (Get-Date).AddSeconds(60)
        while (-not (Test-Path $resultPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    }
    if (-not (Test-Path $resultPath)) { Write-Output 'PLAYCHECK: NO RESULT - the editor may still be in Play Mode'; return }
    Start-Sleep -Milliseconds 300
    $lines = Get-Content $resultPath
    $lines | ForEach-Object { Write-Output $_ }
    $script:ok = [bool]($lines -match '^PLAYCHECK: OK')
}

function Invoke-Cold {
    $script:ok = $false
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $log = Join-Path $outDir "$Command-$stamp.log"
    $results = Join-Path $outDir "$Command-$stamp.xml"
    $argList = @('-batchmode', '-projectPath', (Quote $root), '-logFile', (Quote $log))
    switch ($Command) {
        'compile' { $argList += @('-nographics', '-quit') }
        'test' {
            $argList += @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', (Quote $results))
            if ($Filter) { $argList += @('-testFilter', (Quote $Filter)) }
        }
        'exec' { $argList += @('-quit', '-executeMethod', $Method) }
        'build' {
            $argList += @('-nographics', '-quit', '-executeMethod', 'Toybox.EditorTools.BuildScript.BuildWebGL',
                '-toyboxOutput', (Quote $Out), '-toyboxOpt', $Opt)
            if ($Dev) { $argList += '-toyboxDev' }
        }
    }
    foreach ($a in $UnityArgs) { if ($a -match '\s') { $argList += (Quote $a) } else { $argList += $a } }

    $proc = Start-Process -FilePath $unity -ArgumentList ($argList -join ' ') -PassThru -WindowStyle Hidden
    $null = $proc.Handle
    $finished = $proc.WaitForExit($TimeoutMin * 60000)
    if (-not $finished) { try { $proc.Kill() } catch { } }
    $exit = if ($finished) { $proc.ExitCode } else { -1 }

    Copy-Item $log (Join-Path $outDir "last-$Command.log") -Force -ErrorAction SilentlyContinue
    $lines = @()
    if (Test-Path $log) { $lines = Get-Content $log }
    Write-Output "log: $log (exit $exit)"

    $good = $true
    if (-not $finished) { Write-Output "TIMEOUT after $TimeoutMin min"; $good = $false }
    if ($lines -match 'another Unity instance is running with this project open|No valid Unity Editor license found') { $good = $false }
    Write-LogDiagnosis $lines
    if ($script:hadCompileErrors) { $good = $false }

    $lines | Where-Object { $_ -match '^\[Toybox\]' } | Select-Object -Last 60 | ForEach-Object { Write-Output $_ }
    $exceptions = @($lines | Where-Object { $_ -match '^(\w+\.)*\w*Exception: ' } | Sort-Object -Unique)
    if ($exceptions.Count -gt 0) {
        Write-Output "EXCEPTIONS ($($exceptions.Count)):"
        $exceptions | Select-Object -First 12 | ForEach-Object { Write-Output "  $_" }
    }

    if ($Command -eq 'test') {
        if (Test-Path $results) {
            [xml]$xml = Get-Content $results
            $run = $xml.'test-run'
            Write-Output "TESTS: total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped) duration=$([math]::Round([double]$run.duration, 1))s"
            foreach ($case in $xml.SelectNodes("//test-case[@result='Failed']")) {
                Write-Output "  FAILED: $($case.fullname)"
                $msg = $case.failure.message.InnerText
                if ($msg) { ($msg.Trim() -split "`n") | Select-Object -First 14 | ForEach-Object { Write-Output "      $($_.TrimEnd())" } }
            }
            if ([int]$run.failed -gt 0 -or [int]$run.total -eq 0) { $good = $false }
            if ([int]$run.total -eq 0) { Write-Output '  (no tests matched the filter)' }
        }
        else {
            Write-Output 'TESTS: no results file was produced (compile error or crash - see log).'
            $good = $false
        }
    }
    elseif ($exit -ne 0) { $good = $false }

    if (-not $good -and -not $script:hadCompileErrors -and $Command -ne 'test') {
        Write-Output '--- last log lines ---'
        $lines | Select-Object -Last 25 | ForEach-Object { Write-Output "  $_" }
    }
    $script:ok = $good
}

# ---------------------------------------------------------------------------------------------------

if ($Command -eq 'status') {
    Find-Server
    if ($script:serverPid -ne 0) { Write-Output "Batch server running (pid $($script:serverPid))." } else { Write-Output 'Batch server not running.' }
    exit 0
}
if ($Command -eq 'exec' -and -not $Method) { Write-Output 'RESULT: FAILED - exec needs -Method'; exit 2 }
if ($Command -eq 'build') {
    if (-not $Out) { $Out = Join-Path $cache 'Build\WebGL' }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
}

# Compiler errors are found in seconds without Unity, so check them before queueing for the editor.
if ($Command -ne 'stop') {
    $typecheck = & (Join-Path $PSScriptRoot 'typecheck.ps1')
    if ($LASTEXITCODE -eq 1) {
        $typecheck | ForEach-Object { Write-Output $_ }
        Write-Output 'RESULT: FAILED'
        exit 1
    }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
# One mutex per project folder, so a build running in a clone of the project does not block this one.
$sha = New-Object System.Security.Cryptography.SHA1Managed
$rootHash = [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($root.ToLowerInvariant()))).Replace('-', '').Substring(0, 12)
$mutex = New-Object System.Threading.Mutex($false, "TinkersToyboxUnityBatch-$rootHash")
$owned = $false
try {
    try { $owned = $mutex.WaitOne([TimeSpan]::FromMinutes(180)) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { Write-Output 'RESULT: FAILED - timed out waiting for another Unity run to finish'; exit 2 }

    if ($Command -eq 'stop') {
        Stop-Server
        $script:ok = $true
    }
    elseif ($Command -eq 'build' -or $Cold) {
        Stop-Server
        Invoke-Cold
    }
    elseif ($Command -eq 'playcheck') {
        Invoke-PlayCheck
    }
    else {
        Invoke-Server
    }
}
finally {
    if ($owned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
$sw.Stop()

Write-Output "unity $Command took $([math]::Round($sw.Elapsed.TotalSeconds))s"
if ($script:ok) { Write-Output 'RESULT: OK'; exit 0 }
Write-Output 'RESULT: FAILED'
exit 1
