param([string]$Widget=(Join-Path (Split-Path $PSScriptRoot) 'build\native\TimeTreeWidget.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$output=Join-Path $root 'build\qa'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf=Join-Path $framework 'WPF'
Copy-Item -LiteralPath (Join-Path $root 'firebase-public.json') -Destination (Join-Path $output 'firebase-public.json') -Force
foreach($check in @('DialogPreview','LayerCheck')) {
    & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 /out:"$output\$check.exe" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" /r:"$framework\System.Xaml.dll" /r:System.Web.Extensions.dll /r:System.Core.dll (Join-Path $PSScriptRoot "$check.cs")
    if($LASTEXITCODE -ne 0) { throw "$check build failed." }
    $process=Start-Process -FilePath (Join-Path $output "$check.exe") -ArgumentList @(('"'+$Widget+'"'),'--check') -WindowStyle Hidden -Wait -PassThru
    if($process.ExitCode -ne 0) { throw "$check verification failed." }
}
