# Builds Loco Tester and its installer (dist\LocoTester-Setup-1.0.exe).
# Needs: Windows 10/11 (.NET Framework 4.8 + its C# compiler are built in) and Inno Setup 6 (jrsoftware.org).
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Out (Join-Path $PSScriptRoot 'build5') | Out-Null
& (Join-Path $PSScriptRoot 'installer\make-wizard-images.ps1') | Out-Null
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php" }
& $iscc /Q (Join-Path $PSScriptRoot 'installer\LocoTester.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Get-Item (Join-Path $PSScriptRoot 'dist\LocoTester-Setup-1.0.exe') | Select-Object FullName, Length
