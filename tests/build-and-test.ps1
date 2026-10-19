# Builds the plugin (Release) and runs the offline checks against the real game assemblies, without starting the game:
# every game member the plugin looks up by name must exist, the hardpoint rules must give the expected answers, and a
# saved CUSTOM loadout must survive a save and load. Success ends with "ALL CHECKS PASSED" and exit code 0.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tests\build-and-test.ps1 [-VoidCrewDir <game folder>] [-BepInExDir <BepInEx folder>] [-FleetDll <VoidCrewFleet.dll>]
param(
    [string]$VoidCrewDir = "G:\SteamLibrary\steamapps\common\Void Crew",
    [string]$BepInExDir = (Join-Path $env:APPDATA "raicuparta\rai-pal\data\installed-mods\3743495372862092943\bepinex\BepInEx"),
    # Void Crew Fleet's API is checked too when its DLL is there (by default a Release build next to this repository).
    [string]$FleetDll = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "VoidCrewFleet\VoidCrewFleet\bin\Release\VoidCrewFleet.dll")
)

$root = Split-Path $PSScriptRoot -Parent
$vs = "C:\Program Files\Microsoft Visual Studio\18\Community"
$msbuild = Join-Path $vs "MSBuild\Current\Bin\MSBuild.exe"
$csc = Join-Path $vs "MSBuild\Current\Bin\Roslyn\csc.exe"
# Unity's copy of Newtonsoft.Json fails strong-name validation on desktop .NET, so the save-format test loads Visual
# Studio's copy of the same version instead.
$desktopJson = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\NuGet"

Write-Output "== Building the plugin (Release)"
& $msbuild (Join-Path $root "VoidCrewShipCustomizer\VoidCrewShipCustomizer.csproj") -p:Configuration=Release -nologo -v:minimal -t:Rebuild "-p:VoidCrewDir=$VoidCrewDir" "-p:BepInExDir=$BepInExDir"
if ($LASTEXITCODE -ne 0) {
    Write-Output "BUILD FAILED"
    exit 1
}

Write-Output "== Building the offline checker"
$out = Join-Path ([System.IO.Path]::GetTempPath()) "VoidCrewShipCustomizer-checks"
New-Item -ItemType Directory $out -Force | Out-Null
& $csc -nologo -target:exe -platform:x64 "-r:$(Join-Path $BepInExDir 'core\Mono.Cecil.dll')" "-out:$(Join-Path $out 'ShipCustomizerCheck.exe')" (Join-Path $PSScriptRoot "OfflineChecks\ShipCustomizerCheck.cs")
if ($LASTEXITCODE -ne 0) {
    Write-Output "CHECKER BUILD FAILED"
    exit 1
}

Write-Output "== Running the offline checks"
& (Join-Path $out "ShipCustomizerCheck.exe") (Join-Path $root "VoidCrewShipCustomizer\bin\Release") (Join-Path $VoidCrewDir "Void Crew_Data\Managed") (Join-Path $BepInExDir "core") $desktopJson $FleetDll
exit $LASTEXITCODE
