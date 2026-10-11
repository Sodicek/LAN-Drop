using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanDrop.Desktop;

var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("abc")));
void Check(string json, long size, string hash, bool expected)
{
    using var document = JsonDocument.Parse(json);
    if (UploadReceipt.Validate(document.RootElement, size, hash) != expected)
        throw new Exception("Unexpected receipt validation: " + json);
}
string Receipt(long size, string hash) => JsonSerializer.Serialize(new { id = "id", name = "test.txt", size, sha256 = hash, verified = true });
Check(Receipt(3, digest), 3, digest, true);
Check(Receipt(3, digest.ToUpperInvariant()), 3, digest, true);
Check(Receipt(4, digest), 3, digest, false);
Check(Receipt(3, new string('0', 64)), 3, digest, false);
Check(Receipt(3, "broken"), 3, digest, false);
Check(Receipt(3, digest), -1, digest, false);
Check(Receipt(3, digest), 3, "broken", false);
foreach (var json in new[] {
    "null", "[]", "{}",
    JsonSerializer.Serialize(new { size = "3", sha256 = digest, verified = true }),
    JsonSerializer.Serialize(new { size = 3.5, sha256 = digest, verified = true }),
    JsonSerializer.Serialize(new { size = 3, sha256 = digest, verified = false }),
    JsonSerializer.Serialize(new { size = 3, sha256 = digest, verified = "true" }),
    JsonSerializer.Serialize(new { size = 3, sha256 = digest }),
    JsonSerializer.Serialize(new { size = 3, sha256 = 42, verified = true })
}) Check(json, 3, digest, false);
var emptyDigest = Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>()));
Check(Receipt(0, emptyDigest), 0, emptyDigest, true);
foreach (var bad in new string?[] { null, "", new string('0', 63), new string('0', 65), new string('z', 64), new string('0', 63) + " " })
    if (UploadReceipt.IsSha256(bad)) throw new Exception("Invalid SHA-256 accepted");
Console.WriteLine("PASS: actual desktop receipt helper verifies hash/size/flag, malformed JSON fields and empty-file receipts.");
