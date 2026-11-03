param([string]$OutputDir=$PSScriptRoot)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $PSScriptRoot 'prepare-mcp.ps1')
& (Join-Path $framework 'csc.exe') /nologo /main:FirebaseTodo /target:exe /platform:x64 /optimize+ /codepage:65001 /out:"$OutputDir\FirebaseTodo.exe" /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\FirebaseMateClient.cs" "$PSScriptRoot\FirebaseTodo.cs" "$PSScriptRoot\SnapshotStore.cs" "$PSScriptRoot\AppPaths.cs" "$PSScriptRoot\TodoSnapshots.cs" "$PSScriptRoot\TodoReadFallback.cs" "$PSScriptRoot\build\LegacyMcpClient.g.cs"
if ($LASTEXITCODE -ne 0) { throw 'Firebase CLI build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $OutputDir 'FirebaseTodo.exe.config') -Force
& (Join-Path $framework 'csc.exe') /nologo /main:FirebaseAccount /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:"$OutputDir\FirebaseAccount.exe" /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\FirebaseMateClient.cs" "$PSScriptRoot\FirebaseTodo.cs" "$PSScriptRoot\SnapshotStore.cs" "$PSScriptRoot\AppPaths.cs" "$PSScriptRoot\TodoSnapshots.cs" "$PSScriptRoot\TodoReadFallback.cs" "$PSScriptRoot\build\LegacyMcpClient.g.cs"
if ($LASTEXITCODE -ne 0) { throw 'Firebase account window build failed.' }
Copy-Item -LiteralPath (Join-Path $OutputDir 'FirebaseTodo.exe.config') -Destination (Join-Path $OutputDir 'FirebaseAccount.exe.config') -Force
