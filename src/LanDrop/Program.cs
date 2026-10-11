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
var offers = new Dictionary<string, ReceiveOffer>(); // All access is protected by gate.
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
bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 180 && name is not "." and not ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\') && !name.EndsWith('.') && !name.EndsWith(' ') && !Reserved(name);
void PruneOffers()
{
    var now = DateTimeOffset.UtcNow;
    foreach (var entry in offers.ToArray())
        if (entry.Value.Status is "pending" or "approved" && (entry.Value.Expires <= now || !sessions.Values.Any(d => d.Id == entry.Value.DeviceId && d.Approved && d.Expires > now && !d.Cancellation.IsCancellationRequested)))
            offers[entry.Key] = entry.Value with { Status = "expired" };
    var terminal = offers.Values.Where(o => o.Status is not "pending" and not "approved").OrderByDescending(o => o.Expires).Skip(128).ToArray();
    foreach (var old in terminal) offers.Remove(old.Id);
}
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
    lock (gate)
    {
        PruneOffers();
        return Results.Ok(new { inbox, addresses = Addresses(), listeningAddress, version = typeof(Device).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0], port, pairingExpires, devices = sessions.Values.Select(d => new { d.Id, d.Name, d.Approved, d.Expires }).ToArray(), files = files.Values.Select(f => new { f.Id, f.Name, f.Size, f.Direction, f.RecipientId, f.Sha256, verified = true }).ToArray(), offers = offers.Values.Where(o => o.Status == "pending").Select(o => new { o.Id, o.Name, o.Size, o.DeviceId, o.DeviceName, o.Status, o.Expires }).ToArray() });
    }
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
app.MapGet("/api/state", (HttpContext c) => { var device = GetDevice(c); if (device is null && !IsAdmin(c)) return Results.Unauthorized(); return Results.Ok(new { approved = IsAdmin(c) || device!.Approved, files = (Allowed(c) ? files.Values.Where(f => f.Direction == "outbound" && (IsAdmin(c) || f.RecipientId == device!.Id)) : []).Select(f => new { f.Id, f.Name, f.Size, f.Sha256, verified = true }) }); });
app.MapPost("/api/offers", (HttpContext c, OfferRequest request) =>
{
    var device = GetDevice(c);
    if (device?.Approved != true) return Results.Unauthorized();
    if (!ValidName(request.Name)) return Results.BadRequest(Error("invalid_filename", "Unsupported file name."));
    if (request.Size < 0) return Results.BadRequest(Error("length_required", "A valid content length is required."));
    if (request.Size > maxSize) return Results.Json(Error("file_too_large", "The file size limit is exceeded."), statusCode: 413);
    lock (gate)
    {
        PruneOffers();
        if (GetDevice(c)?.Approved != true || device.Cancellation.IsCancellationRequested) return Results.Unauthorized();
        if (offers.Values.Count(o => o.Status is "pending" or "approved") >= 32) return Results.Json(Error("offer_limit", "Too many files are waiting for approval. Try again later."), statusCode: 429);
        var offer = new ReceiveOffer(Secret()[..16], request.Name, request.Size, device.Id, device.Name, "pending", DateTimeOffset.UtcNow.AddMinutes(10));
        offers.Add(offer.Id, offer);
        return Results.Ok(new { offer.Id, offer.Status, offer.Expires });
    }
});
app.MapGet("/api/offers/{id}", (HttpContext c, string id) =>
{
    var device = GetDevice(c); if (device?.Approved != true) return Results.Unauthorized();
    lock (gate)
    {
        PruneOffers();
        if (!offers.TryGetValue(id, out var offer) || offer.DeviceId != device.Id) return Results.NotFound();
        return Results.Ok(new { offer.Id, offer.Name, offer.Size, offer.Status, offer.Expires });
    }
});
app.MapDelete("/api/offers/{id}", (HttpContext c, string id) =>
{
    var device = GetDevice(c); if (device?.Approved != true) return Results.Unauthorized();
    lock (gate)
    {
        PruneOffers();
        if (!offers.TryGetValue(id, out var offer) || offer.DeviceId != device.Id) return Results.NotFound();
        if (offer.Status is "pending" or "approved") offers[id] = offer with { Status = "rejected" };
        PruneOffers();
        return Results.Ok();
    }
});
app.MapPost("/api/admin/offers/{id}/{action}", (HttpContext c, string id, string action) =>
{
    if (!IsAdmin(c)) return Results.Unauthorized();
    if (action is not "approve" and not "reject") return Results.BadRequest(Error("invalid_action", "Unknown offer action."));
    lock (gate)
    {
        PruneOffers();
        if (!offers.TryGetValue(id, out var offer)) return Results.NotFound();
        if (offer.Status == "expired") return Results.Json(Error("offer_expired", "The file offer has expired. Ask the phone to send a new request."), statusCode: 409);
        if (offer.Status is not "pending" and not "approved") return Results.Json(Error("offer_not_approved", "This file offer is no longer available."), statusCode: 409);
        offers[id] = offer with { Status = action == "approve" ? "approved" : "rejected" };
        PruneOffers();
        return Results.Ok();
    }
});
app.MapPut("/api/files", async (HttpContext c, string name, string? recipient, string? offer) =>
{
    if (!Allowed(c)) return Results.Unauthorized();
    if (!ValidName(name)) return Results.BadRequest(Error("invalid_filename", "Unsupported file name."));
    if (c.Request.ContentLength is null or < 0) return Results.BadRequest(Error("length_required", "A valid content length is required."));
    if (c.Request.ContentLength > maxSize) return Results.Json(Error("file_too_large", "The file size limit is exceeded."), statusCode: 413);
    var checksum = c.Request.Headers["X-LanDrop-SHA256"].ToString();
    if (checksum.Length != 64 || !checksum.All(char.IsAsciiHexDigit)) return Results.BadRequest(Error("checksum_required", "A valid SHA-256 checksum is required before sending this file."));
    var expectedHash = Convert.FromHexString(checksum);
    var local = IsAdmin(c); var owner = local ? sessions.Values.FirstOrDefault(d => d.Id == recipient && d.Approved && d.Expires > DateTimeOffset.UtcNow) : GetDevice(c);
    if (owner is null) return Results.BadRequest(Error("recipient_required", "Choose an approved recipient."));
    if (!local)
    {
        lock (gate)
        {
            PruneOffers();
            if (GetDevice(c)?.Approved != true || owner.Cancellation.IsCancellationRequested) return Results.Unauthorized();
            if (string.IsNullOrEmpty(offer)) return Results.BadRequest(Error("offer_required", "Request approval on the PC before sending this file."));
            if (!offers.TryGetValue(offer, out var requested) || requested.DeviceId != owner.Id) return Results.Json(Error("offer_not_approved", "This file has not been approved for this device."), statusCode: 409);
            if (requested.Status == "expired") return Results.Json(Error("offer_expired", "The file offer has expired. Send a new request."), statusCode: 409);
            if (requested.Status != "approved") return Results.Json(Error("offer_not_approved", "This file has not been approved or its approval was already used."), statusCode: 409);
            if (requested.Name != name || requested.Size != c.Request.ContentLength.Value) return Results.BadRequest(Error("offer_mismatch", "The file name or size does not match the approved offer."));
            offers[offer] = requested with { Status = "consumed" };
            PruneOffers();
        }
    }
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
        using var incomingHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536]; long written = 0, nextCheck = 16L * 1024 * 1024;
            while (true)
            {
                var read = await c.Request.Body.ReadAsync(buffer, cancellation.Token);
                if (read == 0) break;
                if (read > length - written) return Results.BadRequest(Error("incomplete_transfer", "The transfer length does not match its declared size."));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                incomingHash.AppendData(buffer, 0, read);
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
        if (!CryptographicOperations.FixedTimeEquals(incomingHash.GetHashAndReset(), expectedHash)) return Results.Json(Error("checksum_mismatch", "The file checksum does not match. The file was not saved."), statusCode: 422);
        // Read back the closed temporary file before making it available. This checks
        // the stored bytes, but does not guarantee persistence after power loss.
        StorageSafety.ValidateFolder(directory);
        await using (var saved = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(saved, cancellation.Token), expectedHash)) return Results.Json(Error("checksum_mismatch", "The saved file checksum does not match. The file was not saved."), statusCode: 422);
        cancellation.Token.ThrowIfCancellationRequested();
        if (!Allowed(c) || owner.Expires <= DateTimeOffset.UtcNow) return Results.Unauthorized();
        StorageSafety.ValidateFolder(directory);
        var final = Path.Combine(directory, name);
        try { File.Move(temp, final); } catch (IOException) when (File.Exists(final)) { final = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)}-{id}{Path.GetExtension(name)}"); File.Move(temp, final); }
        var file = new SharedFile(id, Path.GetFileName(final), final, size, local ? "outbound" : "inbound", owner.Id, checksum.ToLowerInvariant()); files[id] = file; committed = true; return Results.Ok(new { file.Id, file.Name, file.Size, file.Sha256, verified = true });
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
record SharedFile(string Id, string Name, string Path, long Size, string Direction, string RecipientId, string Sha256);
record OfferRequest([property: System.Text.Json.Serialization.JsonRequired] string Name, [property: System.Text.Json.Serialization.JsonRequired] long Size);
record ReceiveOffer(string Id, string Name, long Size, string DeviceId, string DeviceName, string Status, DateTimeOffset Expires);
