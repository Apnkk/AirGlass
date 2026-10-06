# Builds the release installer: publish (single-file, self-contained) then Inno Setup.
# Usage (from repo root):  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $PSScriptRoot)

Write-Host "==> dotnet publish"
dotnet publish AirGlass.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php then re-run."
    exit 1
}

Write-Host "==> Inno Setup"
& $iscc "Installer\AirGlass.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "Done: Installer\Output"
