param([string]$OutputDir=$PSScriptRoot)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$todo = Join-Path $PSScriptRoot 'shared'
& (Join-Path $PSScriptRoot 'prepare-mcp.ps1')
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:"$OutputDir\TimeTreeWidget.exe" /win32icon:"$PSScriptRoot\calendar.ico" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" /r:"$framework\System.Xaml.dll" /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\CalendarWindow.cs" "$PSScriptRoot\WidgetCommands.cs" "$PSScriptRoot\AppPaths.cs" "$PSScriptRoot\ConnectionSettings.cs" "$PSScriptRoot\StartupSettings.cs" "$PSScriptRoot\SnapshotStore.cs" "$PSScriptRoot\TodoSnapshots.cs" "$PSScriptRoot\TodoPanel.cs" "$PSScriptRoot\WidgetTheme.cs" "$PSScriptRoot\FirebaseMateClient.cs" "$todo\Controls.cs" "$PSScriptRoot\FirebaseTodoDialog.cs" "$PSScriptRoot\TreeEventDialog.cs" "$PSScriptRoot\FirebaseClientSelfTest.cs" "$PSScriptRoot\TodoReadFallback.cs" "$PSScriptRoot\build\LegacyMcpClient.g.cs"
if ($LASTEXITCODE -ne 0) { throw 'Widget build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $OutputDir 'TimeTreeWidget.exe.config') -Force
Write-Output 'TimeTreeWidget.exe built.'
& (Join-Path $PSScriptRoot 'build-firebase.ps1') -OutputDir $OutputDir
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /out:"$OutputDir\daydesk.exe" "$PSScriptRoot\DaydeskCli.cs"
if ($LASTEXITCODE -ne 0) { throw 'CLI launcher build failed.' }
