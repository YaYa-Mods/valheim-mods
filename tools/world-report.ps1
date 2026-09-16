# Rapport du monde courant (dernière partie jouée) : lieux clés, biomes et côte autour des pierres sacrificielles.
# Jeu fermé requis ; QuickStart.AutoLoadLastWorld = true. Déploie le harnais, lance, lit le rapport, ferme, retire le harnais.
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$root = Split-Path $PSScriptRoot -Parent
$V = if ($env:VALHEIM_DIR) { $env:VALHEIM_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Valheim" }  # dossier du jeu : VALHEIM_DIR sinon Steam par defaut
$plugin = "$V\BepInEx\plugins\TestHarness.dll"
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw "Valheim est ouvert : ferme-le d'abord." }
dotnet build "$root\mods\TestHarness" -c Release -p:Deploy=true | Out-Null
Set-Content -Encoding ascii "$V\BepInEx\config\vmods.testharness.cfg" "[General]`r`nAutoRun = false`r`nWorldReport = true`r`n"
if (Test-Path "$V\BepInEx\LogOutput.log") { Copy-Item "$V\BepInEx\LogOutput.log" "$V\BepInEx\LogOutput.prev.log" -Force }
$p = Start-Process -FilePath "$V\valheim.exe" -WorkingDirectory $V -PassThru
$deadline = (Get-Date).AddSeconds(240)
while ((Get-Date) -lt $deadline) { Start-Sleep -Seconds 4; if ((Test-Path "$V\BepInEx\LogOutput.log") -and (Select-String -Path "$V\BepInEx\LogOutput.log" -Pattern "\[MONDE\] ===== fin" -Quiet)) { break } }
Start-Sleep -Seconds 2
Get-Process valheim -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Get-Content "$V\BepInEx\LogOutput.log" -Encoding UTF8 | Select-String -Pattern "\[MONDE\]" | ForEach-Object { $_.Line -replace '^\[Info\s*:Test Harness\] \[MONDE\] ', '' }
Remove-Item $plugin -Force
