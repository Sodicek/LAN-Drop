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
}
if (NetworkDiagnostics.CanShare((NetworkProfile)int.MaxValue))
    throw new Exception("Unknown future profiles must fail closed");
Console.WriteLine("PASS: public/unknown profiles blocked; private/domain/loopback allowed.");
