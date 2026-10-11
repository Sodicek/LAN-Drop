param([ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$taskOutput = Join-Path $taskRoot "publish/LAN-Drop-$Runtime"
dotnet publish (Join-Path $taskRoot "src/LanDrop.Desktop") -c Release -r $Runtime --self-contained true -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }
dotnet publish (Join-Path $taskRoot "src/LanDrop") -c Release -r $Runtime --self-contained true -o (Join-Path $taskOutput "Server")
if ($LASTEXITCODE -ne 0) { throw "Server publish failed." }
Copy-Item -LiteralPath (Join-Path $taskRoot "README.md") -Destination $taskOutput
Write-Output "Portable preview ready: $taskOutput"
