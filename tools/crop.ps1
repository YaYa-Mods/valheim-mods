# Recadre une capture : tools\crop.ps1 in.png out.png x y w h [scale]
param([string]$In,[string]$Out,[int]$X,[int]$Y,[int]$W,[int]$H,[double]$Scale=1)
Add-Type -AssemblyName System.Drawing
$b=[System.Drawing.Bitmap]::FromFile($In)
$r=New-Object System.Drawing.Rectangle($X,$Y,$W,$H)
$c=$b.Clone($r,$b.PixelFormat)
if ($Scale -ne 1) { $c=New-Object System.Drawing.Bitmap($c,[int]($W*$Scale),[int]($H*$Scale)) }
$c.Save($Out)
