param([ValidateSet('on','off','status')][string]$Mode='status')
$ErrorActionPreference='Stop'
$path=Join-Path ([Environment]::GetFolderPath('Startup')) 'TimeTree Calendar.lnk'
if ($env:DAYDESK_DATA_DIR) { $path=Join-Path $env:DAYDESK_DATA_DIR 'Startup\TimeTree Calendar.lnk' }
$shell=New-Object -ComObject WScript.Shell
if ($Mode -eq 'on') {
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    $link=$shell.CreateShortcut($path)
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'standalone.json')) {
        $link.TargetPath=Join-Path $PSScriptRoot 'TimeTreeWidget.exe'
        $link.Arguments=''
    } else {
        $python=(Get-Command python -ErrorAction Stop).Source
        $pythonw=Join-Path (Split-Path $python) 'pythonw.exe'
        if (-not (Test-Path -LiteralPath $pythonw)) { throw 'pythonw.exe is required.' }
        $link.TargetPath=$pythonw
        $link.Arguments='"'+(Join-Path $PSScriptRoot 'widget.py')+'"'
    }
    $link.WorkingDirectory=$PSScriptRoot
    $link.IconLocation=(Join-Path $PSScriptRoot 'calendar.ico')+',0'
    $link.Description='TimeTree and Todo Mate desktop widget at Windows sign-in'
    $link.Save()
}
$enabled=$false
if (Test-Path -LiteralPath $path) {
    $link=$shell.CreateShortcut($path)
    $enabled=$link.WorkingDirectory -eq $PSScriptRoot -and ($link.TargetPath -eq (Join-Path $PSScriptRoot 'TimeTreeWidget.exe') -or $link.Arguments.Contains((Join-Path $PSScriptRoot 'widget.py')))
    if ($Mode -eq 'off') {
        if (-not $enabled) { throw 'Startup shortcut belongs to another installation.' }
        Remove-Item -LiteralPath $path
        $enabled=$false
    }
}
Write-Output $enabled
