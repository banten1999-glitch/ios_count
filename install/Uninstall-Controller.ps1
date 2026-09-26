<#
.SYNOPSIS
  Removes the Remote Desktop Controller for the current user.
.DESCRIPTION
  Deletes installed files and the Start Menu shortcut. Pass -PurgeData to also remove the
  Controller's identity key and known-hosts list.
#>
[CmdletBinding()]
param([switch]$PurgeData)

$ErrorActionPreference = 'SilentlyContinue'
$InstallDir = Join-Path $env:LOCALAPPDATA 'RemoteDesktopControl\controller'
$DataDir    = Join-Path $env:LOCALAPPDATA 'RemoteDesktopControl\Controller'
$Shortcut   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Remote Desktop Controller.lnk'

Get-Process -Name 'RemoteDesktop.Controller' | Stop-Process -Force
Remove-Item -Force -Path $Shortcut
Remove-Item -Recurse -Force -Path $InstallDir

if ($PurgeData) {
  Remove-Item -Recurse -Force -Path $DataDir
  Write-Host "Identity and known-hosts data removed."
} else {
  Write-Host "Kept identity/known-hosts data in $DataDir (use -PurgeData to remove)."
}

Write-Host "Uninstall complete."
