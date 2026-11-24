# Builds Loco Tester with the C# compiler that ships with Windows (.NET Framework 4.8).
# Output: build\Loco Tester.exe  (single file; logo, icon and window layout are embedded)
param([string]$Out = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$src = Join-Path $PSScriptRoot 'src'
$assets = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $Out | Out-Null
$files = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
$args = @(
  '/nologo', '/codepage:65001', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
  "/out:$(Join-Path $Out 'Loco Tester.exe')",
  "/win32icon:$(Join-Path $assets 'app.ico')",
  "/r:$wpf\PresentationCore.dll", "/r:$wpf\PresentationFramework.dll", "/r:$wpf\WindowsBase.dll",
  '/r:System.Xaml.dll', '/r:System.Xml.dll', '/r:System.IO.Compression.dll', '/r:System.Windows.Forms.dll',
  "/resource:$(Join-Path $src 'MainWindow.xaml'),LocoTester.MainWindow.xaml",
  "/resource:$(Join-Path $assets 'logo.png'),LocoTester.logo.png",
  "/resource:$(Join-Path $assets 'app.ico'),LocoTester.app.ico"
) + $files
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Get-Item (Join-Path $Out 'Loco Tester.exe') | Select-Object FullName, Length, LastWriteTime
