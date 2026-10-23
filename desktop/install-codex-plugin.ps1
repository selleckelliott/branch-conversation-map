$ErrorActionPreference = 'Stop'
$branchRoot = Split-Path -Parent $PSScriptRoot
$branchBridge = Join-Path $PSScriptRoot 'plugin.mjs'
$branchProject = Join-Path $PSScriptRoot 'Branch.Desktop\Branch.Desktop.csproj'
dotnet build $branchProject -c Release --configfile (Join-Path $PSScriptRoot 'Branch.Desktop\NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'The desktop overlay did not build.' }
codex mcp add branch-desktop -- node $branchBridge
if ($LASTEXITCODE -ne 0) { throw 'Codex did not register the desktop bridge.' }
Write-Output 'Branch desktop bridge registered. Restart Codex or open a new chat so it can load the tools, then ask: Open my Branch conversation map.'
