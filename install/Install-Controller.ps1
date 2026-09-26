<#
.SYNOPSIS
  Installs the Remote Desktop Controller (viewer/controller) for the current user.

.DESCRIPTION
  The Controller is launched manually when you want to connect, so it does NOT auto-start.
  Per-user install, no elevation.
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PublishDir)

$ErrorActionPreference = 'Stop'
$InstallDir = Join-Path $env:LOCALAPPDATA 'RemoteDesktopControl\controller'

if (-not (Test-Path (Join-Path $PublishDir 'RemoteDesktop.Controller.exe'))) {
  throw "RemoteDesktop.Controller.exe not found in '$PublishDir'. Publish it first: " +
        "dotnet publish src/RemoteDesktop.Controller -c Release -r win-x64 --self-contained false -o <dir>"
}

Write-Host "Installing Controller to $InstallDir ..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $InstallDir -Recurse -Force

# Optional Start Menu shortcut.
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Remote Desktop Controller.lnk'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($startMenu)
$lnk.TargetPath = Join-Path $InstallDir 'RemoteDesktop.Controller.exe'
$lnk.Save()

Write-Host "Done. Launch it from the Start Menu: 'Remote Desktop Controller'."
