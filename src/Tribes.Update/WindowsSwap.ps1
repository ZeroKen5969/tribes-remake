# Replaces the game's files with a staged update after the game has exited, and puts the old
# files back when anything fails. Started by Tribes.Update (WindowsAppUpdater) with -PlanPath
# pointing to a JSON plan; writes a result file that the game reads at its next start.
# Runs on Windows PowerShell 5.1. Every entry is moved (renamed) inside the install directory,
# so the swap never copies data and never crosses volumes.
param([Parameter(Mandatory = $true)][string]$PlanPath)

$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Log([string]$message) {
    try {
        $line = (Get-Date).ToString('o') + ' ' + $message + [Environment]::NewLine
        [System.IO.File]::AppendAllText($plan.logPath, $line, $utf8)
    } catch {
        # The log is a convenience; the result file carries the outcome.
    }
}

function Write-Result([string]$status, [string]$detail) {
    $result = [ordered]@{
        status = $status
        detail = $detail
        version = $plan.version
        finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    [System.IO.File]::WriteAllText($plan.resultPath, ($result | ConvertTo-Json), $utf8)
    Write-Log ('result: ' + $status + ' ' + $detail)
}

function Move-Entry([string]$from, [string]$to) {
    # Files can stay locked for a moment after the game exits (virus scanners, indexers).
    for ($attempt = 1; ; $attempt++) {
        try {
            if ([System.IO.Directory]::Exists($from)) {
                [System.IO.Directory]::Move($from, $to)
            } else {
                [System.IO.File]::Move($from, $to)
            }
            return
        } catch {
            if ($attempt -ge [int]$plan.moveAttempts) { throw }
            Start-Sleep -Milliseconds ([int]$plan.retryDelayMs)
        }
    }
}

function Start-Game {
    if ([string]::IsNullOrEmpty($plan.relaunchPath)) { return }
    try {
        if ([string]::IsNullOrEmpty($plan.relaunchArguments)) {
            Start-Process -FilePath $plan.relaunchPath -WorkingDirectory $plan.installDir
        } else {
            Start-Process -FilePath $plan.relaunchPath -ArgumentList $plan.relaunchArguments -WorkingDirectory $plan.installDir
        }
    } catch {
        Write-Log ('relaunch failed: ' + $_.Exception.Message)
    }
}

function Wait-Game {
    if ([int]$plan.pid -le 0) { return $true }
    try {
        $process = [System.Diagnostics.Process]::GetProcessById([int]$plan.pid)
    } catch {
        return $true # Already gone.
    }
    if ([long]$plan.processStartUtcTicks -gt 0) {
        try {
            $delta = [Math]::Abs($process.StartTime.ToUniversalTime().Ticks - [long]$plan.processStartUtcTicks)
            if ($delta -gt 20000000) { return $true } # Another process reusing the id.
        } catch {
            Write-Log ('could not read the start time of process ' + $plan.pid + ': ' + $_.Exception.Message)
        }
    }
    return $process.WaitForExit([int]$plan.waitTimeoutMs)
}

$install = $plan.installDir
$staging = Join-Path $install $plan.stagingName
$backup = Join-Path $install $plan.backupName
$skip = @($plan.preserve) + @($plan.stagingName, $plan.backupName)

Write-Log ('update to ' + $plan.version + ': waiting for process ' + $plan.pid)
if (-not (Wait-Game)) {
    Write-Result 'failed' 'The game did not exit.'
    exit 2
}
if (-not [System.IO.Directory]::Exists($staging)) {
    Write-Result 'failed' 'The staged update is missing.'
    Start-Game
    exit 2
}
if ([System.IO.Directory]::Exists($backup)) {
    Write-Result 'failed' 'A backup folder from an earlier update is still there.'
    Start-Game
    exit 2
}

$moved = New-Object System.Collections.Generic.List[string]
$placed = New-Object System.Collections.Generic.List[string]
try {
    [System.IO.Directory]::CreateDirectory($backup) | Out-Null
    foreach ($entry in [System.IO.Directory]::GetFileSystemEntries($install)) {
        $name = [System.IO.Path]::GetFileName($entry)
        if ($skip -contains $name) { continue }
        Move-Entry $entry (Join-Path $backup $name)
        $moved.Add($name)
    }
    foreach ($entry in [System.IO.Directory]::GetFileSystemEntries($staging)) {
        $name = [System.IO.Path]::GetFileName($entry)
        if ($skip -contains $name) { continue }
        Move-Entry $entry (Join-Path $install $name)
        $placed.Add($name)
    }
} catch {
    $reason = $_.Exception.Message
    Write-Log ('swap failed, rolling back: ' + $reason)
    $restored = $true
    foreach ($name in $placed) {
        try { Move-Entry (Join-Path $install $name) (Join-Path $staging $name) }
        catch { $restored = $false; Write-Log ('could not take back ' + $name + ': ' + $_.Exception.Message) }
    }
    foreach ($name in $moved) {
        try { Move-Entry (Join-Path $backup $name) (Join-Path $install $name) }
        catch { $restored = $false; Write-Log ('could not restore ' + $name + ': ' + $_.Exception.Message) }
    }
    if (-not $restored) {
        Write-Result 'rollbackFailed' $reason
        exit 3
    }
    try { [System.IO.Directory]::Delete($backup, $true) }
    catch { Write-Log ('could not delete the empty backup folder: ' + $_.Exception.Message) }
    Write-Result 'rolledBack' $reason
    Start-Game
    exit 1
}

Write-Result 'succeeded' ''
try { [System.IO.Directory]::Delete($backup, $true) }
catch { Write-Log ('could not delete the backup: ' + $_.Exception.Message) }
try { [System.IO.Directory]::Delete($staging, $true) }
catch { Write-Log ('could not delete the staging folder: ' + $_.Exception.Message) }
Start-Game
exit 0
