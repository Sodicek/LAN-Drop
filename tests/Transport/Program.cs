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
    void InvalidFixture(string name, DateTimeOffset start, DateTimeOffset end, bool privateKey = true, bool serverAuth = true, string address = "127.0.0.1")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=LAN Drop isolated invalid fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Parse(address));
        request.CertificateExtensions.Add(san.Build());
        if (serverAuth) request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(start, end);
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        var fixture = Path.Combine(directory, name + ".pfx");
        File.WriteAllBytes(fixture, (privateKey ? certificate : publicOnly).Export(X509ContentType.Pfx, password));
        Reject(() => CertificateTools.LoadServerCertificate(fixture, password, "127.0.0.1"), name);
    }
    var now = DateTimeOffset.UtcNow;
    InvalidFixture("expired", now.AddDays(-3), now.AddDays(-1));
    InvalidFixture("not-yet-valid", now.AddDays(1), now.AddDays(2));
    InvalidFixture("missing-private-key", now.AddMinutes(-1), now.AddDays(1), privateKey: false);
    InvalidFixture("missing-server-auth", now.AddMinutes(-1), now.AddDays(1), serverAuth: false);
    InvalidFixture("wrong-ip-san", now.AddMinutes(-1), now.AddDays(1), address: "127.0.0.2");
    Check(TrustedRoots().SequenceEqual(before), "Certificate generation/loading must never change Windows root trust.");
    Console.WriteLine("PASS: certificate chain, public-only CA, server-only PFX, fingerprint, no trust-store mutation, no overwrite/partial-output residue, passwords, IPv4 SAN, validity, private key and server authentication.");
}
finally { Directory.Delete(directory, true); }
