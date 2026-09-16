# Génère les icônes du menu radial (PNG 128×128, traits blancs + ombre sombre, fond transparent), embarquées
# ensuite dans les DLL (dossier Icons de chaque mod). Aucune ressource externe : tout est dessiné ici.
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$S = 128

function New-Icon([string]$path, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    # deux passes : ombre épaisse sombre, puis trait blanc
    foreach ($pass in 0, 1) {
        $pen = if ($pass -eq 0) { New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 20, 15, 10)), 18 } else { New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 245, 240, 225)), 9 }
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $brush = if ($pass -eq 0) { New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, 20, 15, 10)) } else { New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 240, 225)) }
        & $draw $g $pen $brush $pass
        $pen.Dispose(); $brush.Dispose()
    }
    $g.Dispose()
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "icône : $path"
}

# Loupe (scanner)
New-Icon "$root\mods\ResourceFinder\Icons\scanner.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawEllipse($pen, 22, 22, 56, 56)
    $g.DrawLine($pen, 66, 66, 104, 104)
}

# Cible + flèche (cible suivante)
New-Icon "$root\mods\ResourceFinder\Icons\target.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawEllipse($pen, 30, 30, 68, 68)
    $g.FillEllipse($brush, 55, 55, 18, 18)
    $g.DrawLine($pen, 64, 8, 64, 26); $g.DrawLine($pen, 64, 102, 64, 120)
    $g.DrawLine($pen, 8, 64, 26, 64); $g.DrawLine($pen, 102, 64, 120, 64)
}

# Épingle barrée (effacer les épingles)
New-Icon "$root\mods\ResourceFinder\Icons\clear.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawEllipse($pen, 26, 12, 48, 48)
    $g.DrawLine($pen, 32, 54, 50, 98); $g.DrawLine($pen, 68, 54, 50, 98)
    $g.FillEllipse($brush, 42, 28, 16, 16)
    $g.DrawLine($pen, 84, 80, 116, 112); $g.DrawLine($pen, 116, 80, 84, 112)
}

# Engrenage (groupe Mods)
New-Icon "$root\mods\ModHub\Icons\mods.png" {
    param($g, $pen, $brush, $pass)
    $c = 64; $r1 = 30; $r2 = 44
    for ($i = 0; $i -lt 8; $i++) {
        $a = $i * [math]::PI / 4
        $g.DrawLine($pen, $c + $r1 * [math]::Cos($a), $c + $r1 * [math]::Sin($a), $c + $r2 * [math]::Cos($a), $c + $r2 * [math]::Sin($a))
    }
    $g.DrawEllipse($pen, $c - $r1, $c - $r1, 2 * $r1, 2 * $r1)
    $g.DrawEllipse($pen, $c - 11, $c - 11, 22, 22)
}

# Curseurs (config des mods)
New-Icon "$root\mods\ModHub\Icons\config.png" {
    param($g, $pen, $brush, $pass)
    foreach ($row in @(@(34, 46), @(64, 84), @(94, 60))) {
        $y = $row[0]; $x = $row[1]
        $g.DrawLine($pen, 18, $y, 110, $y)
        $g.FillEllipse($brush, $x - 11, $y - 11, 22, 22)
    }
}

# Maison (rentrer au lit)
New-Icon "$root\mods\HomeTeleport\Icons\home.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawLine($pen, 20, 62, 64, 22); $g.DrawLine($pen, 64, 22, 108, 62)
    $g.DrawLine($pen, 30, 56, 30, 108); $g.DrawLine($pen, 98, 56, 98, 108); $g.DrawLine($pen, 30, 108, 98, 108)
    $g.DrawLine($pen, 54, 108, 54, 78); $g.DrawLine($pen, 54, 78, 74, 78); $g.DrawLine($pen, 74, 78, 74, 108)
}

