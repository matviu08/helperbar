param(
    [string]$CompilerPath,
    [switch]$InstallerTest
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location $PSScriptRoot
try {
    if (-not $CompilerPath) {
        $candidates = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
        )
        $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $CompilerPath) { throw 'Install Inno Setup 7 or provide -CompilerPath to ISCC.exe.' }
    }
    $CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
    [xml]$project = Get-Content -LiteralPath HelperBar.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    $buildId = $version + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $payload = Join-Path $PSScriptRoot "artifacts\package\$buildId"
    $output = Join-Path $PSScriptRoot ('artifacts\' + $(if ($InstallerTest) { 'installer-qa' } else { 'installer' }))
    New-Item -ItemType Directory -Path $payload, $output -Force | Out-Null

    # Always publish into a fresh directory, never package a developer's dist/data folder.
    dotnet publish .\HelperBar.csproj -c Release -r win-x64 --self-contained true --nologo -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    $runtime = Get-Content -LiteralPath (Join-Path $payload 'HelperBar.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'The payload is not self-contained.' }
    foreach ($required in @('HelperBar.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing runtime file: $required" }
    }
    # Carry the notices from the exact NuGet runtime packs used by this publish.
    $assets = Get-Content -LiteralPath 'obj\project.assets.json' -Raw | ConvertFrom-Json
    $deps = Get-Content -LiteralPath (Join-Path $payload 'HelperBar.deps.json') -Raw | ConvertFrom-Json
    foreach ($dependency in $deps.libraries.PSObject.Properties.Name | Where-Object { $_.StartsWith('runtimepack.') }) {
        $package = $dependency.Substring('runtimepack.'.Length).ToLowerInvariant()
        $packageDirectory = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $package } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $packageDirectory) { throw "Cannot locate notices for $package" }
        $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\..*)?$' -or $_.Extension -eq '.nuspec' })
        $noticeDirectory = Join-Path $payload ('licenses\' + $package.Replace('/', '-'))
        New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $noticeDirectory }
    }
    $test = Start-Process -FilePath (Join-Path $payload 'HelperBar.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Self-tests failed; see $payload\qa\self-test.txt" }

    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object {
        $relative = $_.FullName.Substring($payload.Length + 1)
        $relative -notmatch '^(qa|data)\\' -and $_.Extension -notin @('.pdb', '.log')
    } | Sort-Object FullName)
    $manifest = @($files | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($payload.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'payload-manifest.json') -Encoding UTF8
    $arguments = @('/Q', "/DAppVersion=$version", "/DPayloadDir=$payload", "/DOutputDir=$output")
    if ($InstallerTest) { $arguments += '/DInstallerTest=1' }
    & $CompilerPath @arguments .\installer\HelperBar.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $name = if ($InstallerTest) { 'HelperBar-Setup-QA-x64.exe' } else { 'HelperBar-Setup-x64.exe' }
    $installer = Join-Path $output $name
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
    [ordered]@{ version = $version; architecture = 'win-x64'; selfContained = $true; signed = $false; file = $name; bytes = (Get-Item -LiteralPath $installer).Length; sha256 = $hash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding UTF8
    Write-Host "Ready: $installer"
} finally { Pop-Location }
