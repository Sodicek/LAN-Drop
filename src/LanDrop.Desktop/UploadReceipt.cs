using System;
using System.Text.Json;

namespace LanDrop.Desktop;

internal static class UploadReceipt
{
    internal static bool IsSha256(string? value) => value is { Length: 64 } &&
        Array.TrueForAll(value.ToCharArray(), character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    internal static bool Validate(JsonElement receipt, long expectedSize, string expectedSha256) =>
        receipt.ValueKind == JsonValueKind.Object && expectedSize >= 0 && IsSha256(expectedSha256) &&
        receipt.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var actualSize) && actualSize == expectedSize &&
        receipt.TryGetProperty("sha256", out var hash) && hash.ValueKind == JsonValueKind.String && IsSha256(hash.GetString()) &&
        string.Equals(hash.GetString(), expectedSha256, StringComparison.OrdinalIgnoreCase) &&
        receipt.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True;
}
