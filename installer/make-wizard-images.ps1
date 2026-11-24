# Wizard artwork for the Inno Setup installer (normal and high-DPI sizes), made from the logo.
param([string]$Logo = (Join-Path $PSScriptRoot '..\assets\logo.png'), [string]$Out = $PSScriptRoot)
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile((Resolve-Path $Logo).Path)
function New-Side($w, $h, $file) {
  $b = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 0, $w, $h), ([System.Drawing.Color]::FromArgb(22, 35, 58)), ([System.Drawing.Color]::FromArgb(10, 17, 29)), 90
  $g.FillRectangle($br, 0, 0, $w, $h)
  # soft orange glow behind the logo
  $s = $w / 164.0
  $gp = New-Object System.Drawing.Drawing2D.GraphicsPath; $gp.AddEllipse([float](12 * $s), [float](52 * $s), [float](140 * $s), [float](140 * $s))
  $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $gp; $pg.CenterColor = [System.Drawing.Color]::FromArgb(110, 247, 129, 30); $pg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 247, 129, 30))
  $g.FillPath($pg, $gp)
  $g.DrawImage($img, [float](27 * $s), [float](67 * $s), [float](110 * $s), [float](110 * $s))
  $f1 = New-Object System.Drawing.Font 'Segoe UI', ([float](15 * $s)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $f2 = New-Object System.Drawing.Font 'Segoe UI', ([float](9.5 * $s)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'
  $g.DrawString('Loco Tester', $f1, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ([float](195 * $s)), $w, ([float](24 * $s))), $sf)
  $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(247, 129, 30))
  $g.DrawString('v1.0 · First Light', $f2, $orange, (New-Object System.Drawing.RectangleF 0, ([float](219 * $s)), $w, ([float](16 * $s))), $sf)
  $muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 157, 186))
  $g.DrawString("by Piscongentine`nMade in Bharat", $f2, $muted, (New-Object System.Drawing.RectangleF 0, ([float](262 * $s)), $w, ([float](40 * $s))), $sf)
  # tricolour strip
  $sh = [int](3 * $s); $sw = [int]($w / 3)
  $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 153, 51))), 0, $h - $sh, $sw, $sh)
  $g.FillRectangle([System.Drawing.Brushes]::White, $sw, $h - $sh, $sw, $sh)
  $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(19, 136, 8))), 2 * $sw, $h - $sh, $w - 2 * $sw, $sh)
  $g.Dispose(); $b.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp); $b.Dispose()
}
function New-Small($n, $file) {
  $b = New-Object System.Drawing.Bitmap $n, $n
  $g = [System.Drawing.Graphics]::FromImage($b); $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::White)
  $g.DrawImage($img, 0, 0, $n, $n)
  $g.Dispose(); $b.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp); $b.Dispose()
}
New-Side 164 314 (Join-Path $Out 'wizard.bmp')
New-Side 328 628 (Join-Path $Out 'wizard@2x.bmp')
New-Small 55 (Join-Path $Out 'wizard-small.bmp')
New-Small 110 (Join-Path $Out 'wizard-small@2x.bmp')
$img.Dispose()
Get-ChildItem $Out -Filter *.bmp | Select-Object Name, Length
