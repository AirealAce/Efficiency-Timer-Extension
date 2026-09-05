param([string]$DotNet = 'dotnet')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$desktopSource = $PSScriptRoot
$publishPath = Join-Path $desktopSource 'artifacts\win-x64'
$installPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\ReflectionTimerDesktop'
$exePath = Join-Path $installPath 'ReflectionTimer.exe'

if (Get-Process ReflectionTimer -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }) {
    throw 'Quit Reflection Timer Desktop from its tray menu before installing an update. Its saved data will be retained.'
}
& $DotNet run --project (Join-Path $desktopSource 'ReflectionTimer.Tests\ReflectionTimer.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed; installation cancelled.' }
& $DotNet publish (Join-Path $desktopSource 'ReflectionTimer.Desktop\ReflectionTimer.Desktop.csproj') -c Release -r win-x64 --self-contained false -o $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; installation cancelled.' }

# Per-user installation. Only application binaries are replaced; no app data,
# Chrome files, security settings, or startup settings are changed here.
if (Test-Path -LiteralPath $exePath) {
    $backupPath = $installPath + '-backup-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $installPath -Destination $backupPath -Recurse
    Write-Host "Previous app binaries preserved at $backupPath"
}
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Get-ChildItem -LiteralPath $publishPath -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $installPath $_.Name) -Force
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Reflection Timer Desktop.lnk'
$shellObject = New-Object -ComObject WScript.Shell
if (Test-Path -LiteralPath $shortcutPath) {
    $existing = $shellObject.CreateShortcut($shortcutPath)
    if ($existing.TargetPath -ne $exePath) { throw 'A different shortcut already has this name. The app is installed, but that shortcut was left unchanged.' }
}
$shortcut = $shellObject.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installPath
$shortcut.Description = 'Focus sessions and Google Sheets reflections'
$shortcut.IconLocation = $exePath + ',0'
$shortcut.Save()
Write-Host "Installed: $exePath"
Write-Host "Shortcut: $shortcutPath"
Write-Host 'Requires Microsoft .NET 10 Desktop Runtime (x64). Launch the shortcut and complete Settings. The Chrome extension is not disabled automatically.'
