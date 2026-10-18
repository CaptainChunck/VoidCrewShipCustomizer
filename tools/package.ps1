# Builds the Release DLL and packs the Thunderstore upload:
#   dist\VoidCrewShipCustomizer-<version>.zip  with manifest.json, icon.png, README.md, CHANGELOG.md and the DLL at its root.
# Usage: powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-NoBuild]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$version = (Select-String -Path (Join-Path $root "VoidCrewShipCustomizer\Plugin.cs") -Pattern 'Version = "([0-9]+\.[0-9]+\.[0-9]+)"').Matches[0].Groups[1].Value
$manifest = Get-Content (Join-Path $root "thunderstore\manifest.json") -Raw | ConvertFrom-Json
if ($manifest.version_number -ne $version) {
    throw "thunderstore\manifest.json says $($manifest.version_number) but Plugin.cs says $version; make them match."
}

if (-not $NoBuild) {
    $msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    & $msbuild (Join-Path $root "VoidCrewShipCustomizer\VoidCrewShipCustomizer.csproj") -p:Configuration=Release -nologo -v:minimal -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
}

$dll = Join-Path $root "VoidCrewShipCustomizer\bin\Release\VoidCrewShipCustomizer.dll"
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "VoidCrewShipCustomizer-package"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $root "thunderstore\manifest.json"), (Join-Path $root "thunderstore\icon.png"), (Join-Path $root "thunderstore\README.md"), (Join-Path $root "CHANGELOG.md"), $dll -Destination $stage

$dist = Join-Path $root "dist"
New-Item -ItemType Directory $dist -Force | Out-Null
$zip = Join-Path $dist "VoidCrewShipCustomizer-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Copy-Item $dll (Join-Path $dist "VoidCrewShipCustomizer.dll") -Force
Remove-Item $stage -Recurse -Force
Write-Output "Packed $zip"
