$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet restore PcDashboard.slnx --locked-mode -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet run --project tests/PcDashboard.Tests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Behavioral checks failed.' }
    dotnet publish src/PcDashboard.Desktop -c Release --no-restore --self-contained false -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination artifacts/win-x64/README.md
    Write-Output 'Release ready in artifacts/win-x64. Keep all files together.'
} finally { Pop-Location }
