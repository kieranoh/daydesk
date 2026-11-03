$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot
$download=Join-Path $taskRoot 'build\downloads\innosetup.exe'
$compiler=Join-Path $taskRoot 'build\tools\Inno'
if (-not (Test-Path -LiteralPath (Join-Path $compiler 'ISCC.exe'))) {
    Import-Module "$PSHOME\Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1" -ErrorAction Stop
    $signature=Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') { throw 'Installer compiler signature verification failed.' }
    $arguments=@('/PORTABLE=1','/VERYSILENT','/CURRENTUSER','/NOICONS','/SP-','/NORESTART',('/DIR="'+$compiler+'"'))
    $process=Start-Process -FilePath $download -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw 'Portable installer compiler setup failed.' }
}
Write-Output (Join-Path $compiler 'ISCC.exe')
