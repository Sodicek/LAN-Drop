using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanDrop.Transport;

namespace LanDrop.Desktop;

enum DiagnosticSharingState { Stopped, Starting, Running, Stopping }
enum DiagnosticTransport { Https, HttpPreview }
enum DiagnosticNetworkProfile { NotSelected, Unknown, Public, Private, Domain, Loopback }

// Deliberately cannot accept logs, user paths, device names, tokens or addresses.
sealed record DiagnosticInput(
    DiagnosticSharingState Sharing,
    DiagnosticTransport Transport,
    DiagnosticNetworkProfile NetworkProfile,
    CertificateStatus? CertificateStatus,
    bool CertificateExpiringSoon,
    int DeviceCount = 0,
    int PendingRequestCount = 0,
    int RecentTransferCount = 0,
    int RecoveredIncompleteUploads = 0,
    int RecoverySkippedRuns = 0);

static class DiagnosticReport
{
    public static string Create(DiagnosticInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        // Public release version only; reject arbitrary assembly metadata.
        if (!Regex.IsMatch(version, @"\A[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}(?:-[a-z0-9][a-z0-9.-]{0,32})?\z", RegexOptions.CultureInvariant)) version = "unknown";
        var status = input.CertificateStatus.HasValue ? EnumValue(input.CertificateStatus.Value) : "NotChecked";
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 2,
            ApplicationVersion = version,
            WindowsVersion = OperatingSystem.IsWindows() ? Environment.OSVersion.Version.ToString() : "NotWindows",
            ProcessArchitecture = EnumValue(RuntimeInformation.ProcessArchitecture),
            RuntimeVersion = Environment.Version.ToString(),
            SharingState = EnumValue(input.Sharing),
            Transport = EnumValue(input.Transport),
            NetworkProfile = EnumValue(input.NetworkProfile),
            CertificateStatus = status,
            CertificateExpiringSoon = input.CertificateStatus == LanDrop.Transport.CertificateStatus.Ready && input.CertificateExpiringSoon,
            DeviceCount = Math.Clamp(input.DeviceCount, 0, 10_000),
            PendingRequestCount = Math.Clamp(input.PendingRequestCount, 0, 10_000),
            RecentTransferCount = Math.Clamp(input.RecentTransferCount, 0, 200),
            RecoveredIncompleteUploads = Math.Clamp(input.RecoveredIncompleteUploads, 0, 10_000),
            RecoverySkippedRuns = Math.Clamp(input.RecoverySkippedRuns, 0, 10_000)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string EnumValue<T>(T value) where T : struct, Enum => Enum.IsDefined(value) ? value.ToString() : "Unknown";
}
