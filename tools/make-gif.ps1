# Records the Loco Tester 3D view into a looping animated GIF for the README.
param([string]$Exe, [string[]]$AppArgs, [string]$Out, [int]$Frames = 36, [int]$DelayMs = 110, [int]$Width = 760)
Add-Type -AssemblyName System.Drawing, PresentationCore, WindowsBase
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[W2]::SetProcessDPIAware() | Out-Null
$p = Start-Process $Exe -ArgumentList $AppArgs -PassThru
Start-Sleep -Milliseconds 7000
$p.Refresh(); $h = $p.MainWindowHandle
$r = New-Object W2+RECT; [W2]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
# crop to the 3D viewport (same layout at any size: right column below the tabs)
$sx = $w / 1800.0; $sy = $hh / 1125.0
$crop = New-Object System.Drawing.Rectangle ([int](460 * $sx)), ([int](276 * $sy)), ([int](1306 * $sx)), ([int](526 * $sy))
$enc = New-Object System.Windows.Media.Imaging.GifBitmapEncoder
for ($i = 0; $i -lt $Frames; $i++) {
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [W2]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
  $hgt = [int]($Width * $crop.Height / $crop.Width)
  $small = New-Object System.Drawing.Bitmap $Width, $hgt
  $g2 = [System.Drawing.Graphics]::FromImage($small); $g2.InterpolationMode = 'HighQualityBicubic'
  $g2.DrawImage($bmp, (New-Object System.Drawing.Rectangle 0, 0, $Width, $hgt), $crop, [System.Drawing.GraphicsUnit]::Pixel); $g2.Dispose(); $bmp.Dispose()
  $ms = New-Object IO.MemoryStream; $small.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $small.Dispose(); $ms.Position = 0
  $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create($ms, 'None', 'OnLoad')
  $enc.Frames.Add($frame)
  Start-Sleep -Milliseconds ([Math]::Max(0, $DelayMs - 60))
}
Stop-Process -Id $p.Id -Force -Confirm:$false
$ms = New-Object IO.MemoryStream; $enc.Save($ms); $b = $ms.ToArray()
# make it loop (NETSCAPE2.0 block after the screen descriptor / global colour table) and set frame delays
$pos = 13; if ($b[10] -band 0x80) { $pos += 3 * [Math]::Pow(2, ($b[10] -band 7) + 1) }
[byte[]]$loop = [byte[]](0x21,0xFF,0x0B) + [byte[]][Text.Encoding]::ASCII.GetBytes('NETSCAPE2.0') + [byte[]](0x03,0x01,0x00,0x00,0x00)
$list = New-Object System.Collections.Generic.List[byte]; $list.AddRange([byte[]]$b[0..($pos-1)]); $list.AddRange($loop); $list.AddRange([byte[]]$b[$pos..($b.Length-1)])
$a = $list.ToArray(); $cs = [int]($DelayMs / 10)
for ($i = 0; $i -lt $a.Length - 8; $i++) { if ($a[$i] -eq 0x21 -and $a[$i+1] -eq 0xF9 -and $a[$i+2] -eq 0x04) { $a[$i+4] = $cs -band 255; $a[$i+5] = ($cs -shr 8) -band 255 } }
[IO.File]::WriteAllBytes($Out, $a)
Get-Item $Out | Select-Object Name, Length
