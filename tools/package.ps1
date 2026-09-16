# Paquets de publication : pour chaque mod (sauf le harnais de test), un zip Thunderstore (manifest.json, icon.png 256x256,
# README.md, DLL) et un zip Nexus / installation manuelle (BepInEx/plugins/<Mod>.dll + README.md), dans dist/.
# Les icones sont dessinees ici (fond sombre + glyphe du mod, repris des icones du menu radial quand il y en a une).
# Usage : tools\package.ps1 [-NoBuild]   (VALHEIM_DIR ou Directory.Build.props.user necessaire pour compiler)
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$repo = 'https://github.com/YaYa-Mods/valheim-mods'
$bepinex = 'denikson-BepInExPack_Valheim-5.4.2350'

# Nom Thunderstore (sans espace), description courte (250 caracteres max), icone : fichier du menu radial ou glyphe dessine
$mods = @(
    @{ Dir = 'ResourceFinder'; Name = 'ResourceFinder'; Icon = 'ResourceFinder\Icons\scanner.png'
       Desc = 'Scanner for ores, pickables, trees, locations and creatures: map pins, on-screen target, track mode that chains to the next one. UI follows the game language (EN/FR).' }
    @{ Dir = 'Guide'; Name = 'Guide'; Icon = 'Guide\Icons\guide.png'
       Desc = 'Progression guide, one chapter per boss, steps that tick themselves from what the game records, on-screen quest tracker (full/compact/hidden), anti-spoiler. EN/FR.' }
    @{ Dir = 'ModHub'; Name = 'ModHub'; Icon = 'ModHub\Icons\config.png'
       Desc = 'In-game configuration of every BepInEx mod (F9), a Mods group in the action wheel, mod buttons in the inventory and mod key hints in the game hint bar. EN/FR.' }
    @{ Dir = 'Inventory'; Name = 'Inventory'; Icon = 'Inventory\Icons\sort.png'
       Desc = 'Bigger scrollable inventory, sort and stack buttons, category filter, no weight limit, bigger stacks, keep inventory and skills on death, tombstone recovery. EN/FR.' }
    @{ Dir = 'CraftFromChests'; Name = 'CraftFromChests'; Glyph = 'chest'
       Desc = 'Crafting, building and stations (smelter, oven, fermenter...) use materials from nearby chests as if they were in your inventory. Configurable range.' }
    @{ Dir = 'HomeTeleport'; Name = 'HomeTeleport'; Icon = 'HomeTeleport\Icons\home.png'
       Desc = 'One key (F8) to teleport back to your bed, with confirmation, refused while an enemy targets you, optional cooldown. EN/FR.' }
    @{ Dir = 'Lumberjack'; Name = 'Lumberjack'; Glyph = 'tree'
       Desc = 'The Woodcutting skill matters: high level fells trees in one hit, and fallen logs and stumps cut themselves up. Tool tiers still apply.' }
    @{ Dir = 'LongerFood'; Name = 'LongerFood'; Glyph = 'food'
       Desc = 'Food with positive effects lasts longer (multiplier, 1.5 by default). Potions and meads untouched, vanilla values restored when disabled.' }
    @{ Dir = 'NoDurability'; Name = 'NoDurability'; Glyph = 'shield'
       Desc = 'Weapons, tools, armour and torches never wear out. Can be disabled on the fly, vanilla values are kept.' }
    @{ Dir = 'ShortNights'; Name = 'ShortNights'; Glyph = 'moon'
       Desc = 'Shorter nights without changing the length of the day/night cycle (night share configurable, 0.15 by default instead of 0.30).' }
    @{ Dir = 'Movement'; Name = 'Movement'; Glyph = 'chevrons'
       Desc = 'Faster walking, jogging, running and swimming for the local player (multipliers, 1.3 by default).' }
    @{ Dir = 'QuickStart'; Name = 'QuickStart'; Glyph = 'play'
       Desc = 'Skip the intro cinematic and optionally auto-load the last character into the last world at launch.' }
)

