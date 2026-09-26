<#
.SYNOPSIS
  Installs the Remote Desktop Host for the CURRENT user, with auto-start at logon.

.DESCRIPTION
  The Host must run inside the user's interactive session (screen capture and input
  injection cannot work from a Session 0 service), so this installs a per-user logon
  scheduled task rather than a system service. It requires NO administrator rights and
  requests NO elevation.

  This is an explicit, consent-based install: you are running it yourself. It does not hide
  anything — the app shows a tray icon and a mandatory on-screen indicator during sessions.

.PARAMETER PublishDir
  Folder containing the published Host build (dotnet publish output).

.PARAMETER NoAutoStart
  Install without creating the logon auto-start task.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PublishDir,
  [switch]$NoAutoStart
)

$ErrorActionPreference = 'Stop'
$AppName   = 'RemoteDesktopControl.Host'
$InstallDir = Join-Path $env:LOCALAPPDATA 'RemoteDesktopControl\app'
$Exe       = Join-Path $InstallDir 'RemoteDesktop.Host.exe'
$TaskName  = 'RemoteDesktopControl Host (logon)'

Write-Host "Installing $AppName to $InstallDir (current user only, no elevation)..."

if (-not (Test-Path (Join-Path $PublishDir 'RemoteDesktop.Host.exe'))) {
  throw "RemoteDesktop.Host.exe not found in '$PublishDir'. Publish it first: " +
        "dotnet publish src/RemoteDesktop.Host -c Release -r win-x64 --self-contained false -o <dir>"
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $InstallDir -Recurse -Force

if (-not $NoAutoStart) {
  Write-Host "Creating logon auto-start task '$TaskName' for $env:USERNAME..."
  $action    = New-ScheduledTaskAction -Execute $Exe
  $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
  # Limited run level = asInvoker (no elevation). Runs only when this user is logged on.
  $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
  $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
  Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null
}

Write-Host "Starting the Host..."
Start-Process -FilePath $Exe

Write-Host "Done. Use the tray icon to pair a device or manage trusted devices."
Write-Host "To remove: install\Uninstall-Host.ps1"
