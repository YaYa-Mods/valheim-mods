# Copie datée des mondes et personnages Valheim dans E:\Mods\Valheim\backups, en gardant les N dernières.
# Utilisé par launch-valheim.ps1 avant chaque lancement ; peut aussi être lancé seul.
param([int]$Keep = 5)
$ErrorActionPreference = 'Stop'

$source = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'backups'
New-Item -ItemType Directory -Force $root | Out-Null

$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$dest = Join-Path $root $stamp
foreach ($sub in 'worlds_local', 'characters_local') {
    $from = Join-Path $source $sub
    if (Test-Path $from) { Copy-Item $from (Join-Path $dest $sub) -Recurse -Force }
}
if (Test-Path $dest) { Write-Host "Sauvegarde : $dest" } else { Write-Host "Rien à sauvegarder ($source introuvable)"; exit 0 }

# Ne garder que les $Keep plus récentes
Get-ChildItem $root -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force
    Write-Host "Ancienne sauvegarde supprimée : $($_.Name)"
}