function Get-Version([string]$dir) {
    $csproj = Get-ChildItem (Join-Path $root "mods\$dir") -Filter *.csproj | Select-Object -First 1
    $m = [regex]::Match([IO.File]::ReadAllText($csproj.FullName), '<Version>([^<]+)</Version>')
    if (-not $m.Success) { throw "pas de <Version> dans $($csproj.Name)" }
    $plugin = [regex]::Match([IO.File]::ReadAllText((Join-Path $root "mods\$dir\Plugin.cs")), 'BepInPlugin\(Guid, "[^"]+", "([^"]+)"\)')
    if ($plugin.Success -and $plugin.Groups[1].Value -ne $m.Groups[1].Value) { Write-Warning "$dir : version csproj $($m.Groups[1].Value) differente de BepInPlugin $($plugin.Groups[1].Value)" }
    return $m.Groups[1].Value
}

function Draw-Glyph([System.Drawing.Graphics]$g, [string]$glyph) {
    # Glyphes 128x128 (meme style que make-icons.ps1 : ombre sombre puis trait clair), dessines dans le repere courant
    foreach ($pass in 0, 1) {
        $col = if ($pass -eq 0) { [System.Drawing.Color]::FromArgb(170, 10, 8, 6) } else { [System.Drawing.Color]::FromArgb(255, 245, 240, 225) }
        $pen = New-Object System.Drawing.Pen $col, $(if ($pass -eq 0) { 18 } else { 9 })
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $brush = New-Object System.Drawing.SolidBrush $col
        switch ($glyph) {
            'chest'    { $g.DrawRectangle($pen, 20, 44, 88, 64); $g.DrawLine($pen, 20, 66, 108, 66); $g.DrawArc($pen, 20, 22, 88, 44, 180, 180); $g.FillEllipse($brush, 56, 70, 16, 16) }
            'tree'     { $g.DrawLine($pen, 64, 12, 30, 56); $g.DrawLine($pen, 30, 56, 50, 56); $g.DrawLine($pen, 50, 56, 22, 92); $g.DrawLine($pen, 22, 92, 106, 92); $g.DrawLine($pen, 106, 92, 78, 56); $g.DrawLine($pen, 78, 56, 98, 56); $g.DrawLine($pen, 98, 56, 64, 12); $g.DrawLine($pen, 64, 94, 64, 116) }
            'food'     { $g.DrawArc($pen, 20, 56, 88, 60, 0, 180); $g.DrawLine($pen, 16, 60, 112, 60); $g.DrawArc($pen, 44, 18, 16, 30, 90, 180); $g.DrawArc($pen, 68, 18, 16, 30, 90, 180) }
            'shield'   { $g.DrawLine($pen, 28, 26, 64, 16); $g.DrawLine($pen, 64, 16, 100, 26); $g.DrawArc($pen, 28, 8, 72, 100, 0, 180); $g.DrawLine($pen, 28, 26, 28, 58); $g.DrawLine($pen, 100, 26, 100, 58); $g.DrawLine($pen, 46, 62, 60, 78); $g.DrawLine($pen, 60, 78, 86, 46) }
            'moon'     { $g.DrawArc($pen, 24, 20, 84, 84, 60, 240); $g.DrawArc($pen, 48, 20, 60, 84, 60, 240) }
            'chevrons' { $g.DrawLine($pen, 30, 28, 62, 64); $g.DrawLine($pen, 62, 64, 30, 100); $g.DrawLine($pen, 66, 28, 98, 64); $g.DrawLine($pen, 98, 64, 66, 100) }
            'play'     { $g.DrawPolygon($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(26, 24), [System.Drawing.Point]::new(70, 64), [System.Drawing.Point]::new(26, 104))); $g.DrawPolygon($pen, [System.Drawing.Point[]]@([System.Drawing.Point]::new(70, 24), [System.Drawing.Point]::new(110, 64), [System.Drawing.Point]::new(70, 104))) }
        }
        $pen.Dispose(); $brush.Dispose()
    }
}

function New-StoreIcon([hashtable]$mod, [string]$out) {
    $S = 256
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    # Fond : degrade brun sombre, liseret ambre (les couleurs des fenetres des mods)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Rectangle]::new(0, 0, $S, $S)), ([System.Drawing.Color]::FromArgb(255, 46, 38, 30)), ([System.Drawing.Color]::FromArgb(255, 18, 14, 11)), 70
    $g.FillRectangle($grad, 0, 0, $S, $S); $grad.Dispose()
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 200, 140, 60)), 6
    $g.DrawRectangle($border, 3, 3, $S - 7, $S - 7); $border.Dispose()
    $inner = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 245, 168, 71)), 2
    $g.DrawRectangle($inner, 12, 12, $S - 25, $S - 25); $inner.Dispose()
    # Glyphe : 176 px au centre
    $scale = 176.0 / 128.0
    if ($mod.Icon) {
        $src = [System.Drawing.Image]::FromFile((Join-Path $root "mods\$($mod.Icon)"))
        $g.DrawImage($src, [System.Drawing.Rectangle]::new(40, 40, 176, 176)); $src.Dispose()
    } else {
        $g.TranslateTransform(40, 40); $g.ScaleTransform($scale, $scale)
        Draw-Glyph $g $mod.Glyph
        $g.ResetTransform()
    }
    $g.Dispose()
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

