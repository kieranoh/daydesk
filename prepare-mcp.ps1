$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'shared\LegacyMcpClient.cs'
$generated = Join-Path $PSScriptRoot 'build\LegacyMcpClient.g.cs'
New-Item -ItemType Directory -Path (Split-Path $generated) -Force | Out-Null
# Keep the proven MCP client intact and isolate its original DTO names.
[IO.File]::WriteAllText($generated, "namespace LegacyMcp {`n" + [IO.File]::ReadAllText($source) + "`n}", [Text.UTF8Encoding]::new($true))
