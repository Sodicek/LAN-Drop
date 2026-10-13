using System;
using System.Linq;
using System.Text.Json;
using LanDrop.Desktop;
using LanDrop.Transport;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var input = new DiagnosticInput(DiagnosticSharingState.Running, DiagnosticTransport.Https,
            DiagnosticNetworkProfile.Private, CertificateStatus.Ready, true, 2, 3, 8);
        using var report = JsonDocument.Parse(DiagnosticReport.Create(input));
        var root = report.RootElement;
        string[] allowed = ["SchemaVersion", "ApplicationVersion", "WindowsVersion", "ProcessArchitecture", "RuntimeVersion",
            "SharingState", "Transport", "NetworkProfile", "CertificateStatus", "CertificateExpiringSoon", "DeviceCount", "PendingRequestCount", "RecentTransferCount"];
        Check(root.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(allowed.Order()), "Diagnostic schema must be an explicit whitelist.");
        Check(root.GetProperty("SchemaVersion").GetInt32() == 1, "Schema version.");
        Check(root.GetProperty("SharingState").GetString() == "Running" && root.GetProperty("Transport").GetString() == "Https", "Running transport summary.");
        Check(root.GetProperty("CertificateStatus").GetString() == "Ready" && root.GetProperty("CertificateExpiringSoon").GetBoolean(), "Certificate summary.");
        Check(root.GetProperty("DeviceCount").GetInt32() == 2 && root.GetProperty("RecentTransferCount").GetInt32() == 8, "Counts.");
        Check(!root.GetProperty("ApplicationVersion").GetString()!.Contains('+'), "No source revision metadata.");
        // The input contract excludes strings and opaque state completely, rather than
        // trying to scrub secrets after a broader object has already been serialized.
        Check(typeof(DiagnosticInput).GetProperties().All(p => p.PropertyType != typeof(string) && p.PropertyType != typeof(object)), "No free-form fields in input contract.");
        using var invalid = JsonDocument.Parse(DiagnosticReport.Create(input with {
            Sharing = (DiagnosticSharingState)999, Transport = (DiagnosticTransport)999,
            NetworkProfile = (DiagnosticNetworkProfile)999, CertificateStatus = (CertificateStatus)999,
            DeviceCount = -20, PendingRequestCount = int.MaxValue, RecentTransferCount = int.MaxValue
        }));
        var bounded = invalid.RootElement;
        foreach (var key in new[] { "SharingState", "Transport", "NetworkProfile", "CertificateStatus" })
            Check(bounded.GetProperty(key).GetString() == "Unknown", "Undefined enum must not leak arbitrary values: " + key);
        Check(bounded.GetProperty("DeviceCount").GetInt32() == 0 && bounded.GetProperty("PendingRequestCount").GetInt32() == 10_000 && bounded.GetProperty("RecentTransferCount").GetInt32() == 200, "Bounded counts.");
        Check(!bounded.GetProperty("CertificateExpiringSoon").GetBoolean(), "Invalid certificate cannot report ready expiry.");
        using var uncheckedReport = JsonDocument.Parse(DiagnosticReport.Create(input with { CertificateStatus = null }));
        Check(uncheckedReport.RootElement.GetProperty("CertificateStatus").GetString() == "NotChecked", "Unperformed check stays unknown.");
        Console.WriteLine("PASS: anonymous diagnostic whitelist, typed inputs, version privacy, enum normalization and count bounds.");
    }
}
