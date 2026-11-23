param([string]$InstallerPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $InstallerPath) { $InstallerPath = Join-Path $root 'artifacts\installer-qa\HelperBar-Setup-QA-x64.exe' }
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
if ([IO.Path]::GetFileName($InstallerPath) -ne 'HelperBar-Setup-QA-x64.exe') { throw 'Only the QA installer may be tested with this script.' }
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{377B6959-994F-4E0F-982A-A441808379BA}_is1'
if (Test-Path -LiteralPath $registry) { throw 'Uninstall the previous HelperBar Installer QA copy first.' }
if (Get-Process -Name HelperBar -ErrorAction SilentlyContinue) { throw 'Close HelperBar before running installer tests.' }
$testDirectory = Join-Path $root ('artifacts\installer-tests\' + [Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $testDirectory 'app'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$report = New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$condition, [string]$label) {
    if (-not $condition) { throw $label }
    $report.Add("PASS $label")
}
function FileHashOrMissing([string]$path) {
    if (Test-Path -LiteralPath $path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    return 'missing'
}
function Install([string]$log) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/LANG=ukrainian', '/TASKS=desktopicon', ('/DIR="' + $installDirectory + '"'), ('/LOG="' + $log + '"'))
    $process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'Installer completes without restart or elevation'
}
$sharedSettings = Join-Path $env:LOCALAPPDATA 'HelperBar\data\settings.json'
$initialHash = FileHashOrMissing $sharedSettings
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'HelperBar Installer QA.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'HelperBar Installer QA.lnk'
try {
    Install (Join-Path $testDirectory 'fresh.log')
    Check (Test-Path -LiteralPath $registry) 'Per-user uninstall registration exists'
    Check ((Get-ItemProperty -LiteralPath $registry).InstallLocation.TrimEnd('\') -eq $installDirectory) 'Registration points only to the QA installation'
    Check ((Test-Path -LiteralPath $startLink) -and (Test-Path -LiteralPath $desktopLink)) 'Start menu and optional desktop shortcuts exist'
    Check (-not ((Test-Path -LiteralPath "$installDirectory\data") -or (Test-Path -LiteralPath "$installDirectory\qa"))) 'No personal data or QA artifacts are shipped'
    $manifest = Get-Content -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($InstallerPath)) 'payload-manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest) {
        $path = Join-Path $installDirectory $entry.path
        if ((FileHashOrMissing $path) -ne $entry.sha256) { throw "Payload hash mismatch: $($entry.path)" }
    }
    Check ($manifest.Count -gt 100) 'Every installed payload file matches the build manifest'
    $executable = Join-Path $installDirectory 'HelperBar.exe'
    $process = Start-Process -FilePath $executable -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0) 'Installed self-contained executable passes all model and storage tests'
    $process = Start-Process -FilePath $executable -ArgumentList '--smoke-test','--inspect' -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath "$installDirectory\qa\smoke-test.txt")) 'Installed WPF app renders every page and settings section'
    $marker = Join-Path $installDirectory 'data\installer-test-marker.txt'
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($marker)) -Force | Out-Null
    [IO.File]::WriteAllText($marker, 'Unowned data must survive repair and uninstall.')
    $markerHash = FileHashOrMissing $marker
    Install (Join-Path $testDirectory 'repair.log')
    Check ((FileHashOrMissing $marker) -eq $markerHash) 'Same-version repair preserves unowned data'
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $testDirectory 'uninstall.log') + '"') -WindowStyle Hidden -Wait -PassThru
    Check ($process.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $executable)) 'Uninstall removes the application successfully'
    Check (-not ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $startLink) -or (Test-Path -LiteralPath $desktopLink))) 'Uninstall removes its registry entry and both shortcuts'
    Check ((FileHashOrMissing $marker) -eq $markerHash) 'Uninstall retains unowned personal files'
    Check ((FileHashOrMissing $sharedSettings) -eq $initialHash) 'Real user settings remain unchanged throughout testing'
} catch {
    $report.Add('FAIL ' + $_.Exception.Message)
    throw
} finally {
    $report | Set-Content -LiteralPath (Join-Path $testDirectory 'results.txt') -Encoding UTF8
    Write-Host ($report -join [Environment]::NewLine)
    Write-Host "Report: $testDirectory"
}
