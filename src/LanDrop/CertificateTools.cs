using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanDrop.Transport;

public sealed record GeneratedCertificate(string CertificatePath, string AuthorityPath, string AuthorityFingerprint);

public enum CertificateStatus
{
    Ready, MissingFile, PasswordRequired, UnreadableOrInvalid, Expired,
    NotYetValid, AddressMismatch, NoPrivateKey, WrongUsage, InvalidAddress
}

/// <summary>A verified public issuing certificate, not evidence of trust on any phone.</summary>
public sealed record PublicCertificateMetadata(string Path, string Fingerprint);

public sealed record CertificateInspection(
    CertificateStatus Status, DateTimeOffset? NotAfterUtc, bool ExpiringSoon,
    string? Fingerprint, IReadOnlyList<string> IpAddresses, PublicCertificateMetadata? Authority);

/// <summary>Contains only an actionable code and a fixed message, never provider details or secrets.</summary>
public sealed class CertificateValidationException : CryptographicException
{
    public CertificateStatus Status { get; }
    public CertificateValidationException(CertificateStatus status) : base(status switch
    {
        CertificateStatus.MissingFile => "The HTTPS certificate file is missing.",
        CertificateStatus.PasswordRequired => "The certificate password is required.",
        CertificateStatus.Expired => "The HTTPS certificate has expired.",
        CertificateStatus.NotYetValid => "The HTTPS certificate is not yet valid.",
        CertificateStatus.AddressMismatch => "The HTTPS certificate does not contain the selected IPv4 address.",
        CertificateStatus.NoPrivateKey => "The HTTPS certificate must include its private key.",
        CertificateStatus.WrongUsage => "The HTTPS certificate must be a server certificate permitting server authentication.",
        CertificateStatus.InvalidAddress => "Choose one specific IPv4 address for the HTTPS certificate.",
        _ => "The HTTPS certificate could not be read. Check the file and password."
    }) => Status = status;
}

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

    public static X509Certificate2 LoadServerCertificate(string path, string password, string address) =>
        LoadValidatedCertificate(path, password, address, inspectionOnly: false);

    private static X509Certificate2 LoadValidatedCertificate(string path, string password, string address, bool inspectionOnly)
    {
        IPAddress ip;
        try { ip = ParseAddress(address); }
        catch (ArgumentException) { throw new CertificateValidationException(CertificateStatus.InvalidAddress); }
        if (string.IsNullOrWhiteSpace(path)) throw new CertificateValidationException(CertificateStatus.MissingFile);
        if (string.IsNullOrEmpty(password)) throw new CertificateValidationException(CertificateStatus.PasswordRequired);
        // Windows Schannel requires a provider-backed key. Do not request a
        // persistent key container or install the certificate in a trust store;
        // disposing the certificate releases the imported temporary leaf key.
        var keyStorage = OperatingSystem.IsWindows() && !inspectionOnly
            ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet;
        X509Certificate2 certificate;
        byte[]? encrypted = null;
        try
        {
            // Read the file ourselves so native PKCS#12 provider wrapping does
            // not turn a missing file into an indistinguishable password error.
            encrypted = File.ReadAllBytes(Path.GetFullPath(path));
            certificate = X509CertificateLoader.LoadPkcs12(encrypted, password, keyStorage);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { throw new CertificateValidationException(CertificateStatus.MissingFile); }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new CertificateValidationException(CertificateStatus.UnreadableOrInvalid); }
        finally { if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted); }
        try
        {
            var now = DateTime.UtcNow;
            if (!certificate.HasPrivateKey) throw new CertificateValidationException(CertificateStatus.NoPrivateKey);
            if (certificate.NotBefore.ToUniversalTime() > now)
                throw new CertificateValidationException(CertificateStatus.NotYetValid);
            if (certificate.NotAfter.ToUniversalTime() <= now)
                throw new CertificateValidationException(CertificateStatus.Expired);
            if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority))
                throw new CertificateValidationException(CertificateStatus.WrongUsage);
            var usages = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
            if (usages.Length != 1 || !usages[0].EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == ServerAuthentication))
                throw new CertificateValidationException(CertificateStatus.WrongUsage);
            var names = certificate.Extensions.Where(e => e.Oid?.Value == "2.5.29.17").ToArray();
            if (names.Length != 1 || !new X509SubjectAlternativeNameExtension(names[0].RawData, names[0].Critical).EnumerateIPAddresses().Any(a => a.Equals(ip)))
                throw new CertificateValidationException(CertificateStatus.AddressMismatch);
            return certificate;
        }
        catch (CertificateValidationException) { certificate.Dispose(); throw; }
        catch (CryptographicException)
        { certificate.Dispose(); throw new CertificateValidationException(CertificateStatus.UnreadableOrInvalid); }
        catch { certificate.Dispose(); throw; }
    }

    public static CertificateInspection InspectServerCertificate(string path, string password, string address)
    {
        try
        {
            // Inspection does not serve TLS, so even on Windows it can keep the
            // decrypted leaf key entirely in memory and avoid provider key churn.
            using var certificate = LoadValidatedCertificate(path, password, address, inspectionOnly: true);
            var expiry = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            var names = certificate.Extensions.Single(e => e.Oid?.Value == "2.5.29.17");
            var addresses = new X509SubjectAlternativeNameExtension(names.RawData, names.Critical)
                .EnumerateIPAddresses().Select(a => a.ToString()).Distinct().Order().ToArray();
            return new CertificateInspection(CertificateStatus.Ready, expiry,
                expiry <= DateTimeOffset.UtcNow.AddDays(7), Fingerprint(certificate),
                Array.AsReadOnly(addresses), ResolvePublicAuthority(path, certificate));
        }
        catch (CertificateValidationException e)
        {
            return new CertificateInspection(e.Status, null, false, null, Array.Empty<string>(), null);
        }
    }

    private static PublicCertificateMetadata? ResolvePublicAuthority(string path, X509Certificate2 certificate)
    {
        try
        {
            var authorityPath = Path.ChangeExtension(Path.GetFullPath(path), ".ca.cer");
            using var authority = X509CertificateLoader.LoadCertificateFromFile(authorityPath);
            if (authority.HasPrivateKey || !authority.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority))
                return null;
            var keyUsages = authority.Extensions.OfType<X509KeyUsageExtension>().ToArray();
            if (keyUsages.Length != 1 || !keyUsages[0].KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign)) return null;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(authority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthentication));
            if (!chain.Build(certificate) || chain.ChainElements.Count < 2 ||
                !chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(authority.RawData)) return null;
            return new PublicCertificateMetadata(authorityPath, Fingerprint(authority));
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return null; }
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
