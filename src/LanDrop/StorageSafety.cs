using System.Text.Json;

// These checks reject redirections at each boundary. They do not claim to defeat
// a hostile local process replacing paths between operating-system calls.
static class StorageSafety
{
    public static string ValidateFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\"))
            throw new UnsafeStorageException();
        var full = Path.GetFullPath(path);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                    throw new UnsafeStorageException();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }
    public static string CreateFolder(string path)
    {
        var full = ValidateFolder(path);
        Directory.CreateDirectory(full);
        return ValidateFolder(full);
    }
    public static string Volume(string path) => Path.GetPathRoot(ValidateFolder(path))!;
    public static long AvailableBytes(string volume) => new DriveInfo(volume).AvailableFreeSpace;

    public static void CleanupOutboxes(string root)
    {
        ValidateFolder(root);
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root).ToArray())
        {
            try { CleanupRun(directory); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or UnsafeStorageException) { }
        }
    }
    public static void CleanupRun(string directory)
    {
        ValidateFolder(directory);
        var name = Path.GetFileName(directory);
        if (name.Length != 12 || !name.All(c => "0123456789abcdef".Contains(c))) return;
        var marker = Path.Combine(directory, ".landrop-owner");
        var leasePath = Path.Combine(directory, ".landrop-lease");
        if (!PlainFile(marker) || !PlainFile(leasePath)) return;
        var owner = JsonSerializer.Deserialize<OutboxOwner>(File.ReadAllText(marker));
        if (owner != new OutboxOwner("LAN-Drop-Outbox-v1", name)) return;
        // Never recurse into directories or follow links. Refuse the entire run
        // when unexpected nested entries exist, even if its marker is valid.
        var entries = Directory.GetFileSystemEntries(directory);
        if (entries.Any(entry => !PlainFile(entry))) return;
        using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ValidateFolder(directory);
            foreach (var entry in entries.Where(entry => entry != leasePath && entry != marker))
            {
                ValidateFolder(directory);
                if (!PlainFile(entry)) throw new UnsafeStorageException();
                File.Delete(entry);
            }
            if (!PlainFile(marker)) throw new UnsafeStorageException();
            File.Delete(marker);
        }
        ValidateFolder(directory);
        if (!PlainFile(leasePath)) throw new UnsafeStorageException();
        File.Delete(leasePath);
        Directory.Delete(directory, false);
    }
    private static bool PlainFile(string path)
    {
        if (!File.Exists(path)) return false;
        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
    }
    public static FileStream CreateRunLease(string directory)
    {
        CreateFolder(directory);
        var lease = new FileStream(Path.Combine(directory, ".landrop-lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try { File.WriteAllText(Path.Combine(directory, ".landrop-owner"), JsonSerializer.Serialize(new OutboxOwner("LAN-Drop-Outbox-v1", Path.GetFileName(directory)))); return lease; }
        catch { lease.Dispose(); throw; }
    }
}
sealed class UnsafeStorageException : Exception { }
sealed class InsufficientDiskSpaceException : Exception { }
record OutboxOwner(string Kind, string Run);
