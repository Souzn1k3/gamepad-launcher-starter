param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'dist\GamepadLauncherStarter.exe'
if (-not (Test-Path -LiteralPath $source)) {
    throw 'dist\GamepadLauncherStarter.exe not found. Run .\build.ps1 first.'
}

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\GamepadLauncherStarter'
$target = Join-Path $targetDir 'GamepadLauncherStarter.exe'
if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

Get-Process -Name 'GamepadLauncherStarter' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null } catch { }
}
Start-Sleep -Milliseconds 400
Get-Process -Name 'GamepadLauncherStarter' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Copy-Item -LiteralPath $source -Destination $target -Force

$shortcutDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
if (-not (Test-Path -LiteralPath $shortcutDir)) {
    New-Item -ItemType Directory -Force -Path $shortcutDir | Out-Null
}
$shortcutPath = Join-Path $shortcutDir 'Gamepad Launcher Starter.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.Arguments = '--settings'
$shortcut.WorkingDirectory = $targetDir
$shortcut.IconLocation = $target
$shortcut.Save()

Write-Host "Installed: $target"
Write-Host "Shortcut:  $shortcutPath"
Write-Host "Autostart is controlled by the app (General tab)."

if (-not $NoLaunch) {
    Start-Process -FilePath $target
}
