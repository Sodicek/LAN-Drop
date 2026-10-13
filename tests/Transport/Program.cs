using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LanDrop.Transport;

const string password = "test-only-password-0123456789";
if (args.Length == 2 && args[0] == "--generate")
{
    var generated = CertificateTools.Generate(args[1], "127.0.0.1", password);
    Console.WriteLine(JsonSerializer.Serialize(generated));
    return;
}
if (args.Length != 0) throw new ArgumentException("Use no arguments for checks or --generate path for an isolated fixture.");

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action operation, string message)
{
    try { operation(); } catch (Exception) { return; }
    throw new Exception("Expected rejection: " + message);
}
CertificateInspection InspectStatus(string path, string passphrase, string address, CertificateStatus expected)
{
    var result = CertificateTools.InspectServerCertificate(path, passphrase, address);
    Check(result.Status == expected, $"Expected {expected}, got {result.Status}.");
    if (expected != CertificateStatus.Ready)
    {
        Check(result.NotAfterUtc is null && !result.ExpiringSoon && result.Fingerprint is null &&
            result.IpAddresses.Count == 0 && result.Authority is null, "Rejected inspection must expose no certificate metadata.");
        try { using var loaded = CertificateTools.LoadServerCertificate(path, passphrase, address); }
        catch (CertificateValidationException error)
        {
            Check(error.Status == expected && error.InnerException is null, "Load must expose the same safe status without provider detail.");
            Check(!error.Message.Contains(path, StringComparison.OrdinalIgnoreCase) &&
                (passphrase.Length == 0 || !error.Message.Contains(passphrase, StringComparison.Ordinal)), "Errors must not include a path or password.");
            return result;
        }
        throw new Exception("Invalid certificate passed runtime validation.");
    }
    return result;
}
string[] TrustedRoots()
{
    var roots = new List<string>();
    foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
    {
        using var store = new X509Store(StoreName.Root, location);
        store.Open(OpenFlags.ReadOnly);
        roots.AddRange(store.Certificates.Select(c => location + ":" + c.Thumbprint));
    }
    return roots.Order().ToArray();
}
var before = TrustedRoots();
var directory = Path.Combine(Path.GetTempPath(), "landrop-tls-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "server.pfx");
    var generated = CertificateTools.Generate(path, "127.0.0.1", password);
    Check(generated.CertificatePath == path && File.Exists(generated.AuthorityPath), "Both certificate files must be returned.");
    using var server = CertificateTools.LoadServerCertificate(path, password, "127.0.0.1");
    using var authority = X509CertificateLoader.LoadCertificateFromFile(generated.AuthorityPath);
    Check(server.HasPrivateKey, "TLS certificate requires a private key.");
    Check(!authority.HasPrivateKey, "Exported phone trust certificate must contain no private key.");
    Check(authority.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority, "Trust export must be the issuing CA.");
    var exported = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
    try
    {
        Check(exported.Count == 1 && exported[0].Thumbprint == server.Thumbprint && exported[0].HasPrivateKey,
            "PFX must contain only the server identity; the CA signing key must never be exported.");
    }
    finally { foreach (var item in exported) item.Dispose(); }
    var actualFingerprint = Convert.ToHexString(SHA256.HashData(authority.RawData));
    Check(generated.AuthorityFingerprint.Replace(":", "").Replace(" ", "").Equals(actualFingerprint, StringComparison.OrdinalIgnoreCase), "Displayed CA fingerprint must match exported bytes.");
    using var chain = new X509Chain();
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(authority);
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
    Check(chain.Build(server), "Generated TLS certificate must validate against its exported CA.");
    var inspection = InspectStatus(path, password, "127.0.0.1", CertificateStatus.Ready);
    Check(inspection.NotAfterUtc == new DateTimeOffset(server.NotAfter.ToUniversalTime()) && !inspection.ExpiringSoon,
        "Ready inspection must report validated leaf expiry and the seven-day renewal warning.");
    Check(inspection.Fingerprint == CertificateTools.Fingerprint(server) && inspection.IpAddresses.SequenceEqual(new[] { "127.0.0.1" }),
        "Ready inspection must report the actual leaf fingerprint and IP SAN.");
    Check(inspection.Authority?.Path == generated.AuthorityPath && inspection.Authority.Fingerprint == generated.AuthorityFingerprint,
        "Inspection must recover verified adjacent public CA metadata after generation has returned.");
    InspectStatus(path, "", "127.0.0.1", CertificateStatus.PasswordRequired);
    InspectStatus(path, "wrong-password", "127.0.0.1", CertificateStatus.UnreadableOrInvalid);
    InspectStatus(path, password, "127.0.0.2", CertificateStatus.AddressMismatch);
    InspectStatus(path, password, "0.0.0.0", CertificateStatus.InvalidAddress);
    InspectStatus(Path.Combine(directory, "missing.pfx"), password, "127.0.0.1", CertificateStatus.MissingFile);
    var publicAuthorityBytes = File.ReadAllBytes(generated.AuthorityPath);
    File.Delete(generated.AuthorityPath);
    Check(InspectStatus(path, password, "127.0.0.1", CertificateStatus.Ready).Authority is null,
        "Missing companion CA must not invalidate an otherwise valid external leaf or claim phone trust.");
    File.WriteAllBytes(generated.AuthorityPath, server.RawData);
    Check(InspectStatus(path, password, "127.0.0.1", CertificateStatus.Ready).Authority is null,
        "A public leaf renamed as the CA must not be exposed as a trust certificate.");
    var unrelated = CertificateTools.Generate(Path.Combine(directory, "unrelated.pfx"), "127.0.0.1", password);
    File.WriteAllBytes(generated.AuthorityPath, File.ReadAllBytes(unrelated.AuthorityPath));
    Check(InspectStatus(path, password, "127.0.0.1", CertificateStatus.Ready).Authority is null,
        "Matching filenames and subjects cannot replace actual chain verification.");
    File.WriteAllBytes(generated.AuthorityPath, File.ReadAllBytes(path));
    Check(InspectStatus(path, password, "127.0.0.1", CertificateStatus.Ready).Authority is null,
        "A private-key container renamed as a public CA must never be exposed.");
    File.WriteAllBytes(generated.AuthorityPath, publicAuthorityBytes);
    Check(InspectStatus(path, password, "127.0.0.1", CertificateStatus.Ready).Authority?.Fingerprint == generated.AuthorityFingerprint,
        "Only restoring the real public issuer should restore companion metadata.");
    var original = File.ReadAllBytes(path);
    Reject(() => CertificateTools.Generate(path, "127.0.0.1", password), "Existing key must not be overwritten.");
    Check(File.ReadAllBytes(path).SequenceEqual(original), "Rejected overwrite changed the original key.");
    var occupied = Path.Combine(directory, "occupied.pfx");
    File.WriteAllText(Path.ChangeExtension(occupied, ".ca.cer"), "keep this public certificate");
    Reject(() => CertificateTools.Generate(occupied, "127.0.0.1", password), "Existing CA export must not be overwritten.");
    Check(!File.Exists(occupied), "Failed CA overwrite must not leave a new private key.");
    var rollback = Path.Combine(directory, "rollback.pfx");
    var blockedAuthority = Path.ChangeExtension(rollback, ".ca.cer");
    Directory.CreateDirectory(blockedAuthority);
    Reject(() => CertificateTools.Generate(rollback, "127.0.0.1", password), "Second output failure must roll back the first new output.");
    Check(!File.Exists(rollback) && Directory.Exists(blockedAuthority), "Rollback must remove only its newly created private key and preserve the occupied CA destination.");
    Reject(() => CertificateTools.Generate(Path.Combine(directory, "bad-address.pfx"), "hostile.example", password), "DNS address is unsupported.");
    Reject(() => CertificateTools.Generate(Path.Combine(directory, "ipv6.pfx"), "::1", password), "Only IPv4 supported.");
    Reject(() => CertificateTools.Generate(Path.Combine(directory, "any-ip.pfx"), "0.0.0.0", password), "Unspecified IPv4 cannot identify a phone endpoint.");
    Reject(() => CertificateTools.Generate(Path.Combine(directory, "broadcast.pfx"), "255.255.255.255", password), "Broadcast IPv4 cannot identify a phone endpoint.");
    Reject(() => CertificateTools.Generate(Path.Combine(directory, "weak.pfx"), "127.0.0.1", "short"), "Weak generation password.");
    Reject(() => CertificateTools.LoadServerCertificate(path, "wrong-password", "127.0.0.1"), "Wrong PFX password.");
    Reject(() => CertificateTools.LoadServerCertificate(path, "", "127.0.0.1"), "Empty PFX password.");
    Reject(() => CertificateTools.LoadServerCertificate(path, password, "127.0.0.2"), "Changed interface address requires a matching certificate.");
    var invalid = Path.Combine(directory, "invalid.pfx");
    File.WriteAllText(invalid, "not a certificate");
    Reject(() => CertificateTools.LoadServerCertificate(invalid, password, "127.0.0.1"), "Malformed certificate.");
    InspectStatus(invalid, password, "127.0.0.1", CertificateStatus.UnreadableOrInvalid);
    void InvalidFixture(string name, DateTimeOffset start, DateTimeOffset end, CertificateStatus expected,
        bool privateKey = true, bool serverAuth = true, string address = "127.0.0.1", bool isAuthority = false)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=LAN Drop isolated invalid fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isAuthority, isAuthority, 0, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Parse(address));
        request.CertificateExtensions.Add(san.Build());
        if (serverAuth) request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(start, end);
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        var fixture = Path.Combine(directory, name + ".pfx");
        File.WriteAllBytes(fixture, (privateKey ? certificate : publicOnly).Export(X509ContentType.Pfx, password));
        var result = InspectStatus(fixture, password, "127.0.0.1", expected);
        if (expected == CertificateStatus.Ready)
            Check(result.ExpiringSoon && result.Authority is null, "Seven-day warning must work with a valid independently supplied identity.");
    }
    var now = DateTimeOffset.UtcNow;
    InvalidFixture("expired", now.AddDays(-3), now.AddDays(-1), CertificateStatus.Expired);
    InvalidFixture("not-yet-valid", now.AddDays(1), now.AddDays(2), CertificateStatus.NotYetValid);
    InvalidFixture("missing-private-key", now.AddMinutes(-1), now.AddDays(1), CertificateStatus.NoPrivateKey, privateKey: false);
    InvalidFixture("missing-server-auth", now.AddMinutes(-1), now.AddDays(1), CertificateStatus.WrongUsage, serverAuth: false);
    InvalidFixture("wrong-ip-san", now.AddMinutes(-1), now.AddDays(1), CertificateStatus.AddressMismatch, address: "127.0.0.2");
    InvalidFixture("authority-as-leaf", now.AddMinutes(-1), now.AddDays(1), CertificateStatus.WrongUsage, isAuthority: true);
    InvalidFixture("expiring-soon", now.AddMinutes(-1), now.AddDays(6), CertificateStatus.Ready);
    void AuthorityFixture(string name, bool certificateAuthority = true, X509KeyUsageFlags? keyUsage = X509KeyUsageFlags.KeyCertSign,
        bool expired = false, bool exportPrivateKey = false, bool expected = false)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=LAN Drop isolated issuer " + name, rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority, certificateAuthority, 0, true));
        if (keyUsage is { } usage) rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-3), expired ? now.AddDays(-1) : now.AddDays(10));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=LAN Drop isolated server " + name, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        leafRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        leafRequest.CertificateExtensions.Add(san.Build());
        // Signing directly allows invalid issuer fixtures without weakening the
        // production chain checks or requiring access to an actual signing CA.
        using var publicLeaf = leafRequest.Create(root.SubjectName, X509SignatureGenerator.CreateForRSA(rootKey, RSASignaturePadding.Pkcs1),
            now.AddMinutes(-1), now.AddDays(6), RandomNumberGenerator.GetBytes(16));
        using var leaf = publicLeaf.CopyWithPrivateKey(leafKey);
        var fixture = Path.Combine(directory, name + ".pfx");
        var companion = Path.ChangeExtension(fixture, ".ca.cer");
        File.WriteAllBytes(fixture, leaf.Export(X509ContentType.Pfx, password));
        File.WriteAllBytes(companion, root.Export(exportPrivateKey ? X509ContentType.Pfx : X509ContentType.Cert));
        var inspected = InspectStatus(fixture, password, "127.0.0.1", CertificateStatus.Ready);
        Check((inspected.Authority is not null) == expected, "Adjacent CA verification was incorrect for " + name);
        if (expected) Check(inspected.Authority!.Fingerprint == CertificateTools.Fingerprint(root), "Verified issuer fingerprint must match its own bytes.");
    }
    AuthorityFixture("valid-independent-issuer", expected: true);
    AuthorityFixture("issuer-not-ca", certificateAuthority: false);
    AuthorityFixture("issuer-no-key-cert-sign", keyUsage: X509KeyUsageFlags.DigitalSignature);
    AuthorityFixture("issuer-no-key-usage", keyUsage: null);
    AuthorityFixture("issuer-expired", expired: true);
    AuthorityFixture("issuer-private-key-container", exportPrivateKey: true);
    Check(TrustedRoots().SequenceEqual(before), "Certificate generation/loading must never change Windows root trust.");
    Console.WriteLine("PASS: certificate chain, public-only CA recovery, server-only PFX, safe typed inspection/errors, expiry warning, fingerprint, no trust-store mutation, no overwrite/partial-output residue, passwords, IPv4 SAN, validity, private key and server authentication.");
}
finally { Directory.Delete(directory, true); }
