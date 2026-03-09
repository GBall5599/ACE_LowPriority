param(
    [switch]$NoPrompt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ReadableProcessPath {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    try {
        if ($Process.Path) {
            return $Process.Path
        }
    }
    catch {
    }

    try {
        return $Process.MainModule.FileName
    }
    catch {
        return $null
    }
}

function Get-TargetProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    $processName = [System.IO.Path]::GetFileNameWithoutExtension($ExecutablePath)
    $processes = @(
        Get-Process -Name $processName -ErrorAction SilentlyContinue
    )

    if ($processes.Count -eq 0) {
        throw "Target process is not running: $ExecutablePath"
    }

    $exactPathMatches = New-Object System.Collections.Generic.List[System.Diagnostics.Process]
    $unknownPathMatches = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

    foreach ($process in $processes) {
        $resolvedPath = Get-ReadableProcessPath -Process $process
        if (-not $resolvedPath) {
            [void]$unknownPathMatches.Add($process)
            continue
        }

        if ($resolvedPath -ieq $ExecutablePath) {
            [void]$exactPathMatches.Add($process)
        }
    }

    if ($exactPathMatches.Count -gt 0) {
        return @($exactPathMatches)
    }

    if ($unknownPathMatches.Count -gt 0) {
        return @($unknownPathMatches)
    }

    throw "Found process name '$processName', but its path did not match: $ExecutablePath"
}

function Set-TargetProcessState {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [long]$AffinityMask
    )

    $Process.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Idle
    $Process.ProcessorAffinity = [IntPtr]$AffinityMask
    $Process.Refresh()
}

$targets = @(
    'C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe',
    'C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe'
)

$exitCode = 1
$delegatedToElevatedProcess = $false

try {
    if (-not (Test-IsAdministrator)) {
        $elevationArguments = @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', "`"$PSCommandPath`""
        )

        if ($NoPrompt) {
            $elevationArguments += '-NoPrompt'
        }

        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $elevationArguments | Out-Null
        $delegatedToElevatedProcess = $true
        $exitCode = 0
        return
    }

    $logicalProcessorCount = [Environment]::ProcessorCount
    if ($logicalProcessorCount -lt 1) {
        throw 'No logical processors were detected.'
    }

    if ($logicalProcessorCount -gt 64) {
        throw "This script supports up to 64 logical processors, but detected $logicalProcessorCount."
    }

    $lastProcessorIndex = $logicalProcessorCount - 1
    $affinityMask = ([long]1 -shl $lastProcessorIndex)

    Write-Host 'Running as administrator.'
    Write-Host "Target logical processor: CPU $lastProcessorIndex"
    Write-Host 'Priority target: low priority (Idle)'
    Write-Host ''

    $resolvedTargets = foreach ($targetPath in $targets) {
        [PSCustomObject]@{
            Path = $targetPath
            Processes = @(Get-TargetProcesses -ExecutablePath $targetPath)
        }
    }

    foreach ($target in $resolvedTargets) {
        foreach ($process in $target.Processes) {
            Set-TargetProcessState -Process $process -AffinityMask $affinityMask
            Write-Host ("SUCCESS: {0} (PID {1}) -> Idle priority, CPU {2} only" -f $target.Path, $process.Id, $lastProcessorIndex) -ForegroundColor Green
        }
    }

    Write-Host ''
    Write-Host 'All target processes were updated successfully.' -ForegroundColor Green
    $exitCode = 0
}
catch {
    Write-Host ''
    Write-Host ("FAILED: {0}" -f $_.Exception.Message) -ForegroundColor Red
    $exitCode = 1
}
finally {
    if (-not $NoPrompt -and -not $delegatedToElevatedProcess) {
        Write-Host ''
        [void](Read-Host 'Press Enter to exit')
    }
}

exit $exitCode
