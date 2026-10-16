# Replaces the game's files with a staged update after the game has exited, and puts the old
# files back when anything fails. Started by Tribes.Update (WindowsAppUpdater) with -PlanPath
# pointing to a JSON plan; writes a result file that the game reads at its next start.
# Runs on Windows PowerShell 5.1. Only the game's own files are touched: those on the list of
# installed files (install-files.txt in the install directory) and those on the update's list.
# Anything else in the install directory stays where it is, so a game unpacked into a shared
# folder (Downloads, the desktop) never takes the user's files with it. Every file is moved
# (renamed) inside the install directory, so the swap never copies data and never crosses volumes.
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

function New-NameSet {
    return ,(New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase))
}

function Read-FileList([string]$path) {
    # One path per line, relative to the install directory. A path that is rooted, names a drive
    # or a stream, or has an empty, '.' or '..' part could reach outside the install directory:
    # the update stops instead of touching it. Paths under the preserved entries are left out.
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($line in [System.IO.File]::ReadAllLines($path, $utf8)) {
        if ($line.Trim().Length -eq 0) { continue }
        $parts = $line -split '[\\/]'
        foreach ($part in $parts) {
            if ($part.Length -eq 0 -or $part -eq '.' -or $part -eq '..' -or $part.Contains(':')) {
                throw ('The list ' + $path + ' has a path outside the install directory: ' + $line)
            }
        }
        if ($skip -contains $parts[0]) { continue }
        $names.Add($parts -join '\')
    }
    return ,$names
}

function Get-Folders($names) {
    # Every folder above the given files, inside the install directory.
    $folders = New-NameSet
    foreach ($name in $names) {
        $parent = [System.IO.Path]::GetDirectoryName($name)
        while (-not [string]::IsNullOrEmpty($parent)) {
            [void]$folders.Add($parent)
            $parent = [System.IO.Path]::GetDirectoryName($parent)
        }
    }
    return ,$folders
}

function Remove-EmptyFolders([string]$root, $folders) {
    # Deepest first, so a folder whose subfolders were just removed goes too. A folder that still
    # holds anything stays (Directory.Delete without recursion refuses).
    foreach ($folder in ($folders | Sort-Object { $_.Length } -Descending)) {
        $path = Join-Path $root $folder
        if (-not [System.IO.Directory]::Exists($path)) { continue }
        if (@([System.IO.Directory]::GetFileSystemEntries($path)).Count -gt 0) { continue }
        try { [System.IO.Directory]::Delete($path) }
        catch { Write-Log ('could not remove the empty folder ' + $folder + ': ' + $_.Exception.Message) }
    }
}

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

$oldListPath = Join-Path $install $plan.fileListName
$newListPath = Join-Path $staging $plan.fileListName
try {
    if (-not [System.IO.File]::Exists($newListPath)) { throw 'The staged update has no list of its files.' }
    $newFiles = Read-FileList $newListPath
    if ([System.IO.File]::Exists($oldListPath)) {
        $oldFiles = Read-FileList $oldListPath
    } else {
        $oldFiles = New-Object System.Collections.Generic.List[string]
        Write-Log 'no list of the installed files: only the files the update brings are replaced'
    }
} catch {
    Write-Result 'failed' $_.Exception.Message
    Start-Game
    exit 2
}

$newSet = New-NameSet
foreach ($name in $newFiles) { [void]$newSet.Add($name) }
$gameFiles = New-NameSet
foreach ($name in $oldFiles) { [void]$gameFiles.Add($name) }
foreach ($name in $newFiles) { [void]$gameFiles.Add($name) }
Write-Log ('install directory: ' + $install + ' (installed files listed: ' + $oldFiles.Count + ', files in the update: ' + $newFiles.Count + '; nothing else there is touched)')

$moved = New-Object System.Collections.Generic.List[string]
$placed = New-Object System.Collections.Generic.List[string]
$created = New-Object System.Collections.Generic.List[string]
try {
    [System.IO.Directory]::CreateDirectory($backup) | Out-Null
    foreach ($name in $gameFiles) {
        $path = Join-Path $install $name
        if ([System.IO.Directory]::Exists($path)) { throw ('The game file ' + $name + ' is a folder in the install directory.') }
        if (-not [System.IO.File]::Exists($path)) { continue }
        $target = Join-Path $backup $name
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
        Move-Entry $path $target
        $moved.Add($name)
    }
    foreach ($name in $newFiles) {
        $target = Join-Path $install $name
        $folder = [System.IO.Path]::GetDirectoryName($target)
        for ($missing = $folder; -not [System.IO.Directory]::Exists($missing); $missing = [System.IO.Path]::GetDirectoryName($missing)) {
            $created.Add($missing)
        }
        [System.IO.Directory]::CreateDirectory($folder) | Out-Null
        Move-Entry (Join-Path $staging $name) $target
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
    foreach ($folder in ($created | Sort-Object { $_.Length } -Descending)) {
        try { [System.IO.Directory]::Delete($folder) }
        catch { Write-Log ('could not remove the folder ' + $folder + ': ' + $_.Exception.Message) }
    }
    # The backup holds only the game's (now restored) folders.
    try { [System.IO.Directory]::Delete($backup, $true) }
    catch { Write-Log ('could not delete the empty backup folder: ' + $_.Exception.Message) }
    Write-Result 'rolledBack' $reason
    Start-Game
    exit 1
}

foreach ($name in $oldFiles) {
    if (-not $newSet.Contains($name)) { Write-Log ('removed ' + $name) }
}
Write-Log ('moved out ' + $moved.Count + ' game files, placed ' + $placed.Count)
Write-Result 'succeeded' ''
# The backup holds only the game's old files; the staging folder only what the update left in it.
try { [System.IO.Directory]::Delete($backup, $true) }
catch { Write-Log ('could not delete the backup: ' + $_.Exception.Message) }
try { [System.IO.Directory]::Delete($staging, $true) }
catch { Write-Log ('could not delete the staging folder: ' + $_.Exception.Message) }
Remove-EmptyFolders $install (Get-Folders $oldFiles)
Start-Game
exit 0
