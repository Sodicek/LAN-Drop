# This test intentionally changes trust only on a disposable CI runner.
# Local tests use explicit per-client CA validation in secure_transport.py instead.
$ErrorActionPreference = "Stop"
if ($env:GITHUB_ACTIONS -ne "true" -or $env:RUNNER_ENVIRONMENT -ne "github-hosted" -or !$IsWindows) {
    throw "This browser trust test is restricted to disposable Windows GitHub Actions runners. Local Windows trust will not be changed."
}
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$taskFixtureDirectory = Join-Path $env:RUNNER_TEMP ("landrop-browser-tls-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $taskFixtureDirectory | Out-Null
Write-Output "Creating an isolated browser TLS fixture."
$taskFixture = dotnet run --project (Join-Path $taskRoot "tests/Transport") -c Release --no-build -- --generate (Join-Path $taskFixtureDirectory "server.pfx") | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Could not create the isolated test certificate." }
$taskImported = $null
try {
    $taskPublicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($taskFixture.AuthorityPath))
    try {
        $taskFingerprint = $taskPublicCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
        if ($taskFingerprint -ne $taskFixture.AuthorityFingerprint) { throw "Test CA fingerprint mismatch." }
        $taskTrustPath = Join-Path "Cert:\LocalMachine\Root" $taskPublicCertificate.Thumbprint
        if (Test-Path -LiteralPath $taskTrustPath) { throw "Refusing to replace an existing trusted certificate." }
        $taskPemPath = Join-Path $taskFixtureDirectory "authority.pem"
        $taskEncoded = [Convert]::ToBase64String($taskPublicCertificate.RawData, [Base64FormattingOptions]::InsertLineBreaks)
        [IO.File]::WriteAllText($taskPemPath, "-----BEGIN CERTIFICATE-----`n$taskEncoded`n-----END CERTIFICATE-----`n", [Text.UTF8Encoding]::new($false))
    } finally { $taskPublicCertificate.Dispose() }
    # CurrentUser Root can request interactive confirmation and block headless CI.
    # The hosted Windows runner is disposable and elevated; use its machine store.
    Write-Output "Installing the verified test authority on the disposable hosted runner."
    $taskImported = Import-Certificate -FilePath $taskFixture.AuthorityPath -CertStoreLocation "Cert:\LocalMachine\Root"
    $env:LAN_DROP_TEST_TLS_PFX = $taskFixture.CertificatePath
    $env:LAN_DROP_CERTIFICATE_PASSWORD = "test-only-password-0123456789"
    $env:NODE_EXTRA_CA_CERTS = $taskPemPath
    Write-Output "Running Chromium with normal certificate validation."
    node (Join-Path $taskRoot "tests/mobile_browser.cjs") android
    if ($LASTEXITCODE -ne 0) { throw "Trusted HTTPS browser checks failed." }
} finally {
    foreach ($taskVariable in @("LAN_DROP_TEST_TLS_PFX", "LAN_DROP_CERTIFICATE_PASSWORD", "NODE_EXTRA_CA_CERTS")) {
        Remove-Item -LiteralPath ("Env:\" + $taskVariable) -ErrorAction SilentlyContinue
    }
    if ($taskImported) {
        if ($taskImported.Thumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw "Unexpected test root identity." }
        Remove-Item -LiteralPath (Join-Path "Cert:\LocalMachine\Root" $taskImported.Thumbprint)
        Write-Output "Removed the isolated test authority."
    }
}
