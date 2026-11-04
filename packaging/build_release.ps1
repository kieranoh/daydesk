param([string]$TimeTreeSource=(Join-Path (Split-Path $PSScriptRoot) 'vendor\timetree-mcp'))
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot
Push-Location $taskRoot
try {
    Push-Location $TimeTreeSource
    try {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'TimeTree dependencies failed.' }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw 'TimeTree server build failed.' }
    } finally { Pop-Location }
    & python -X utf8 packaging\fetch_tools.py
    if ($LASTEXITCODE -ne 0) { throw 'Runtime download failed.' }
    & (Join-Path $PSScriptRoot 'prepare_tools.ps1')
    & node (Join-Path $TimeTreeSource 'node_modules\esbuild\bin\esbuild') (Join-Path $TimeTreeSource 'dist\index.js') --bundle --platform=node --format=cjs --target=node22 --minify --outfile=build/timetree.cjs --metafile=build/timetree-bundle.json --legal-comments=eof
    if ($LASTEXITCODE -ne 0) { throw 'TimeTree bundle failed.' }
    & (Join-Path $taskRoot 'build.ps1') -OutputDir (Join-Path $taskRoot 'build\native')
    & python -X utf8 packaging\stage.py
    if ($LASTEXITCODE -ne 0) { throw 'Payload staging failed.' }
    & python -X utf8 packaging\audit_payload.py
    if ($LASTEXITCODE -ne 0) { throw 'Payload privacy audit failed.' }
    & (Join-Path $taskRoot 'build\tools\Inno\ISCC.exe') (Join-Path $PSScriptRoot 'Daydesk.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer=Join-Path $taskRoot 'dist\Daydesk-Setup-1.3.7-x64.exe'
    & python -c "import hashlib,pathlib,sys; p=pathlib.Path(sys.argv[1]); line=hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name; p.with_suffix(p.suffix+'.sha256').write_text(line+'\n',encoding='ascii'); print(line)" $installer
    if ($LASTEXITCODE -ne 0) { throw 'Installer checksum failed.' }
} finally { Pop-Location }
