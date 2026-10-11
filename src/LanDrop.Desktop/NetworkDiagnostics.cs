using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanDrop.Desktop;

enum NetworkProfile { Unknown, Public, Private, Domain, Loopback }
record NetworkChoice(string Address, string Name, string AdapterType, NetworkProfile Profile)
{
    public string Label { get; init; } = Name + " · " + Address;
}

static class NetworkDiagnostics
{
    // Read-only Windows networking query. Fixed command, bounded runtime, no user interpolation.
    public static Dictionary<int, NetworkProfile> Profiles()
    {
        try
        {
            var executable = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var info = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach(var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; @(Get-NetConnectionProfile | Select-Object InterfaceIndex,@{Name='Category';Expression={$_.NetworkCategory.ToString()}}) | ConvertTo-Json -Compress" }) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(4000)) { process.Kill(true); return new(); }
            if(process.ExitCode != 0) return new();
            using var json = System.Text.Json.JsonDocument.Parse(output.GetAwaiter().GetResult());
            var rows = json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? json.RootElement.EnumerateArray().ToArray() : new[] { json.RootElement };
            var result = new Dictionary<int, NetworkProfile>();
            foreach(var row in rows)
            {
                var index = row.GetProperty("InterfaceIndex").GetInt32();
                var profile = row.GetProperty("Category").GetString() switch { "Private" => NetworkProfile.Private, "DomainAuthenticated" => NetworkProfile.Domain, "Public" => NetworkProfile.Public, _ => NetworkProfile.Unknown };
                if(result.TryGetValue(index, out var prior) && prior != profile) profile = NetworkProfile.Unknown;
                result[index] = profile;
            }
            return result;
        }
        catch(Exception) { return new(); }
    }
    public static NetworkChoice[] Interfaces()
    {
        var profiles = Profiles();
        return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => new NetworkChoice(a.Address.ToString(), n.Name,
                    n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "AdapterWifi" : n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "AdapterEthernet" : "AdapterOther",
                    profiles.GetValueOrDefault(n.GetIPProperties().GetIPv4Properties()?.Index ?? -1, NetworkProfile.Unknown))))
            .Append(new NetworkChoice("127.0.0.1", "Local test", "AdapterLoopback", NetworkProfile.Loopback)).ToArray();
    }
    public static bool CanShare(NetworkProfile profile) => profile is NetworkProfile.Private or NetworkProfile.Domain or NetworkProfile.Loopback;
}
