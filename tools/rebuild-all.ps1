# Recompile et redéploie tous les mods (mods\*\*.csproj) dans BepInEx\plugins.
# À lancer après une mise à jour de Valheim : si le jeu a renommé une méthode, la compilation échoue
# en nommant le mod fautif, au lieu d'une erreur en jeu.
$ErrorActionPreference = 'Continue'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$mods = Join-Path (Split-Path $PSScriptRoot -Parent) 'mods'
$ok = @(); $ko = @()

foreach ($proj in Get-ChildItem $mods -Recurse -Filter *.csproj | Sort-Object Name) {
    Write-Host "== $($proj.BaseName)" -ForegroundColor Cyan
    $out = & dotnet build $proj.FullName -c Release -p:Deploy=true 2>&1
    if ($LASTEXITCODE -eq 0) { $ok += $proj.BaseName }
    else {
        $ko += $proj.BaseName
        $out | Where-Object { $_ -match 'error' } | Select-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    }
}

Write-Host ""
Write-Host "OK    : $($ok -join ', ')" -ForegroundColor Green
if ($ko.Count -gt 0) { Write-Host "ÉCHEC : $($ko -join ', ')" -ForegroundColor Red; exit 1 }
