using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using QRCoder;
using Forms = System.Windows.Forms;

namespace LanDrop.Desktop;
public partial class MainWindow : Window
{
    private readonly string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDrop");
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromHours(4) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Forms.NotifyIcon tray = new() { Icon = System.Drawing.SystemIcons.Application, Text = "LAN Drop" };
    private readonly Dictionary<string, string> english;
    private Dictionary<string, string> translations;
    private DesktopSettings settings = new();
    private Process? server;
    private string? baseUrl, adminKey, pairLink;
    private string inbox = "";
    private bool polling, closing, uploading, stopping;
    private Task? stopTask;
    private int generation;
    private CancellationTokenSource? transfer;
    private ServerState? lastState;
    private string settingsPath => Path.Combine(dataDirectory, "desktop.json");
    private string T(string key) => translations.GetValueOrDefault(key, english.GetValueOrDefault(key, key));
    public MainWindow()
    {
        InitializeComponent();
        english = LoadLanguage("en"); translations = english;
        try { if (!App.SmokeTest && File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(settingsPath)) ?? new(); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        LanguagePicker.ItemsSource = new[] { new LanguageChoice("en", "English"), new LanguageChoice("cs", "Čeština"), new LanguageChoice("auto", "Automatic / Automaticky") };
        LanguagePicker.DisplayMemberPath = "Name"; LanguagePicker.SelectedValuePath = "Code"; LanguagePicker.SelectedValue = settings.Language;
        Network.ItemsSource = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
          .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
          .Select(a => new NetworkChoice(a.Address.ToString(), $"{n.Name} · {a.Address}"))).Append(new NetworkChoice("127.0.0.1", "Local test · 127.0.0.1")).ToArray();
        Network.SelectedValue = settings.Address; // User explicitly chooses the network; sharing stays off at startup.
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        timer.Tick += async (_, _) => await Poll();
        Closing += ClosingWindow;
        Loaded += (_, _) => { if (App.SmokeTest) { Application.Current.Shutdown(0); return; } Status.Text = T("Stopped"); };
        ApplyLanguage();
    }
    private Dictionary<string,string> LoadLanguage(string code)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"LanDrop.Desktop.Locales.{code}.json");
        if (stream is null) throw new InvalidOperationException("Missing embedded language resource: " + code);
        using (stream) return JsonSerializer.Deserialize<Dictionary<string,string>>(stream) ?? new();
    }
    private void SaveSettings()
    {
        Directory.CreateDirectory(dataDirectory);
        var temp = settingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings));
        File.Move(temp, settingsPath, true);
    }
    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (english is null || LanguagePicker.SelectedValue is not string code) return;
        settings = settings with { Language = code };
        var resolved = code == "auto" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "cs" ? "cs" : "en") : code;
        translations = LoadLanguage(resolved);
        try { if (!App.SmokeTest) SaveSettings(); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Status.Text = T("OperationFailed"); }
        ApplyLanguage();
    }
    private void ApplyLanguage()
    {
        Subtitle.Text = T("Subtitle"); SecurityNote.Text = T("SecurityNote"); NetworkLabel.Text = T("NetworkLabel");
        ConnectionGroup.Header = T("Connect"); FolderGroup.Header = T("Folder"); DevicesGroup.Header = T("Devices");
        SendGroup.Header = T("Send"); FilesGroup.Header = T("Files"); RecipientLabel.Text = T("Recipient");
        foreach (var pair in new[] { (StartButton,"Start"),(StopButton,"Stop"),(NewQrButton,"NewQr"),(CopyLinkButton,"CopyLink"),(ChooseFolderButton,"ChooseFolder"),(OpenFolderButton,"OpenFolder"),(SendButton,"ChooseFiles"),(CancelButton,"Cancel"),(HideButton,"Hide") }) pair.Item1.Content = T(pair.Item2);
        VersionText.Text = "LAN Drop " + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "development");
        if(server is null) Status.Text = T("Stopped");
        if(lastState is not null) RenderState(lastState);
    }
    private string FindServer()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Server", "LanDrop.exe");
        if(File.Exists(bundled)) return bundled;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "LanDrop", "bin", "Release", "net10.0", "LanDrop.exe");
            if(File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new IOException(T("ServerMissing"));
    }
    private async void StartSharing(object sender, RoutedEventArgs e)
    {
        if(server is not null || Network.SelectedValue is not string address) { Status.Text = T("SelectNetwork"); return; }
        StartButton.IsEnabled = false; Network.IsEnabled = false;
        try
        {
            var executable = FindServer();
            using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            var launch = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            launch.WorkingDirectory = Directory.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "wwwroot")) ? Path.GetDirectoryName(executable)! : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
            foreach(var argument in new[] { "--OpenBrowser=false", "--ListenAddress=" + address, "--Port=" + port, "--DataDirectory=" + dataDirectory }) launch.ArgumentList.Add(argument);
            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = new Process { StartInfo = launch, EnableRaisingEvents = true };
            server.OutputDataReceived += (_, args) => { var line = args.Data; if(line is null) return; var index = line.IndexOf("http://127.0.0.1:",StringComparison.Ordinal); if(index >= 0 && line.Contains("#admin=")) ready.TrySetResult(line[index..].Trim()); };
            server.ErrorDataReceived += (_, _) => { }; // Drain errors without logging secrets; show a generic startup error.
            server.Exited += (_, _) => ready.TrySetException(new IOException(T("ServerFailed")));
            server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
            var url = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            baseUrl = url[..url.IndexOf("/#admin=",StringComparison.Ordinal)]; adminKey = url.Split("#admin=")[1];
            settings = settings with { Address = address }; SaveSettings();
            StopButton.IsEnabled = ChooseFolderButton.IsEnabled = OpenFolderButton.IsEnabled = NewQrButton.IsEnabled = true;
            Status.Text = T("Running") + " · " + address;
            tray.Visible = true; timer.Start(); await Poll(); await RefreshQr();
        }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or TimeoutException or HttpRequestException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Status.Text = T("ServerFailed"); await StopServer(); }
    }
    private async Task<JsonElement> Api(string path, HttpMethod? method = null, object? body = null, CancellationToken token = default)
    {
        if(baseUrl is null || adminKey is null) throw new IOException(T("Stopped"));
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, baseUrl + path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        request.Headers.Add("X-LanDrop-Admin",adminKey);
        if(body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request,deadline.Token);
            var text = await response.Content.ReadAsStringAsync(deadline.Token);
            if(!response.IsSuccessStatusCode) throw new IOException(T("OperationFailed") + " (" + (int)response.StatusCode + ")");
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch(OperationCanceledException ex) when(!token.IsCancellationRequested) { throw new IOException(T("OperationFailed"),ex); }
    }
    private async Task Poll()
    {
        if(polling || server is null || stopping) return;
        polling = true;
        var currentGeneration = generation;
        try
        {
            var json = await Api("/api/admin");
            if(currentGeneration != generation || stopping || server is null) return;
            lastState = json.Deserialize<ServerState>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if(lastState is not null) { inbox = lastState.Inbox; RenderState(lastState); }
        }
        catch(Exception ex) when(ex is IOException or HttpRequestException or JsonException or OperationCanceledException) { if(currentGeneration == generation && !stopping && server is not null) Status.Text = T("ConnectionLost"); }
        finally { polling = false; }
    }
    private void RenderState(ServerState state)
    {
        FolderText.Text = state.Inbox;
        var selected = Recipient.SelectedValue as string;
        Recipient.ItemsSource = state.Devices.Where(d => d.Approved).ToArray();
        Recipient.SelectedValue = selected;
        if(Recipient.SelectedItem is null && Recipient.Items.Count == 1) Recipient.SelectedIndex = 0;
        SendButton.IsEnabled = !uploading && Recipient.Items.Count > 0 && server is not null;
        DevicesPanel.Children.Clear();
        if(state.Devices.Length == 0) DevicesPanel.Children.Add(new TextBlock { Text = T("NoDevices") });
        foreach(var device in state.Devices)
        {
            var panel = new DockPanel();
            var action = new Button { Content = T(device.Approved ? "Disconnect" : "Approve"), Tag = device }; DockPanel.SetDock(action,Dock.Right);
            action.Click += async (_,_) => { try { await Api($"/api/admin/devices/{device.Id}/{(device.Approved ? "revoke" : "approve")}",HttpMethod.Post); if(!device.Approved) await Api("/api/admin/pairing",HttpMethod.Post); await Poll(); await RefreshQr(); } catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; } };
            panel.Children.Add(action);
            if(!device.Approved) { var reject = new Button { Content = T("Reject") }; DockPanel.SetDock(reject,Dock.Right); reject.Click += async (_,_) => { try { await Api($"/api/admin/devices/{device.Id}/revoke",HttpMethod.Post); await Poll(); } catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; } }; panel.Children.Add(reject); }
            panel.Children.Add(new TextBlock { Text = device.Name + " · " + T(device.Approved ? "Approved" : "Pending"), VerticalAlignment = VerticalAlignment.Center }); DevicesPanel.Children.Add(panel);
        }
        FilesPanel.Children.Clear();
        if(state.Files.Length == 0) FilesPanel.Children.Add(new TextBlock { Text = T("NoFiles") });
        foreach(var f in state.Files)
        {
            var row = new DockPanel(); var remove = new Button { Content = T(f.Direction == "outbound" ? "Unshare" : "Dismiss") }; DockPanel.SetDock(remove,Dock.Right);
            remove.Click += async (_,_) => { try { await Api("/api/admin/files/"+f.Id,HttpMethod.Delete); await Poll(); } catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; } };
            row.Children.Add(remove); row.Children.Add(new TextBlock { Text = $"{f.Name} · {f.Size:N0} B · {T(f.Direction == "outbound" ? "Ready" : "Received")}",VerticalAlignment = VerticalAlignment.Center }); FilesPanel.Children.Add(row);
        }
    }
    private async Task RefreshQr()
    {
        if(Network.SelectedValue is not string address || address == "127.0.0.1") { LinkText.Text = T("LocalOnly"); QrImage.Source = null; return; }
        var qr = await Api("/api/admin/qr?address=" + Uri.EscapeDataString(address));
        pairLink = qr.GetProperty("link").GetString();
        using var data = QRCodeGenerator.GenerateQrCode(pairLink!,QRCodeGenerator.ECCLevel.M);
        using var image = new PngByteQRCode(data);
        using var stream = new MemoryStream(image.GetGraphic(8));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); QrImage.Source = bitmap;
        LinkText.Text = T("ScanQr"); CopyLinkButton.IsEnabled = true;
    }
    private async void NewQr(object sender,RoutedEventArgs e) { try { await Api("/api/admin/pairing",HttpMethod.Post); await RefreshQr(); } catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; } }
    private void CopyLink(object sender,RoutedEventArgs e) { if(pairLink is not null) Clipboard.SetText(pairLink); }
    private async void ChooseFolder(object sender,RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("ChooseFolder"), InitialDirectory = Directory.Exists(inbox) ? inbox : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if(dialog.ShowDialog(this) != true) return;
        try { await Api("/api/admin/folder",HttpMethod.Post,new { path = dialog.FolderName }); await Poll(); } catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; }
    }
    private void OpenFolder(object sender,RoutedEventArgs e) { if(Directory.Exists(inbox)) Process.Start(new ProcessStartInfo(inbox) { UseShellExecute = true }); }
    private async void SelectFiles(object sender,RoutedEventArgs e) { var dialog = new OpenFileDialog { Multiselect = true }; if(dialog.ShowDialog(this) == true) await SendFiles(dialog.FileNames); }
    private async void FilesDropped(object sender,DragEventArgs e) { if(e.Data.GetData(DataFormats.FileDrop) is string[] paths) await SendFiles(paths); }
    private async Task SendFiles(string[] paths)
    {
        if(uploading) return;
        if(Recipient.SelectedValue is not string recipient || server is null) { Status.Text = T("SelectRecipient"); return; }
        transfer = new CancellationTokenSource(); uploading = true; SendButton.IsEnabled = false; CancelButton.IsEnabled = true; Recipient.IsEnabled = false;
        try
        {
            foreach(var path in paths)
            {
                transfer.Token.ThrowIfCancellationRequested();
                var info = new FileInfo(path); if(!info.Exists || info.Length > 10L*1024*1024*1024) throw new IOException(T("InvalidFile"));
                using var request = new HttpRequestMessage(HttpMethod.Put,baseUrl+"/api/files?name="+Uri.EscapeDataString(info.Name)+"&recipient="+Uri.EscapeDataString(recipient));
                request.Headers.Add("X-LanDrop-Admin",adminKey);
                await using var file = File.OpenRead(path);
                using var progress = new ProgressReadStream(file, read => Dispatcher.InvokeAsync(() => { UploadProgress.Value = info.Length == 0 ? 100 : read*100d/info.Length; ProgressText.Text = info.Name + " · " + Math.Round(UploadProgress.Value) + " %"; }));
                request.Content = new StreamContent(progress); request.Content.Headers.ContentLength = info.Length;
                using var response = await http.SendAsync(request,transfer.Token);
                if(!response.IsSuccessStatusCode) throw new IOException(T("OperationFailed")+" ("+(int)response.StatusCode+")");
            }
            Status.Text = T("ReadyForPhone"); await Poll();
        }
        catch(OperationCanceledException) { Status.Text = T("Cancelled"); }
        catch(Exception ex) when(ex is IOException or HttpRequestException or UnauthorizedAccessException) { Status.Text = ex.Message; }
        finally { uploading = false; Recipient.IsEnabled = true; CancelButton.IsEnabled = false; SendButton.IsEnabled = server is not null && Recipient.Items.Count > 0; transfer.Dispose(); transfer = null; }
    }
    private void CancelTransfer(object sender,RoutedEventArgs e) => transfer?.Cancel();
    private void HideToTray(object sender,RoutedEventArgs e) { tray.Visible = true; Hide(); }
    private async void StopSharing(object sender,RoutedEventArgs e) => await StopServer();
    private Task StopServer() => stopTask is { IsCompleted: false } ? stopTask : stopTask = StopServerCore();
    private async Task StopServerCore()
    {
        stopping = true; generation++; StartButton.IsEnabled = StopButton.IsEnabled = false; timer.Stop(); transfer?.Cancel(); pairLink = null; QrImage.Source = null; LinkText.Text = "";
        var owned = server; server = null;
        if(owned is not null)
        {
            try { if(!owned.HasExited) { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await Api("/api/admin/shutdown",HttpMethod.Post,token:deadline.Token); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
            catch(Exception e) when(e is IOException or HttpRequestException or TimeoutException or InvalidOperationException or OperationCanceledException) { if(!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync(); }
            owned.Dispose();
        }
        baseUrl = adminKey = null; lastState = null; DevicesPanel.Children.Clear(); FilesPanel.Children.Clear(); Recipient.ItemsSource = null;
        StartButton.IsEnabled = Network.IsEnabled = true; StopButton.IsEnabled = ChooseFolderButton.IsEnabled = OpenFolderButton.IsEnabled = NewQrButton.IsEnabled = SendButton.IsEnabled = CopyLinkButton.IsEnabled = false;
        Status.Text = T("Stopped"); stopping = false;
    }
    private async void ClosingWindow(object? sender,CancelEventArgs e)
    {
        if(closing) return; e.Cancel = true; closing = true;
        await StopServer(); tray.Dispose(); http.Dispose(); Close();
    }
}
record LanguageChoice(string Code,string Name);
record NetworkChoice(string Address,string Label);
record DesktopSettings(string Language = "en",string? Address = null);
record DeviceInfo(string Id,string Name,bool Approved);
record FileInfoDto(string Id,string Name,long Size,string Direction);
record ServerState(string Inbox,DeviceInfo[] Devices,FileInfoDto[] Files);

sealed class ProgressReadStream(Stream inner,Action<long> progress) : Stream
{
    private long count;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer,int offset,int length) { var read = inner.Read(buffer,offset,length); count += read; progress(count); return read; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token = default) { var read = await inner.ReadAsync(buffer,token); count += read; progress(count); return read; }
    public override Task<int> ReadAsync(byte[] buffer,int offset,int length,CancellationToken token) => ReadAsync(buffer.AsMemory(offset,length),token).AsTask();
    public override void Flush() { }
    public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int length) => throw new NotSupportedException();
}
