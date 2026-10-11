using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanDrop.Transport;

public sealed record GeneratedCertificate(string CertificatePath, string AuthorityPath, string AuthorityFingerprint);

/// <summary>Creates an isolated local HTTPS identity without changing any trust store.</summary>
public static class CertificateTools
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    public static GeneratedCertificate Generate(string certificatePath, string address, string password)
    {
        var ip = ParseAddress(address);
        if (string.IsNullOrEmpty(password) || password.Length < 12)
            throw new ArgumentException("Use a certificate password of at least 12 characters.");
        var path = Path.GetFullPath(certificatePath);
        var authorityPath = Path.ChangeExtension(path, ".ca.cer");
        if (path.Equals(authorityPath, StringComparison.OrdinalIgnoreCase) || File.Exists(path) || File.Exists(authorityPath))
            throw new IOException("Certificate output files already exist. Choose a new certificate file name.");

        using var authorityKey = RSA.Create(3072);
        var authorityRequest = new CertificateRequest($"CN=LAN Drop Local CA {Guid.NewGuid():N}", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var authorityIdentifier = new X509SubjectKeyIdentifierExtension(authorityRequest.PublicKey, false);
        authorityRequest.CertificateExtensions.Add(authorityIdentifier);
        authorityRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(authorityIdentifier));
        var now = DateTimeOffset.UtcNow;
        using var authority = authorityRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(30));

        using var serverKey = RSA.Create(3072);
        var serverRequest = new CertificateRequest($"CN=LAN Drop {ip}", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(ServerAuthentication) }, true));
        serverRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(serverRequest.PublicKey, false));
        serverRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(ip);
        serverRequest.CertificateExtensions.Add(names.Build());
        using var publicServer = serverRequest.Create(authority, now.AddMinutes(-5), now.AddDays(30).AddSeconds(-1), RandomNumberGenerator.GetBytes(16));
        using var server = publicServer.CopyWithPrivateKey(serverKey);
        // Only the leaf private key is exported. The signing key dies with this call.
        var encrypted = server.ExportPkcs12(new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000), password);
        var certificateCreated = false;
        var authorityCreated = false;
        try
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            certificateCreated = true;
            // Reserve both destinations before writing so no existing file is overwritten.
            using var publicOutput = new FileStream(authorityPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            authorityCreated = true;
            output.Write(encrypted);
            output.Flush(true);
            publicOutput.Write(authority.Export(X509ContentType.Cert));
            publicOutput.Flush(true);
        }
        catch
        {
            // The using scopes close first. Never remove an output that this
            // invocation did not create (including a competing creator's CA).
            if (certificateCreated) DeleteCreatedOutput(path);
            if (authorityCreated) DeleteCreatedOutput(authorityPath);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
        return new GeneratedCertificate(path, authorityPath, Fingerprint(authority));
    }

    public static X509Certificate2 LoadServerCertificate(string path, string password, string address)
    {
        var ip = ParseAddress(address);
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("The certificate password is required.");
        // Windows Schannel requires a provider-backed key. Do not request a
        // persistent key container or install the certificate in a trust store;
        // disposing the certificate releases the imported temporary leaf key.
        var keyStorage = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet;
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.GetFullPath(path), password, keyStorage);
        try
        {
            var now = DateTime.UtcNow;
            if (!certificate.HasPrivateKey) throw new CryptographicException("The HTTPS certificate must include its private key.");
            if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
                throw new CryptographicException("The HTTPS certificate is expired or is not yet valid.");
            if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority))
                throw new CryptographicException("Use a server certificate, not a certificate authority certificate.");
            var usages = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
            if (usages.Length != 1 || !usages[0].EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == ServerAuthentication))
                throw new CryptographicException("The HTTPS certificate must permit server authentication.");
            var names = certificate.Extensions.Where(e => e.Oid?.Value == "2.5.29.17").ToArray();
            if (names.Length != 1 || !new X509SubjectAlternativeNameExtension(names[0].RawData, names[0].Critical).EnumerateIPAddresses().Any(a => a.Equals(ip)))
                throw new CryptographicException("The HTTPS certificate does not contain the selected IPv4 address in its subject alternative names.");
            return certificate;
        }
        catch { certificate.Dispose(); throw; }
    }

    public static string Fingerprint(X509Certificate2 certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();

    private static void DeleteCreatedOutput(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static IPAddress ParseAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast))
            throw new ArgumentException("Choose one specific IPv4 address for the HTTPS certificate.");
        return ip;
    }
}
