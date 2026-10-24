$ErrorActionPreference = 'SilentlyContinue'

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\GamepadLauncherStarter'
$dataDir = Join-Path $env:LOCALAPPDATA 'GamepadLauncherStarter'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$backupFile = Join-Path $dataDir 'steam_autostart_backup.txt'
$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Gamepad Launcher Starter.lnk'

Get-Process -Name 'GamepadLauncherStarter' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Remove-ItemProperty -Path $runKey -Name 'GamepadLauncherStarter' -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $backupFile) {
    $steamValue = (Get-Content -LiteralPath $backupFile -Raw -Encoding UTF8).Trim()
    if ($steamValue) {
        Set-ItemProperty -Path $runKey -Name 'Steam' -Value $steamValue -ErrorAction SilentlyContinue
        Write-Host "Steam autostart restored: $steamValue"
    }
    Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $targetDir -Recurse -Force -ErrorAction SilentlyContinue

$answer = Read-Host 'Delete settings and logs as well? (y/N)'
if ($answer -match '^(y|yes|д|да)$') {
    Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Gamepad Launcher Starter removed.'
