# Installs Loco Tester into C:\OR\tsre\Loco Tester and puts a shortcut on the desktop.
param([string]$Build = (Join-Path $PSScriptRoot 'build5'), [string]$Dest = 'C:\OR\tsre\Loco Tester')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Dest | Out-Null
Copy-Item (Join-Path $Build 'Loco Tester.exe') $Dest -Force
Copy-Item (Join-Path $PSScriptRoot 'assets\logo.png') $Dest -Force
Copy-Item (Join-Path $PSScriptRoot 'README.txt') $Dest -Force
$desktop = [Environment]::GetFolderPath('Desktop')
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut((Join-Path $desktop 'Loco Tester.lnk'))
$lnk.TargetPath = Join-Path $Dest 'Loco Tester.exe'
$lnk.WorkingDirectory = $Dest
$lnk.IconLocation = (Join-Path $Dest 'Loco Tester.exe') + ',0'
$lnk.Description = 'Loco Tester by Piscongentine - test Open Rails locomotives'
$lnk.Save()
Get-ChildItem $Dest | Select-Object Name, Length
Get-Item (Join-Path $desktop 'Loco Tester.lnk') | Select-Object FullName
