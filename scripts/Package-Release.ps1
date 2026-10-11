param([ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot "Directory.Build.props") -Raw)).Project.PropertyGroup.Version
$taskPackageName = "LAN-Drop-$taskVersion-$Runtime"
$taskSource = Join-Path $taskRoot "publish/$taskPackageName"
if (!(Test-Path -LiteralPath (Join-Path $taskSource "LanDrop.Desktop.exe"))) { throw "Build the portable package first." }
$taskArchive = Join-Path $taskRoot "publish/$taskPackageName.zip"
Compress-Archive -LiteralPath $taskSource -DestinationPath $taskArchive -Force
$taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$taskArchive.sha256", "$taskHash  $taskPackageName.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Release package ready: $taskArchive"
