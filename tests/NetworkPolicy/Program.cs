using System;
using LanDrop.Desktop;

foreach (var profile in Enum.GetValues<NetworkProfile>())
{
    var expected = profile switch
    {
        NetworkProfile.Private => true,
        NetworkProfile.Domain => true,
        NetworkProfile.Loopback => true,
        _ => false
    };
    if (NetworkDiagnostics.CanShare(profile) != expected)
        throw new Exception("Unexpected sharing decision for " + profile);
    if (NetworkDiagnostics.CanContinueSharing(profile) != expected)
        throw new Exception("Unexpected active sharing decision for " + profile);
}
if (NetworkDiagnostics.CanShare((NetworkProfile)int.MaxValue))
    throw new Exception("Unknown future profiles must fail closed");
if (NetworkDiagnostics.CanContinueSharing(null))
    throw new Exception("Disappeared network adapters must stop sharing");
if (NetworkDiagnostics.CanContinueSharing((NetworkProfile)int.MaxValue))
    throw new Exception("Unknown future profiles must stop sharing");
Console.WriteLine("PASS: public/unknown/missing profiles blocked; private/domain/loopback allowed.");
