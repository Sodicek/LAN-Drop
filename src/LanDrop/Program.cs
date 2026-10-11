using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using QRCoder;

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Port", 45821);
string[] Addresses() => NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).Distinct().ToArray();
var listeningAddress = builder.Configuration["ListenAddress"] ?? "127.0.0.1";
if (!IPAddress.TryParse(listeningAddress, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || (listeningAddress != "127.0.0.1" && !Addresses().Contains(listeningAddress))) throw new ArgumentException("ListenAddress must be loopback or an active local IPv4 address.");
builder.WebHost.UseUrls(listeningAddress == "127.0.0.1" ? [$"http://127.0.0.1:{port}"] : [$"http://127.0.0.1:{port}", $"http://{listeningAddress}:{port}"]);
const long maxSize = 10L * 1024 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxSize);
var app = builder.Build();
string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
object Error(string code, string message) => new { code, error = message };
var admin = Secret(); var pairing = Secret(); var pairingExpires = DateTimeOffset.UtcNow.AddMinutes(10);
var gate = new object();
var sessions = new ConcurrentDictionary<string, Device>();
var files = new ConcurrentDictionary<string, SharedFile>();
var slots = new SemaphoreSlim(Math.Max(1, builder.Configuration.GetValue("MaxConcurrentUploads", 3)));
var maxStoredBytes = builder.Configuration.GetValue<long>("MaxStoredBytes", 20L * 1024 * 1024 * 1024);
long reservedBytes = 0;
var maxRegisteredFiles = Math.Max(1, builder.Configuration.GetValue("MaxRegisteredFiles", 1000));
var reservedFiles = 0;
var storage = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDrop"));
StorageSafety.CreateFolder(storage);
var diskSafetyBuffer = Math.Max(0, builder.Configuration.GetValue<long>("DiskSafetyBufferBytes", 64L * 1024 * 1024));
var diskReservations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
var settingsPath = Path.Combine(storage, "settings.json");
var inbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "LAN-Drop");
if (File.Exists(settingsPath))
{
    try { var saved = JsonSerializer.Deserialize<FolderRequest>(File.ReadAllText(settingsPath)); if (saved is not null && Path.IsPathFullyQualified(saved.Path) && !saved.Path.StartsWith(@"\\")) inbox = StorageSafety.CreateFolder(saved.Path); }
    catch (Exception e) when (e is IOException or JsonException or ArgumentException or UnsafeStorageException or UnauthorizedAccessException) { }
}
var outboxRoot = StorageSafety.CreateFolder(Path.Combine(storage, "Outbox"));
StorageSafety.CleanupOutboxes(outboxRoot);
var outbox = Path.Combine(outboxRoot, Secret()[..12]);
StorageSafety.CreateFolder(inbox);
var outboxLease = StorageSafety.CreateRunLease(outbox);
bool IsAdmin(HttpContext c) => c.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote) && c.Request.Headers["X-LanDrop-Admin"] == admin;
Device? GetDevice(HttpContext c) => sessions.TryGetValue(c.Request.Headers["X-LanDrop-Session"].ToString(), out var d) && d.Expires > DateTimeOffset.UtcNow ? d : null;
bool Allowed(HttpContext c) => IsAdmin(c) || GetDevice(c)?.Approved == true;
bool Reserved(string name) { var stem = name.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant(); return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789\u00b9\u00b2\u00b3".Contains(stem[3])); }
app.Use(async (c, next) =>
{
    if (!IPAddress.TryParse(c.Request.Host.Host, out _) && c.Request.Host.Host != "localhost") { c.Response.StatusCode = 400; await c.Response.WriteAsJsonAsync(Error("invalid_host", "Invalid request host.")); return; }
    c.Response.Headers.CacheControl = "no-store";
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    try { await next(c); }
    catch (OperationCanceledException) when (c.RequestAborted.IsCancellationRequested) { }
    catch (UnsafeStorageException) { if (!c.Response.HasStarted) { c.Response.StatusCode = 400; await c.Response.WriteAsJsonAsync(Error("unsafe_folder", "The destination folder contains a link or redirected directory. Choose a local folder without links.")); } }
    catch (InsufficientDiskSpaceException) { if (!c.Response.HasStarted) { c.Response.StatusCode = 507; await c.Response.WriteAsJsonAsync(Error("insufficient_disk_space", "There is not enough free disk space for this transfer.")); } }
    catch (IOException) { if (!c.Response.HasStarted) { c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(Error("storage_failure", "Could not save the file. Check disk space and folder access.")); } }
    catch (UnauthorizedAccessException) { if (!c.Response.HasStarted) { c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(Error("folder_access", "Cannot write to the destination folder.")); } }
});
// Link the request lifetime to the device lease, including native Safari downloads.
app.Use(async (c, next) =>
{
    var device = GetDevice(c);
    if (device is null && c.Request.Path.StartsWithSegments("/download"))
        sessions.TryGetValue(c.Request.Cookies["landrop-download"] ?? "", out device);
    if (device is null) { await next(c); return; }
    var original = c.RequestAborted;
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(original, device.Cancellation.Token);
    c.RequestAborted = linked.Token;
    try { await next(c); }
    catch (OperationCanceledException) when (linked.IsCancellationRequested) { c.Abort(); }
    finally { c.RequestAborted = original; }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/admin", (HttpContext c) =>
{
    if (!IsAdmin(c)) return Results.Unauthorized();
    foreach (var item in sessions.Where(s => s.Value.Expires <= DateTimeOffset.UtcNow)) if (sessions.TryRemove(item.Key, out var expired)) expired.Cancellation.Cancel();
    lock (gate) return Results.Ok(new { inbox, addresses = Addresses(), listeningAddress, version = typeof(Device).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0], port, pairingExpires, devices = sessions.Values.Select(d => new { d.Id, d.Name, d.Approved, d.Expires }), files = files.Values.Select(f => new { f.Id, f.Name, f.Size, f.Direction, f.RecipientId }) });
});
app.MapPost("/api/admin/shutdown", (HttpContext c) => { if (!IsAdmin(c)) return Results.Unauthorized(); c.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; }); return Results.Ok(); });
app.MapPost("/api/admin/pairing", (HttpContext c) => { if (!IsAdmin(c)) return Results.Unauthorized(); lock (gate) {
    pairing = Secret(); pairingExpires = DateTimeOffset.UtcNow.AddMinutes(10);
    foreach (var pending in sessions.Where(s => !s.Value.Approved).ToArray())
        if (sessions.TryRemove(pending.Key, out var removed)) removed.Cancellation.Cancel();
} return Results.Ok(); });
app.MapGet("/api/admin/qr", (HttpContext c, string address) =>
{
    if (!IsAdmin(c)) return Results.Unauthorized();
    if (address != listeningAddress) return Results.BadRequest(Error("invalid_address", "The QR address must match the selected listening address."));
    string link; lock (gate) link = $"http://{address}:{port}/#pair={pairing}";
    using var data = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.M); using var qr = new SvgQRCode(data); return Results.Ok(new { link, svg = qr.GetGraphic(5) });
});
app.MapPost("/api/pair", (PairRequest request) =>
{
    lock (gate)
    {
        if (pairingExpires <= DateTimeOffset.UtcNow || request.Code != pairing) return Results.BadRequest(Error("pairing_expired", "The pairing link has expired. Create a new QR code on the PC."));
        if (sessions.Count >= 32) return Results.Json(Error("device_limit", "Too many devices. Remove old requests on the PC."), statusCode: 429);
        var token = Secret(); var name = string.IsNullOrWhiteSpace(request.Name) ? "Phone" : request.Name.Trim();
        var lease = new CancellationTokenSource(TimeSpan.FromHours(8));
        sessions[token] = new Device(Secret()[..12], name[..Math.Min(name.Length, 60)], false, DateTimeOffset.UtcNow.AddHours(8), lease); return Results.Ok(new { token });
    }
});
app.MapPost("/api/admin/devices/{id}/{action}", (HttpContext c, string id, string action) =>
{
    if (!IsAdmin(c)) return Results.Unauthorized();
    lock (gate)
    {
        var item = sessions.FirstOrDefault(s => s.Value.Id == id); if (item.Key is null) return Results.NotFound();
        if (action == "approve") { if (item.Value.Approved) return Results.Ok(); if (item.Value.Expires <= DateTimeOffset.UtcNow) return Results.BadRequest(Error("session_expired", "The device session has expired.")); sessions.TryUpdate(item.Key, item.Value with { Approved = true }, item.Value); pairing = Secret(); pairingExpires = DateTimeOffset.UtcNow;
            foreach (var pending in sessions.Where(s => !s.Value.Approved).ToArray())
                if (sessions.TryRemove(pending.Key, out var removed)) removed.Cancellation.Cancel();
        }
        else if (action == "revoke") { if (sessions.TryRemove(item.Key, out var revoked)) revoked.Cancellation.Cancel(); }
        else return Results.BadRequest(Error("invalid_action", "Unknown device action."));
    }
    return Results.Ok();
});
app.MapPost("/api/admin/folder", (HttpContext c, FolderRequest request) =>
{
    if (!IsAdmin(c)) return Results.Unauthorized();
    try
    {
        if (!Path.IsPathFullyQualified(request.Path) || request.Path.StartsWith(@"\\")) return Results.BadRequest(Error("invalid_folder", "Choose an absolute local folder path."));
        var path = StorageSafety.CreateFolder(request.Path);
        lock (gate) { File.WriteAllText(settingsPath + ".tmp", JsonSerializer.Serialize(new FolderRequest(path))); File.Move(settingsPath + ".tmp", settingsPath, true); inbox = path; }
        return Results.Ok();
    }
    catch (UnsafeStorageException) { return Results.BadRequest(Error("unsafe_folder", "Choose a local folder without links or redirected directories.")); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Results.BadRequest(Error("invalid_folder", "Cannot use this folder. Check its path and permissions.")); }
});
app.MapGet("/api/state", (HttpContext c) => { var device = GetDevice(c); if (device is null && !IsAdmin(c)) return Results.Unauthorized(); return Results.Ok(new { approved = IsAdmin(c) || device!.Approved, files = (Allowed(c) ? files.Values.Where(f => f.Direction == "outbound" && (IsAdmin(c) || f.RecipientId == device!.Id)) : []).Select(f => new { f.Id, f.Name, f.Size }) }); });
app.MapPut("/api/files", async (HttpContext c, string name, string? recipient) =>
{
    if (!Allowed(c)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' ') || Reserved(name)) return Results.BadRequest(Error("invalid_filename", "Unsupported file name."));
    if (c.Request.ContentLength is null or < 0) return Results.BadRequest(Error("length_required", "A valid content length is required."));
    if (c.Request.ContentLength > maxSize) return Results.Json(Error("file_too_large", "The file size limit is exceeded."), statusCode: 413);
    var local = IsAdmin(c); var owner = local ? sessions.Values.FirstOrDefault(d => d.Id == recipient && d.Approved && d.Expires > DateTimeOffset.UtcNow) : GetDevice(c);
    if (owner is null) return Results.BadRequest(Error("recipient_required", "Choose an approved recipient."));
    if (!await slots.WaitAsync(0, c.RequestAborted)) return Results.Json(Error("upload_limit", "Too many active uploads. Try again shortly."), statusCode: 429);
    var length = c.Request.ContentLength.Value;
    lock (gate) { if (reservedFiles >= maxRegisteredFiles) { slots.Release(); return Results.Json(Error("file_limit", "The registered file limit is reached. Remove shared files or restart sharing."), statusCode: 429); } if (length > maxStoredBytes - reservedBytes) { slots.Release(); return Results.Json(Error("storage_quota", "The storage quota is exceeded."), statusCode: 413); } reservedBytes += length; reservedFiles++; }
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, owner.Cancellation.Token);
    var committed = false; string directory; lock (gate) directory = local ? outbox : inbox;
    string? volume = null; long diskRemaining = 0;
    var id = Secret()[..16]; var temp = Path.Combine(directory, $".landrop-{id}.part");
    try
    {
        StorageSafety.ValidateFolder(directory);
        volume = StorageSafety.Volume(directory);
        lock (gate)
        {
            var other = diskReservations.GetValueOrDefault(volume);
            var available = StorageSafety.AvailableBytes(volume);
            if (other > available || diskSafetyBuffer > available - other || length > available - other - diskSafetyBuffer)
                throw new InsufficientDiskSpaceException();
            diskReservations[volume] = other + length;
            diskRemaining = length;
        }
        StorageSafety.ValidateFolder(directory);
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536]; long written = 0, nextCheck = 16L * 1024 * 1024;
            while (true)
            {
                var read = await c.Request.Body.ReadAsync(buffer, cancellation.Token);
                if (read == 0) break;
                if (read > length - written) return Results.BadRequest(Error("incomplete_transfer", "The transfer length does not match its declared size."));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                written += read;
                lock (gate) { diskReservations[volume] -= read; diskRemaining -= read; }
                if (written >= nextCheck)
                {
                    lock (gate)
                    {
                        var available = StorageSafety.AvailableBytes(volume);
                        var remaining = diskReservations[volume];
                        if (remaining > available || diskSafetyBuffer > available - remaining) throw new InsufficientDiskSpaceException();
                    }
                    nextCheck = written + 16L * 1024 * 1024;
                }
            }
            await output.FlushAsync(cancellation.Token);
        }
        cancellation.Token.ThrowIfCancellationRequested();
        if (!Allowed(c) || owner.Expires <= DateTimeOffset.UtcNow) return Results.Unauthorized();
        var size = new FileInfo(temp).Length; if (size != length) return Results.BadRequest(Error("incomplete_transfer", "The transfer was not completed."));
        StorageSafety.ValidateFolder(directory);
        var final = Path.Combine(directory, name);
        try { File.Move(temp, final); } catch (IOException) when (File.Exists(final)) { final = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)}-{id}{Path.GetExtension(name)}"); File.Move(temp, final); }
        var file = new SharedFile(id, Path.GetFileName(final), final, size, local ? "outbound" : "inbound", owner.Id); files[id] = file; committed = true; return Results.Ok(new { file.Id, file.Name, file.Size });
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return Results.Json(Error("transfer_cancelled", "The transfer was cancelled."), statusCode: 409); }
    finally { try { StorageSafety.ValidateFolder(directory); if (File.Exists(temp)) File.Delete(temp); } finally { lock (gate) { if (volume is not null && diskRemaining != 0) diskReservations[volume] = diskReservations.GetValueOrDefault(volume) - diskRemaining; } if (!committed) { lock (gate) { reservedBytes -= length; reservedFiles--; } } slots.Release(); } }
});
app.MapGet("/api/files/{id}", (HttpContext c, string id) => { if (!Allowed(c)) return Results.Unauthorized(); if (!files.TryGetValue(id, out var f) || f.Direction != "outbound" || (!IsAdmin(c) && f.RecipientId != GetDevice(c)?.Id) || !File.Exists(f.Path)) return Results.NotFound(); return Results.File(f.Path, "application/octet-stream", f.Name, enableRangeProcessing: true); });
app.MapDelete("/api/admin/files/{id}", (HttpContext c, string id) => { if (!IsAdmin(c)) return Results.Unauthorized(); lock (gate) { if (files.TryGetValue(id, out var f)) { if (f.Direction == "outbound") File.Delete(f.Path); if (files.TryRemove(id, out _)) { if (f.Direction == "outbound") reservedBytes -= f.Size; reservedFiles--; } } } return Results.Ok(); });
app.MapPost("/api/download-session", (HttpContext c) => { if (GetDevice(c)?.Approved != true) return Results.Unauthorized(); c.Response.Cookies.Append("landrop-download", c.Request.Headers["X-LanDrop-Session"].ToString(), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(8), Path = "/download" }); return Results.Ok(); });
app.MapGet("/download/{id}", (HttpContext c, string id) => { if (!sessions.TryGetValue(c.Request.Cookies["landrop-download"] ?? "", out var d) || !d.Approved || d.Expires <= DateTimeOffset.UtcNow) return Results.Unauthorized(); if (!files.TryGetValue(id, out var f) || f.Direction != "outbound" || f.RecipientId != d.Id || !File.Exists(f.Path)) return Results.NotFound(); return Results.File(f.Path, "application/octet-stream", f.Name, enableRangeProcessing: true); });
app.Lifetime.ApplicationStarted.Register(() => { var url = $"http://127.0.0.1:{port}/#admin={admin}"; Console.WriteLine($"LAN Drop administration: {url}"); if (builder.Configuration.GetValue("OpenBrowser", true)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); });
app.Lifetime.ApplicationStopped.Register(() => { outboxLease.Dispose(); try { StorageSafety.CleanupRun(outbox); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeStorageException or JsonException) { } });
app.Run();
record PairRequest(string Code, string? Name);
record FolderRequest(string Path);
record Device(string Id, string Name, bool Approved, DateTimeOffset Expires, CancellationTokenSource Cancellation);
record SharedFile(string Id, string Name, string Path, long Size, string Direction, string RecipientId);
