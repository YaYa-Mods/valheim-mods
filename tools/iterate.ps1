# Boucle d'itération visuelle : lance les tests rapides (captures d'écran comprises) et rassemble les captures
# dans docs\iter\<n>\ pour les regarder. Usage : tools\iterate.ps1 -N 3
param([int]$N = 0)
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$cfg = (if ($env:VALHEIM_DIR) { $env:VALHEIM_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Valheim" }) + "\BepInEx\config"
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw "Valheim est ouvert : ferme-le d'abord." }
$out = powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\run-tests.ps1" -Quick 2>&1
$out | Select-String -Pattern "FAIL|fin :" | ForEach-Object { $_.Line }
$dir = Join-Path $root "docs\iter\$N"
New-Item -ItemType Directory -Force $dir | Out-Null
foreach ($f in 'finder_window.png','finder_scan.png','finder_hud.png','finder_hud_far.png','finder_map.png','guide_window.png','guide_tracker.png','guide_tracker_compact.png','hub_window.png','radial_mods.png','inventory_scroll.png','inventory_filter.png','inventory_buttons.png','home_confirm.png','keyhints.png') {
    if (Test-Path "$cfg\$f") { Copy-Item "$cfg\$f" "$dir\$f" -Force }
}
"captures : $dir"
