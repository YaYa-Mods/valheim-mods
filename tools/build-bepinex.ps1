# Compile BepInEx depuis tools\BepInEx-src avec `dotnet build` (on n'utilise PAS build.cake,
# qui télécharge des binaires Doorstop), puis assemble tools\dist : l'ensemble exact à copier
# dans le dossier Valheim. Prérequis : .NET SDK 8, tools\UnityDoorstop-src\build\winhttp.dll
# déjà compilé par build-doorstop.ps1.
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$src = Join-Path $PSScriptRoot 'BepInEx-src'
$dist = Join-Path $PSScriptRoot 'dist'

dotnet build (Join-Path $src 'BepInEx.Preloader\BepInEx.Preloader.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build BepInEx échoué' }

$out = Join-Path $src 'BepInEx.Preloader\bin\Release\net35'
Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$dist\BepInEx\core", "$dist\BepInEx\plugins", "$dist\BepInEx\config" | Out-Null
Copy-Item "$out\*.dll", "$out\*.xml" "$dist\BepInEx\core\"
Copy-Item (Join-Path $PSScriptRoot 'UnityDoorstop-src\build\winhttp.dll') $dist
Copy-Item (Join-Path $src 'doorstop\doorstop_config.ini') $dist
Set-Content -NoNewline (Join-Path $dist '.doorstop_version') '4.5.0'
# Console BepInEx cachée (le log reste écrit dans BepInExogoutput.log).
Set-Content (Join-Path $dist 'BepInEx\config\BepInEx.cfg') @"
[Logging.Console]
Enabled = false

[Logging.Disk]
WriteUnityLog = false
"@
Write-Host "OK -> $dist  (copier son contenu dans le dossier Valheim)"
Get-ChildItem $dist -Recurse -File | Get-FileHash -Algorithm SHA256 | Format-Table -AutoSize
