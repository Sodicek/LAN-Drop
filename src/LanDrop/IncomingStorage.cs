using System.Text.Json;

// A marker identifies our format, not a security boundary against another local
// process. Refuse redirections at every boundary; never recursively delete.
sealed class IncomingStorage : IDisposable
{
    internal const string Prefix = ".landrop-incoming-";
    internal const string Kind = "LAN-Drop-Incoming-v1";
    private readonly object gate = new();
    private readonly Dictionary<string, RunLease> runs = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int recovered, skipped;
    public int RecoveredIncompleteUploads { get { lock (gate) return recovered; } }
    public int RecoverySkippedRuns { get { lock (gate) return skipped; } }

    public string Prepare(string inbox, bool retryRecovery = false)
    {
        lock (gate)
        {
            inbox = StorageSafety.CreateFolder(inbox);
            if (runs.TryGetValue(inbox, out var existing))
            {
                ValidateRun(inbox, existing.Directory);
                if (retryRecovery) Recover(inbox);
                return existing.Directory;
            }
            if (runs.Count >= 128) throw new IOException("Too many incoming destinations in this sharing run.");
            Recover(inbox);
            var run = Guid.NewGuid().ToString("N");
            var directory = Child(inbox, Prefix + run);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Incoming staging already exists.");
            StorageSafety.CreateFolder(directory);
            FileStream? lease = null;
            try
            {
                lease = new FileStream(Child(directory, ".landrop-lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                using (var marker = new FileStream(Child(directory, ".landrop-owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    JsonSerializer.Serialize(marker, new { Kind, Run = run });
                ValidateRun(inbox, directory);
                runs.Add(inbox, new RunLease(directory, lease));
                return directory;
            }
            catch { lease?.Dispose(); throw; } // Unmarked/partial runs are preserved.
        }
    }

    public static string Payload(string inbox, string directory)
    {
        ValidateRun(inbox, directory);
        return Child(directory, ".upload-" + Guid.NewGuid().ToString("N") + ".part");
    }

    public static void ValidatePayload(string inbox, string directory, string payload, bool exists)
    {
        ValidateRun(inbox, directory);
        if (!IsPayload(Path.GetFileName(payload)) || !Same(Child(directory, Path.GetFileName(payload)), payload)) throw new UnsafeStorageException();
        if (exists && !PlainFile(payload)) throw new UnsafeStorageException();
    }

    private void Recover(string inbox)
    {
        var entries = 0; var candidates = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(inbox))
        {
            if (++entries > 4096) { skipped++; break; }
            if (!Path.GetFileName(entry).StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (runs.TryGetValue(inbox, out var own) && Same(entry, own.Directory)) continue;
            if (++candidates > 128) { skipped++; break; }
            var removed = 0;
            try { if (!TryCleanup(inbox, entry, out removed)) skipped++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeStorageException or JsonException) { skipped++; }
            finally { recovered += removed; }
        }
    }

    private static bool TryCleanup(string inbox, string directory, out int removed)
    {
        removed = 0;
        ValidateRun(inbox, directory);
        var marker = Child(directory, ".landrop-owner");
        var leasePath = Child(directory, ".landrop-lease");
        if (!PlainFile(marker) || !PlainFile(leasePath) || new FileInfo(leasePath).Length != 0) return false;
        using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ValidateRun(inbox, directory);
            if (!PlainFile(marker) || !PlainFile(leasePath)) return false;
            using var owner = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (!ValidMarker(owner, Path.GetFileName(directory)[Prefix.Length..])) return false;
            var payloads = new List<string>(); var count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 258 || !PlainFile(entry)) return false;
                var name = Path.GetFileName(entry);
                if (name == ".landrop-owner" || name == ".landrop-lease") continue;
                if (!IsPayload(name)) return false;
                payloads.Add(entry);
            }
            // Preflight every payload lock before deleting any. Preserve a run
            // containing a locked file rather than partially cleaning it.
            var locks = new List<FileStream>();
            try
            {
                foreach (var payload in payloads)
                {
                    ValidatePayload(inbox, directory, payload, true);
                    locks.Add(new FileStream(payload, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                }
            }
            finally { foreach (var handle in locks) handle.Dispose(); }
            foreach (var payload in payloads)
            {
                ValidatePayload(inbox, directory, payload, true);
                File.Delete(payload); removed++;
            }
            ValidateRun(inbox, directory);
            if (!PlainFile(marker)) throw new UnsafeStorageException();
            owner.Dispose();
            File.Delete(marker);
        }
        ValidateRun(inbox, directory);
        if (!PlainFile(leasePath)) throw new UnsafeStorageException();
        File.Delete(leasePath);
        Directory.Delete(directory, false);
        return true;
    }

    private static bool ValidMarker(FileStream input, string run)
    {
        if (input.Length is < 1 or > 512) return false;
        var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2) return false;
        return root.TryGetProperty("Kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == Kind
            && root.TryGetProperty("Run", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == run;
    }

    private static bool IsHex(string value) => value.Length == 32 && value.All(c => "0123456789abcdef".Contains(c));
    private static bool IsPayload(string name) => name.Length == 45 && name.StartsWith(".upload-", StringComparison.Ordinal) && name.EndsWith(".part", StringComparison.Ordinal) && IsHex(name[8..^5]);
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string Child(string parent, string name)
    {
        var result = Path.GetFullPath(Path.Combine(parent, name));
        if (!Same(Path.GetDirectoryName(result)!, parent)) throw new UnsafeStorageException();
        return result;
    }
    private static void ValidateRun(string inbox, string directory)
    {
        StorageSafety.ValidateFolder(inbox);
        var name = Path.GetFileName(directory);
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !IsHex(name[Prefix.Length..]) || !Same(Child(inbox, name), directory)) throw new UnsafeStorageException();
        StorageSafety.ValidateFolder(directory);
        if (!Directory.Exists(directory)) throw new UnsafeStorageException();
    }
    private static bool PlainFile(string path) => File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var entry in runs)
            {
                entry.Value.Lease.Dispose();
                try { TryCleanup(entry.Key, entry.Value.Directory, out _); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeStorageException or JsonException) { }
            }
            runs.Clear();
        }
    }
    private sealed record RunLease(string Directory, FileStream Lease);
}
