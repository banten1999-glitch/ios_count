<#
.SYNOPSIS
  Removes the Remote Desktop Host for the current user.

.DESCRIPTION
  Stops the app, removes the logon auto-start task, and deletes the installed files. By
  default it KEEPS your identity key and trusted-device list; pass -PurgeData to also remove
  those (this revokes this machine's identity and forgets all paired devices).
#>
[CmdletBinding()]
param([switch]$PurgeData)

$ErrorActionPreference = 'SilentlyContinue'
$InstallDir = Join-Path $env:LOCALAPPDATA 'RemoteDesktopControl\app'
$DataDir    = Join-Path $env:LOCALAPPDATA 'RemoteDesktopControl\Host'
$TaskName   = 'RemoteDesktopControl Host (logon)'

Write-Host "Stopping the Host process..."
Get-Process -Name 'RemoteDesktop.Host' | Stop-Process -Force

Write-Host "Removing logon task..."
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false

Write-Host "Deleting installed files..."
Remove-Item -Recurse -Force -Path $InstallDir

if ($PurgeData) {
  Write-Host "Purging identity key and trusted-device list from $DataDir ..."
  Remove-Item -Recurse -Force -Path $DataDir
  Write-Host "Identity and trust data removed."
} else {
  Write-Host "Kept identity/trust data in $DataDir (use -PurgeData to remove)."
}

Write-Host "Uninstall complete."
