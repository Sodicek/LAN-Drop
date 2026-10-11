param([ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot "Directory.Build.props") -Raw)).Project.PropertyGroup.Version
$taskOutput = Join-Path $taskRoot "publish/LAN-Drop-$taskVersion-$Runtime"
dotnet publish (Join-Path $taskRoot "src/LanDrop.Desktop") -c Release -r $Runtime --self-contained true -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }
dotnet publish (Join-Path $taskRoot "src/LanDrop") -c Release -r $Runtime --self-contained true -o (Join-Path $taskOutput "Server")
if ($LASTEXITCODE -ne 0) { throw "Server publish failed." }
Copy-Item -LiteralPath (Join-Path $taskRoot "README.md") -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskRoot "THIRD-PARTY-NOTICES.md") -Destination $taskOutput
foreach ($taskDocument in @("CHANGELOG.md", "SECURITY.md", "ToDo.md", "PRODUCT-DESIGN.md")) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskDocument) -Destination $taskOutput
}
Copy-Item -LiteralPath (Join-Path $taskRoot "docs") -Destination $taskOutput -Recurse -Force
Write-Output "Portable preview ready: $taskOutput"