# Parchemin coché (guide de progression)
New-Icon "$root\mods\Guide\Icons\guide.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawRectangle($pen, 28, 16, 72, 96)
    $g.DrawLine($pen, 42, 40, 86, 40); $g.DrawLine($pen, 42, 58, 86, 58)
    $g.DrawLine($pen, 42, 84, 54, 96); $g.DrawLine($pen, 54, 96, 84, 70)
}

# Barres décroissantes (trier)
New-Icon "$root\mods\Inventory\Icons\sort.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawLine($pen, 22, 34, 106, 34); $g.DrawLine($pen, 22, 64, 82, 64); $g.DrawLine($pen, 22, 94, 58, 94)
}

# Piles superposées (empiler)
New-Icon "$root\mods\Inventory\Icons\stack.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawRectangle($pen, 30, 74, 68, 34); $g.DrawRectangle($pen, 38, 46, 52, 28); $g.DrawRectangle($pen, 46, 20, 36, 26)
}

# Grille de quatre cases (toutes les catégories)
New-Icon "$root\mods\Inventory\Icons\all.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawRectangle($pen, 24, 24, 34, 34); $g.DrawRectangle($pen, 70, 24, 34, 34); $g.DrawRectangle($pen, 24, 70, 34, 34); $g.DrawRectangle($pen, 70, 70, 34, 34)
}

# Œil (afficher / masquer le suivi HUD du guide)
New-Icon "$root\mods\Guide\Icons\tracker.png" {
    param($g, $pen, $brush, $pass)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(14, 64, 40, 18, 88, 18, 114, 64); $path.AddBezier(114, 64, 88, 110, 40, 110, 14, 64); $path.CloseFigure()
    $g.DrawPath($pen, $path)
    $g.DrawEllipse($pen, 50, 50, 28, 28); $g.FillEllipse($brush, 58, 58, 12, 12)
}

# Icônes de tri par mode (l'icône du bouton change avec le mode) : catégorie = grille + barres, nom = « Aa »,
# quantité = « 123 », poids = haltère
function Draw-Text($g, $brush, $pass, [string]$text, [float]$size, [float]$x, [float]$y) {
    $font = New-Object System.Drawing.Font 'Arial', $size, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddString($text, $font.FontFamily, [int]$font.Style, $size, (New-Object System.Drawing.PointF $x, $y), [System.Drawing.StringFormat]::GenericDefault)
    if ($pass -eq 0) { $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, 20, 15, 10)), 14; $pen.LineJoin = 'Round'; $g.DrawPath($pen, $path); $pen.Dispose() }
    else { $g.FillPath($brush, $path) }
    $font.Dispose(); $path.Dispose()
}
New-Icon "$root\mods\Inventory\Icons\sort_cat.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawRectangle($pen, 18, 22, 30, 30); $g.DrawRectangle($pen, 18, 74, 30, 30)
    $g.DrawLine($pen, 62, 37, 110, 37); $g.DrawLine($pen, 62, 89, 110, 89)
}
New-Icon "$root\mods\Inventory\Icons\sort_name.png" {
    param($g, $pen, $brush, $pass)
    Draw-Text $g $brush $pass 'Aa' 74 14 22
}
New-Icon "$root\mods\Inventory\Icons\sort_qty.png" {
    param($g, $pen, $brush, $pass)
    Draw-Text $g $brush $pass '123' 58 8 34
}
New-Icon "$root\mods\Inventory\Icons\sort_weight.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawLine($pen, 34, 64, 94, 64)
    $g.DrawRectangle($pen, 14, 40, 18, 48); $g.DrawRectangle($pen, 96, 40, 18, 48)
}

# Traque : cible + flèche qui enchaîne (cercle, point, flèche courbe)
New-Icon "$root\mods\ResourceFinder\Icons\track.png" {
    param($g, $pen, $brush, $pass)
    $g.DrawEllipse($pen, 26, 26, 52, 52); $g.FillEllipse($brush, 44, 44, 16, 16)
    $g.DrawArc($pen, 14, 14, 100, 100, 300, 120)
    $g.DrawLine($pen, 100, 78, 112, 100); $g.DrawLine($pen, 100, 78, 78, 84)
}