function New-Zip([string]$zipPath, [hashtable]$entries) {
    # entries : chemin dans le zip (avec des /) -> fichier source. Ecrit avec ZipArchive pour garantir des separateurs /
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $fs = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
    $zip = New-Object System.IO.Compression.ZipArchive $fs, ([System.IO.Compression.ZipArchiveMode]::Create)
    foreach ($k in $entries.Keys) {
        $e = $zip.CreateEntry($k, [System.IO.Compression.CompressionLevel]::Optimal)
        $in = [IO.File]::OpenRead($entries[$k]); $out = $e.Open(); $in.CopyTo($out); $out.Dispose(); $in.Dispose()
    }
    $zip.Dispose(); $fs.Dispose()
}

if (-not $NoBuild) {
    foreach ($m in $mods) {
        Write-Host "== compilation $($m.Dir)"
        dotnet build (Join-Path $root "mods\$($m.Dir)") -c Release -nologo -v q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "compilation echouee : $($m.Dir)" }
    }
}

New-Item -ItemType Directory -Force "$dist\thunderstore", "$dist\nexus", "$dist\work" | Out-Null
$summary = @()
foreach ($m in $mods) {
    $ver = Get-Version $m.Dir
    $dll = Join-Path $root "mods\$($m.Dir)\bin\Release\$($m.Dir).dll"
    if (-not (Test-Path $dll)) { throw "DLL absente : $dll (compiler d'abord)" }
    $readme = Join-Path $root "mods\$($m.Dir)\README.md"
    $work = Join-Path $dist "work\$($m.Dir)"
    New-Item -ItemType Directory -Force $work | Out-Null

    # Thunderstore : manifest, icone, README (celui du mod, plus la ligne d'installation), DLL a la racine
    $manifest = [ordered]@{
        name = $m.Name; version_number = $ver; website_url = $repo; description = $m.Desc; dependencies = @($bepinex)
    } | ConvertTo-Json
    [IO.File]::WriteAllText("$work\manifest.json", $manifest, (New-Object Text.UTF8Encoding $false))
    New-StoreIcon $m "$work\icon.png"
    $readmeText = [IO.File]::ReadAllText($readme, [Text.Encoding]::UTF8)
    $install = "`n## Installation`n`nThunderstore Mod Manager / r2modman: install from the page. Manual: BepInEx 5 installed, then copy ``$($m.Dir).dll`` into ``BepInEx/plugins``. Source code and issues: $repo`n"
    $readmeText = $readmeText -replace "(?m)^---\s*$", ($install + "`n---")   # avant la section francaise
    [IO.File]::WriteAllText("$work\README.md", $readmeText, (New-Object Text.UTF8Encoding $false))
    $tsZip = Join-Path $dist "thunderstore\YaYa-Mods-$($m.Name)-$ver.zip"
    New-Zip $tsZip ([ordered]@{ 'manifest.json' = "$work\manifest.json"; 'icon.png' = "$work\icon.png"; 'README.md' = "$work\README.md"; "$($m.Dir).dll" = $dll })

    # Nexus / manuel : arborescence du jeu
    $nxZip = Join-Path $dist "nexus\$($m.Name)-$ver.zip"
    New-Zip $nxZip ([ordered]@{ "BepInEx/plugins/$($m.Dir).dll" = $dll; 'README.md' = "$work\README.md" })

    $summary += "{0,-16} {1,-7} {2,7:n0} Ko  {3}" -f $m.Name, $ver, ((Get-Item $dll).Length / 1KB), (Split-Path $tsZip -Leaf)
}
Write-Host ""
Write-Host "Paquets dans $dist :"
$summary | ForEach-Object { Write-Host "  $_" }
