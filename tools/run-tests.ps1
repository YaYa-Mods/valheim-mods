# Tests d'intégration en jeu : déploie le harnais, arme AutoRun, lance Valheim (QuickStart charge la dernière partie),
# attend la fin des tests, ferme le jeu, affiche les résultats, puis retire le harnais des plugins.
# Prérequis : jeu fermé, QuickStart.AutoLoadLastWorld = true (sinon le monde n'est jamais chargé).
param([switch]$Quick)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$root = Split-Path $PSScriptRoot -Parent
$V = if ($env:VALHEIM_DIR) { $env:VALHEIM_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Valheim" }  # dossier du jeu : VALHEIM_DIR sinon Steam par defaut
$plugin = "$V\BepInEx\plugins\TestHarness.dll"
$cfg = "$V\BepInEx\config\vmods.testharness.cfg"

if (Get-Process valheim -ErrorAction SilentlyContinue) { throw "Valheim est ouvert : ferme-le d'abord." }

# Le harnais joue UNIQUEMENT sur son personnage/monde de test (ModTester / ModTestWorld, sauvegardes locales) : QuickStart est
# configuré pour les charger par leur nom pendant le run, puis la configuration du joueur est restaurée telle quelle.
$qsCfg = "$V\BepInEx\config\vmods.quickstart.cfg"
$qsBackup = "$qsCfg.avant-tests"
function Set-CfgKey([string]$path, [string]$key, [string]$value) {
    $lines = if (Test-Path $path) { Get-Content $path -Encoding UTF8 } else { @('[General]') }
    $done = $false
    $lines = $lines | ForEach-Object { if ($_ -match "^\s*$key\s*=") { $done = $true; "$key = $value" } else { $_ } }
    if (-not $done) { $lines += "$key = $value" }
    Set-Content -Path $path -Value $lines -Encoding UTF8
}
if (-not (Test-Path $qsBackup)) { if (Test-Path $qsCfg) { Copy-Item $qsCfg $qsBackup -Force } } else { Write-Host "Sauvegarde de config QuickStart trouvee (run precedent interrompu) : conservee." -ForegroundColor Yellow }
try {
    Set-CfgKey $qsCfg 'AutoLoadLastWorld' 'true'
    Set-CfgKey $qsCfg 'AutoLoadCharacter' 'ModTester'
    Set-CfgKey $qsCfg 'AutoLoadWorld' 'ModTestWorld'
    
    dotnet build "$root\mods\TestHarness" -c Release -p:Deploy=true | Out-Null
    if (-not (Test-Path $plugin)) { throw "Harnais non déployé" }
    Set-Content -Encoding ascii $cfg "[General]`r`nAutoRun = true`r`nQuick = $(if ($Quick) { 'true' } else { 'false' })`r`n"
    
    if (Test-Path "$V\BepInEx\LogOutput.log") { Copy-Item "$V\BepInEx\LogOutput.log" "$V\BepInEx\LogOutput.prev.log" -Force }
    $p = Start-Process -FilePath "$V\valheim.exe" -WorkingDirectory $V -PassThru
    $deadline = (Get-Date).AddSeconds(600)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        if ((Test-Path "$V\BepInEx\LogOutput.log") -and (Select-String -Path "$V\BepInEx\LogOutput.log" -Pattern "\[TEST\] ===== fin|rien n.est charg|REFUS|impossible . pr.parer" -Quiet)) { break }
    }
    Start-Sleep -Seconds 2
    Get-Process valheim -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    
    Write-Host ""
    Get-Content "$V\BepInEx\LogOutput.log" -Encoding UTF8 | Select-String -Pattern "\[TEST\]" | ForEach-Object {
        $line = $_.Line -replace '^\[Info\s*:Test Harness\] ', ''
        if ($line -match 'FAIL') { Write-Host $line -ForegroundColor Red } elseif ($line -match 'PASS') { Write-Host $line -ForegroundColor Green } else { Write-Host $line }
    }
    Get-Content "$V\BepInEx\LogOutput.log" -Encoding UTF8 | Select-String -Pattern "Error|Exception" | Select-Object -First 5 | ForEach-Object { Write-Host $_.Line -ForegroundColor Yellow }
    
}
finally {
    # Toujours executee, meme si le script est interrompu : le jeu du joueur ne doit jamais redemarrer sur le personnage de test
    Get-Process valheim -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item $plugin -Force -ErrorAction SilentlyContinue
    if (Test-Path $cfg) { Set-Content -Encoding ascii $cfg "[General]`r`nAutoRun = false`r`nQuick = false`r`n" }
    if (Test-Path $qsBackup) { Move-Item $qsBackup $qsCfg -Force; Write-Host "Configuration QuickStart du joueur restauree." -ForegroundColor DarkGray } else { Set-CfgKey $qsCfg 'AutoLoadCharacter' ''; Set-CfgKey $qsCfg 'AutoLoadWorld' '' }
    Write-Host "Harnais retire des plugins." -ForegroundColor DarkGray
}
