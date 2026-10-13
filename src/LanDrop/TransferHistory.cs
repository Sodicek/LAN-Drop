using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Completion metadata is opt-in and never restores files, grants, or sessions.
sealed class TransferHistory : IDisposable
{
    const int MaximumBytes = 2 * 1024 * 1024;
    readonly object gate = new();
    readonly string folder;
    readonly string path;
    readonly string checkpoint;
    readonly string leasePath;
    FileStream? lease;
    bool available;
    bool enabled;
    HistoryEntry[] entries = [];
    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TransferHistory(string storage) : this(storage, false) { }
    TransferHistory(string storage, bool readOnly)
    {
        folder = Path.Combine(storage, "History");
        path = Path.Combine(folder, "history.json");
        checkpoint = Path.Combine(folder, ".checkpoint");
        leasePath = Path.Combine(folder, ".lease");
        try
        {
            if (readOnly) StorageSafety.ValidateFolder(folder);
            else
            {
                StorageSafety.CreateFolder(folder);
                CheckFile(leasePath);
                lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            ValidatePaths();
            if (File.Exists(checkpoint)) throw new IOException(); // Preserve interrupted checkpoints for inspection.
            if (File.Exists(path)) Read();
            available = true;
        }
        catch (Exception e) when (StorageError(e)) { lease?.Dispose(); lease = null; }
    }

    public static HistorySnapshot ReadOnlySnapshot(string storage)
    {
        using var history = new TransferHistory(storage, true);
        return history.Snapshot();
    }
    public HistorySnapshot Snapshot()
    {
        lock (gate) return new HistorySnapshot(enabled, available, entries.ToArray(), available ? null : "history_unavailable");
    }
    public bool SetEnabled(bool value)
    {
        lock (gate) return Save(value, entries);
    }
    public bool Clear()
    {
        lock (gate) return Save(enabled, []);
    }
    public void Record(string name, long size, string direction)
    {
        lock (gate)
        {
            if (!available || !enabled) return;
            var entry = new HistoryEntry(Guid.NewGuid().ToString("N"), name, size, direction, DateTimeOffset.UtcNow);
            if (!Valid(entry)) { available = false; return; }
            Save(true, new[] { entry }.Concat(entries).Take(200).ToArray());
        }
    }

    bool Save(bool nextEnabled, HistoryEntry[] nextEntries)
    {
        if (!available || lease is null) return false;
        var created = false;
        try
        {
            ValidatePaths();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new HistoryDocument(1, nextEnabled, nextEntries), Options);
            if (bytes.Length > MaximumBytes) throw new IOException();
            using (var stream = new FileStream(checkpoint, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(bytes);
                stream.Flush(true);
            }
            ValidatePaths();
            File.Move(checkpoint, path, true);
            enabled = nextEnabled;
            entries = nextEntries;
            return true;
        }
        catch (Exception e) when (StorageError(e))
        {
            available = false;
            if (created)
            {
                try { StorageSafety.ValidateFolder(folder); CheckFile(checkpoint); File.Delete(checkpoint); }
                catch (Exception cleanup) when (StorageError(cleanup)) { }
            }
            return false;
        }
    }
    void Read()
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new JsonException();
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        Properties(root, "version", "enabled", "entries");
        if (root.GetProperty("version").GetInt32() != 1) throw new JsonException();
        var savedEnabled = root.GetProperty("enabled").GetBoolean();
        var array = root.GetProperty("entries");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 200) throw new JsonException();
        var saved = new List<HistoryEntry>();
        var ids = new HashSet<string>();
        foreach (var element in array.EnumerateArray())
        {
            Properties(element, "id", "name", "size", "direction", "completedAt");
            var entry = new HistoryEntry(element.GetProperty("id").GetString()!, element.GetProperty("name").GetString()!, element.GetProperty("size").GetInt64(), element.GetProperty("direction").GetString()!, element.GetProperty("completedAt").GetDateTimeOffset());
            if (!Valid(entry) || !ids.Add(entry.Id)) throw new JsonException();
            saved.Add(entry);
        }
        enabled = savedEnabled;
        entries = saved.ToArray();
    }
    static void Properties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException();
        var seen = new HashSet<string>();
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException();
        if (seen.Count != names.Length) throw new JsonException();
    }
    static bool Valid(HistoryEntry entry) => entry.Id is { Length: 32 } && entry.Id.All(c => "0123456789abcdef".Contains(c))
        && !string.IsNullOrWhiteSpace(entry.Name) && entry.Name.Length <= 255 && entry.Name is not "." and not ".."
        && entry.Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !entry.Name.Contains('/') && !entry.Name.Contains('\\')
        && !entry.Name.EndsWith('.') && !entry.Name.EndsWith(' ') && !entry.Name.Any(char.IsControl)
        && entry.Size is >= 0 and <= 10L * 1024 * 1024 * 1024 && entry.Direction is "incoming" or "outgoing"
        && entry.CompletedAt >= DateTimeOffset.UnixEpoch && entry.CompletedAt <= DateTimeOffset.UtcNow.AddMinutes(5);
    void ValidatePaths()
    {
        StorageSafety.ValidateFolder(folder);
        CheckFile(leasePath);
        CheckFile(path);
        CheckFile(checkpoint);
    }
    static void CheckFile(string value)
    {
        try
        {
            if ((File.GetAttributes(value) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new UnsafeStorageException();
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    static bool StorageError(Exception e) => e is IOException or UnauthorizedAccessException or UnsafeStorageException or JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException;
    public void Dispose() { lock (gate) { available = false; lease?.Dispose(); lease = null; } }
    record HistoryDocument(int Version, bool Enabled, HistoryEntry[] Entries);
}
record HistoryEntry(string Id, string Name, long Size, string Direction, DateTimeOffset CompletedAt);
record HistorySnapshot(bool Enabled, bool Available, HistoryEntry[] Entries, string? Error);
