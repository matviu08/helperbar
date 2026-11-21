$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish .\HelperBar.csproj -c Release --no-self-contained -o .\dist\HelperBar --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Host 'Ready: dist\HelperBar\HelperBar.exe'
} finally {
    Pop-Location
}
