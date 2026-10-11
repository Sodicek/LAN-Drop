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
using System.Security.Cryptography;
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
using LanDrop.Transport;

namespace LanDrop.Desktop;
public partial class MainWindow : Window
{
    private readonly string dataDirectory = App.TestDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanDrop");
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromHours(4) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer networkTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool checkingNetwork;
    private readonly Forms.NotifyIcon tray = new() { Icon = System.Drawing.SystemIcons.Application, Text = "LAN Drop" };
    private readonly Dictionary<string, string> english;
    private Dictionary<string, string> translations;
    private DesktopSettings settings = new();
    private Process? server;
    private string? baseUrl, adminKey, pairLink;
    private string inbox = "";
    private bool polling, closing, uploading, stopping;
    private bool configuringCertificate, secureSharing;
    private GeneratedCertificate? createdCertificate;
    private Task? stopTask;
    private int generation;
    private int? lastStopExitCode;
    private CancellationTokenSource? transfer;
    private ServerState? lastState;
    private readonly RetainedTransferQueue retryQueue = new();
    private string[] retryPaths => retryQueue.Paths;
    private string? retryRecipient => retryQueue.Recipient;
    private string settingsPath => Path.Combine(dataDirectory, "desktop.json");
    private string T(string key) => translations.GetValueOrDefault(key, english.GetValueOrDefault(key, key));
    public MainWindow()
    {
        InitializeComponent();
        Recipient.SelectionChanged += (_,_) => { if(!renderingState && !uploading && Recipient.SelectedValue is string) retryTargetExplicitlySelected = true; };
        english = LoadLanguage("en"); translations = english;
        try { if (!App.SmokeTest && File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(settingsPath)) ?? new(); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        CertificatePath.Text = settings.CertificatePath ?? "";
        SecureHttps.IsChecked = settings.SecureHttps;
        LanguagePicker.ItemsSource = new[] { new LanguageChoice("en", "English"), new LanguageChoice("cs", "Čeština"), new LanguageChoice("auto", "Automatic / Automaticky") };
        LanguagePicker.DisplayMemberPath = "Name"; LanguagePicker.SelectedValuePath = "Code"; LanguagePicker.SelectedValue = settings.Language;
        RefreshNetworks();
        Network.SelectedValue = settings.Address; // User explicitly chooses the network; sharing stays off at startup.
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        timer.Tick += async (_, _) => await Poll();
        networkTimer.Tick += async (_, _) => await CheckSharingNetwork();
        Closing += ClosingWindow;
        Loaded += async (_, _) => {
            if (App.LifecycleTest) { await RunLifecycleTest(); return; }
            if (App.SmokeTest) { Application.Current.Shutdown(0); return; }
            Status.Text = T("Stopped");
        };
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
        try { if (!App.SmokeTest && !App.LifecycleTest) SaveSettings(); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Status.Text = T("OperationFailed"); }
        ApplyLanguage();
    }
    private void ApplyLanguage()
    {
        RefreshNetworks();
        Subtitle.Text = T("Subtitle"); SecurityNote.Text = T("SecurityNote"); NetworkLabel.Text = T("NetworkLabel");
        ConnectionGroup.Header = T("Connect"); FolderGroup.Header = T("Folder"); DevicesGroup.Header = T("Devices");
        OffersGroup.Header = T("IncomingOffers"); OffersHelp.Text = T("OffersHelp");
        SendGroup.Header = T("Send"); FilesGroup.Header = T("Files"); RecipientLabel.Text = T("Recipient");
        TransfersGroup.Header = T("Transfers"); TransfersHelp.Text = T("TransfersHelp");
        RetryButton.Content = T("Retry"); ClearRetryButton.Content = T("ClearRetry"); TransferFolderButton.Content = T("OpenFolder"); UpdateRetry();
        SecureHttps.Content = T("SecureHttps"); CertificateLabel.Text = T("CertificateLabel"); CertificatePasswordLabel.Text = T("CertificatePasswordLabel");
        ChooseCertificateButton.Content = T("ChooseCertificate"); GenerateCertificateButton.Content = T("GenerateCertificate");
        CopyCertificateButton.Content = T("CopyCertificateDetails");
        if(createdCertificate is not null) CertificateInfo.Text = string.Format(CultureInfo.CurrentCulture,T("CertificateGenerated"),createdCertificate.AuthorityPath,createdCertificate.AuthorityFingerprint);
        CertificateHelpHeader.Header = T("CertificateHelpHeader"); CertificateHelp.Text = T("CertificateHelp"); UpdateTransportControls();
        foreach (var pair in new[] { (StartButton,"Start"),(StopButton,"Stop"),(NewQrButton,"NewQr"),(CopyLinkButton,"CopyLink"),(ChooseFolderButton,"ChooseFolder"),(OpenFolderButton,"OpenFolder"),(SendButton,"ChooseFiles"),(CancelButton,"Cancel"),(HideButton,"Hide"),(DiagnosticsButton,"Diagnostics") }) pair.Item1.Content = T(pair.Item2);
        VersionText.Text = "LAN Drop " + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "development");
        if(server is null) Status.Text = T("Stopped");
        if(lastState is not null) RenderState(lastState);
    }
    private void RefreshNetworks()
    {
        var selected = Network.SelectedValue as string ?? settings.Address;
        Network.ItemsSource = NetworkDiagnostics.Interfaces().Select(n => n with { Label = (n.Profile == NetworkProfile.Loopback ? T("AdapterLoopback") : n.Name) + " · " + n.Address + " · " + T(n.AdapterType) + " · " + T("Profile" + n.Profile) }).ToArray();
        Network.SelectedValue = selected;
    }
    private void UpdateTransportControls()
    {
        var editable = server is null && !stopping && !configuringCertificate && StartButton.IsEnabled;
        SecureHttps.IsEnabled = editable;
        CertificateControls.IsEnabled = editable && SecureHttps.IsChecked == true;
        var encrypted = server is null ? SecureHttps.IsChecked == true : secureSharing;
        TransportStatus.Text = T(server is null ? (encrypted ? "HttpsSelected" : "HttpSelected") : (encrypted ? "HttpsRunning" : "HttpRunning"));
        SecurityNote.Text = T(encrypted ? "HttpsSecurityNote" : "SecurityNote");
    }
    private void TransportChanged(object sender,RoutedEventArgs e)
    {
        if(english is null) return;
        settings = settings with { SecureHttps = SecureHttps.IsChecked == true };
        try { if(!App.SmokeTest && !App.LifecycleTest) SaveSettings(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Status.Text = T("OperationFailed"); }
        UpdateTransportControls();
    }
    private void ChooseCertificate(object sender,RoutedEventArgs e)
    {
        if(server is not null || configuringCertificate) return;
        var dialog = new OpenFileDialog { Title = T("ChooseCertificate"), Filter = "Certificate (*.pfx;*.p12)|*.pfx;*.p12", CheckFileExists = true };
        if(dialog.ShowDialog(this) != true) return;
        CertificatePath.Text = Path.GetFullPath(dialog.FileName); CertificateInfo.Text = ""; createdCertificate = null; CopyCertificateButton.IsEnabled = false;
        settings = settings with { CertificatePath = CertificatePath.Text };
        try { if(!App.SmokeTest && !App.LifecycleTest) SaveSettings(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Status.Text = T("OperationFailed"); }
    }
    private async void GenerateCertificate(object sender,RoutedEventArgs e)
    {
        if(server is not null || configuringCertificate) return;
        if(Network.SelectedValue is not string address) { Status.Text = T("SelectNetwork"); return; }
        if(CertificatePassword.Password.Length < 12) { Status.Text = T("CertificatePasswordMinimum"); return; }
        var dialog = new SaveFileDialog { Title = T("GenerateCertificate"), Filter = "Certificate (*.pfx)|*.pfx", DefaultExt = ".pfx", FileName = "LAN-Drop-" + address + ".pfx", OverwritePrompt = true };
        if(dialog.ShowDialog(this) != true) return;
        configuringCertificate = true; StartButton.IsEnabled = Network.IsEnabled = false; UpdateTransportControls();
        try
        {
            var password = CertificatePassword.Password;
            var created = await Task.Run(() => CertificateTools.Generate(dialog.FileName,address,password));
            createdCertificate = created; CopyCertificateButton.IsEnabled = true;
            CertificatePath.Text = created.CertificatePath;
            CertificateInfo.Text = string.Format(CultureInfo.CurrentCulture,T("CertificateGenerated"),created.AuthorityPath,created.AuthorityFingerprint);
            settings = settings with { CertificatePath = created.CertificatePath, SecureHttps = true };
            SecureHttps.IsChecked = true;
            if(!App.SmokeTest && !App.LifecycleTest) SaveSettings();
            Status.Text = T("CertificateReady");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or InvalidOperationException)
        { Status.Text = T("CertificateGenerateFailed"); }
        finally { configuringCertificate = false; StartButton.IsEnabled = Network.IsEnabled = true; UpdateTransportControls(); }
    }
    private void CopyCertificateDetails(object sender,RoutedEventArgs e)
    {
        if(createdCertificate is not null) Clipboard.SetText(string.Format(CultureInfo.CurrentCulture,T("CertificateGenerated"),createdCertificate.AuthorityPath,createdCertificate.AuthorityFingerprint));
    }
    private void ShowDiagnostics(object sender, RoutedEventArgs e)
    {
        RefreshNetworks();
        var selected = Network.SelectedItem as NetworkChoice;
        var detail = T("DiagnosticSelected") + ": " + (selected?.Label ?? T("SelectNetwork")) + "\n" +
            T("DiagnosticBound") + ": " + (server is null ? T("Stopped") : settings.Address ?? "—") + "\n" +
            T("DiagnosticServer") + ": " + (server is null || server.HasExited ? T("Stopped") : T("Running")) + "\n\n" + T("DiagnosticHelp");
        MessageBox.Show(this, detail, T("Diagnostics"), MessageBoxButton.OK, MessageBoxImage.Information);
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
    private async void StartSharing(object sender, RoutedEventArgs e) => await StartServer();
    private static (int Administration,int Phone) FindAvailablePorts(string address,bool encrypted)
    {
        for(var attempt = 0; attempt < 32; attempt++)
        {
            using var administration = new TcpListener(IPAddress.Loopback,0);
            administration.Start();
            var port = ((IPEndPoint)administration.LocalEndpoint).Port;
            // Check both actual interfaces while the admin candidate is reserved.
            // The child still owns the final bind and fails closed if another process wins the race.
            try
            {
                if(encrypted)
                {
                    using var phone = new TcpListener(IPAddress.Parse(address),0); phone.Start();
                    var phonePort = ((IPEndPoint)phone.LocalEndpoint).Port;
                    if(phonePort == port) continue;
                    return (port,phonePort);
                }
                else if(address != "127.0.0.1") { using var phone = new TcpListener(IPAddress.Parse(address),port); phone.Start(); }
                return (port,port);
            }
            catch(SocketException) { }
        }
        throw new IOException("Could not find available local sharing ports.");
    }
    private async Task StartServer()
    {
        if(server is not null || Network.SelectedValue is not string address) { Status.Text = T("SelectNetwork"); return; }
        RefreshNetworks();
        if(Network.SelectedItem is not NetworkChoice selected || !NetworkDiagnostics.CanShare(selected.Profile)) { Status.Text = T("NetworkBlocked"); return; }
        var encrypted = SecureHttps.IsChecked == true;
        if(encrypted)
        {
            try
            {
                using var certificate = CertificateTools.LoadServerCertificate(CertificatePath.Text,CertificatePassword.Password,address);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or InvalidOperationException)
            { Status.Text = T("CertificateInvalid"); return; }
        }
        StartButton.IsEnabled = false; Network.IsEnabled = false; UpdateTransportControls();
        try
        {
            var executable = FindServer();
            var ports = FindAvailablePorts(address,encrypted);
            var port = ports.Administration;
            var launch = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            launch.WorkingDirectory = Directory.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "wwwroot")) ? Path.GetDirectoryName(executable)! : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
            foreach(var argument in new[] { "--OpenBrowser=false", "--ListenAddress=" + address, "--Port=" + port, "--DataDirectory=" + dataDirectory, "--contentRoot=" + launch.WorkingDirectory }) launch.ArgumentList.Add(argument);
            foreach(var variable in new[] { "LAN_DROP_CERTIFICATE_PASSWORD", "HttpsCertificatePath", "HttpsPort" }) launch.Environment.Remove(variable);
            if(encrypted)
            {
                launch.ArgumentList.Add("--HttpsCertificatePath=" + CertificatePath.Text);
                launch.ArgumentList.Add("--HttpsPort=" + ports.Phone);
                launch.Environment["LAN_DROP_CERTIFICATE_PASSWORD"] = CertificatePassword.Password;
            }
            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = new Process { StartInfo = launch, EnableRaisingEvents = true };
            server.OutputDataReceived += (_, args) => { var line = args.Data; if(line is null) return; var index = line.IndexOf("http://127.0.0.1:",StringComparison.Ordinal); if(index >= 0 && line.Contains("#admin=")) ready.TrySetResult(line[index..].Trim()); };
            server.ErrorDataReceived += (_, _) => { }; // Drain errors without logging secrets; show a generic startup error.
            server.Exited += (_, _) => ready.TrySetException(new IOException(T("ServerFailed")));
            try { server.Start(); } finally { launch.Environment.Remove("LAN_DROP_CERTIFICATE_PASSWORD"); }
            secureSharing = encrypted; UpdateTransportControls(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
            var url = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            baseUrl = url[..url.IndexOf("/#admin=",StringComparison.Ordinal)]; adminKey = url.Split("#admin=")[1];
            settings = settings with { Address = address, SecureHttps = encrypted, CertificatePath = CertificatePath.Text }; SaveSettings();
            StopButton.IsEnabled = ChooseFolderButton.IsEnabled = OpenFolderButton.IsEnabled = NewQrButton.IsEnabled = true;
            Status.Text = T("Running") + " · " + address;
            tray.Visible = true; timer.Start(); networkTimer.Start(); await Poll(); await RefreshQr();
        }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or TimeoutException or HttpRequestException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { await StopServer(); Status.Text = T("ServerFailed"); }
    }
    private async Task RunLifecycleTest()
    {
        var result = 1;
        try
        {
            if(App.TestDataDirectory is null) throw new IOException("Explicit test directory required.");
            SecureHttps.IsChecked = false;
            Network.SelectedValue = "127.0.0.1";
            await StartServer();
            if(server is null || lastState is null) throw new IOException("Server failed to start or poll.");
            timer.Stop(); // Keep the isolated lifecycle proof deterministic; all polls below are explicit.
            var qr = await Api("/api/admin/qr?address=127.0.0.1");
            var code = qr.GetProperty("link").GetString()!.Split("#pair=")[1];
            string? session = null;
            async Task<JsonElement> PhoneApi(string path,HttpMethod method,object? body = null)
            {
                using var request = new HttpRequestMessage(method,baseUrl + path);
                if(session is not null) request.Headers.Add("X-LanDrop-Session",session);
                if(body is not null) request.Content = JsonContent.Create(body);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var response = await http.SendAsync(request,deadline.Token);
                response.EnsureSuccessStatusCode();
                return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(deadline.Token));
            }
            var paired = await PhoneApi("/api/pair",HttpMethod.Post,new { code, name = "Desktop lifecycle phone" });
            session = paired.GetProperty("token").GetString();
            await Poll();
            var device = lastState!.Devices.Single(d => d.Name == "Desktop lifecycle phone");
            await Api($"/api/admin/devices/{device.Id}/approve",HttpMethod.Post);
            await Poll();
            Recipient.SelectedValue = device.Id;
            var source = Path.Combine(dataDirectory,"lifecycle-outbound.txt");
            await File.WriteAllTextAsync(source,"Desktop integrity lifecycle proof.\n");
            var sourceBytes = await File.ReadAllBytesAsync(source);
            var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
            await SendFiles([source]); // Same prehash, upload, and receipt path as file picker/drop.
            await Poll();
            var prepared = lastState!.Files.Single(f => f.Name == Path.GetFileName(source));
            if(!prepared.Verified || prepared.Sha256 != sourceHash || prepared.Size != sourceBytes.Length || Status.Text != T("ReadyForPhone"))
                throw new IOException("Desktop upload integrity was not confirmed.");
            using(var download = new HttpRequestMessage(HttpMethod.Get,baseUrl + "/api/files/" + prepared.Id))
            {
                download.Headers.Add("X-LanDrop-Session",session);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var response = await http.SendAsync(download,deadline.Token);
                response.EnsureSuccessStatusCode();
                if(!(await response.Content.ReadAsByteArrayAsync(deadline.Token)).SequenceEqual(sourceBytes))
                    throw new IOException("Desktop prepared file did not download intact.");
            }
            foreach(var action in new[] { "approve", "reject" })
            {
                var name = "lifecycle-" + action + ".txt";
                var offer = await PhoneApi("/api/offers",HttpMethod.Post,new { name, size = 123 });
                var id = offer.GetProperty("id").GetString()!;
                await Poll();
                if(lastState?.Offers?.SingleOrDefault(o => o.Id == id) is not { } shown || shown.DeviceName != device.Name || shown.Name != name || shown.Size != 123)
                    throw new IOException("Offer state did not reach desktop.");
                var labels = OffersPanel.Children.OfType<DockPanel>().SelectMany(row => row.Children.OfType<TextBlock>()).Select(label => label.Text);
                if(!labels.Any(label => label.Contains(device.Name,StringComparison.Ordinal) && label.Contains(name,StringComparison.Ordinal) && label.Contains("123",StringComparison.Ordinal)))
                    throw new IOException("Offer did not render in desktop.");
                await DecideOffer(id,action); // Same action and refresh path as the visible buttons.
                var decided = await PhoneApi("/api/offers/" + id,HttpMethod.Get);
                if(decided.GetProperty("status").GetString() != (action == "approve" ? "approved" : "rejected") || lastState?.Offers?.Any(o => o.Id == id) == true)
                    throw new IOException("Desktop offer action did not complete.");
            }
            foreach(var action in new[] { "approve", "reject" })
            {
                var batch = await PhoneApi("/api/offer-batches",HttpMethod.Post,new { files = new[] { new { name = "batch-first.txt",size = 10 }, new { name = "batch-second.txt",size = 20 } } });
                var id = batch.GetProperty("id").GetString()!;
                await Poll();
                if(lastState?.Batches?.SingleOrDefault(b => b.Id == id) is not { } shown || shown.TotalSize != 30 || shown.Files.Length != 2)
                    throw new IOException("Batch did not reach desktop.");
                if(!OffersPanel.Children.OfType<StackPanel>().Any(p => p.Children.OfType<Expander>().Any()))
                    throw new IOException("Batch file detail did not render.");
                await DecideBatch(id,action);
                var decided = await PhoneApi("/api/offer-batches/" + id,HttpMethod.Get);
                if(decided.GetProperty("status").GetString() != (action == "approve" ? "approved" : "rejected") || lastState?.Batches?.Any(b => b.Id == id) == true)
                    throw new IOException("Desktop batch action did not complete.");
            }
            var firstRetry = Path.Combine(dataDirectory,"retry-first.txt");
            var remainingRetry = Path.Combine(dataDirectory,"retry-remaining.txt");
            await File.WriteAllTextAsync(firstRetry,"Confirmed file must not be sent twice.");
            await SendFiles([firstRetry,remainingRetry]); // Real second-file IO failure, retaining only its suffix.
            await Poll();
            if(!retryPaths.SequenceEqual([remainingRetry]) || lastState!.Files.Count(f => f.Name == "retry-first.txt") != 1)
                throw new IOException("Retry retained completed sources.");
            await File.WriteAllTextAsync(remainingRetry,"New contents are hashed on retry.");
            await RetryRetainedFiles();
            await Poll();
            if(retryPaths.Length != 0 || lastState!.Files.Count(f => f.Name == "retry-first.txt") != 1 || lastState.Files.Count(f => f.Name == "retry-remaining.txt") != 1)
                throw new IOException("Retry duplicated a confirmed source or lost the remaining source.");
            if(lastState.Transfers?.Any(t => t.Status == "completed") != true || TransfersPanel.Children.Count == 0)
                throw new IOException("Transfer overview did not reach desktop.");
            var folder = Path.Combine(dataDirectory, "Inbox");
            Directory.CreateDirectory(folder);
            await Api("/api/admin/folder", HttpMethod.Post, new { path = folder });
            await Poll();
            if(lastState?.Inbox != folder) throw new IOException("Folder roundtrip failed.");
            await StopServer();
            if(server is not null || lastStopExitCode != 0) throw new IOException("Server did not stop gracefully.");
            const string testPassword = "Isolated lifecycle test password 42!";
            var certificate = await Task.Run(() => CertificateTools.Generate(Path.Combine(dataDirectory,"lifecycle.pfx"),"127.0.0.1",testPassword));
            var wrongAddress = await Task.Run(() => CertificateTools.Generate(Path.Combine(dataDirectory,"wrong-address.pfx"),"127.0.0.2",testPassword));
            SecureHttps.IsChecked = true; CertificatePath.Text = certificate.CertificatePath; CertificatePassword.Password = "wrong password";
            await StartServer();
            if(server is not null || Status.Text != T("CertificateInvalid")) throw new IOException("Incorrect certificate password did not fail closed.");
            CertificatePath.Text = wrongAddress.CertificatePath; CertificatePassword.Password = testPassword;
            await StartServer();
            if(server is not null || Status.Text != T("CertificateInvalid")) throw new IOException("Incorrect certificate address did not fail closed.");
            CertificatePath.Text = certificate.CertificatePath;
            await StartServer(); timer.Stop();
            if(server is null || !secureSharing || baseUrl?.StartsWith("http://127.0.0.1:",StringComparison.Ordinal) != true || SecureHttps.IsEnabled || CertificateControls.IsEnabled)
                throw new IOException("Desktop secure startup or control isolation failed.");
            var secureQr = await Api("/api/admin/qr?address=127.0.0.1");
            if(secureQr.GetProperty("link").GetString()?.StartsWith("https://127.0.0.1:",StringComparison.Ordinal) != true)
                throw new IOException("Desktop secure pairing link was not HTTPS.");
            if(File.ReadAllText(settingsPath).Contains(testPassword,StringComparison.Ordinal) || server.StartInfo.ArgumentList.Any(a => a.Contains(testPassword,StringComparison.Ordinal)) || server.StartInfo.Environment.ContainsKey("LAN_DROP_CERTIFICATE_PASSWORD"))
                throw new IOException("Certificate secret persisted outside the child environment.");
            if(JsonSerializer.Deserialize<DesktopSettings>("{\"Language\":\"en\"}")?.SecureHttps != true)
                throw new IOException("Legacy desktop settings did not default to HTTPS.");
            await StopServer();
            if(server is not null || lastStopExitCode != 0) throw new IOException("Secure server did not stop gracefully.");
            File.WriteAllText(Path.Combine(dataDirectory, "lifecycle-test.json"), JsonSerializer.Serialize(new { Started = true, Stopped = true, OffersApproved = true, OffersRejected = true, BatchesApproved = true, BatchesRejected = true, RetryWithoutDuplicates = true, TransfersRendered = true, OutboundVerified = true, SecureStarted = true, SecureQr = true, InvalidCertificateBlocked = true, SecretNotPersisted = true, SecureDefaultMigrated = true, ServerExitCode = lastStopExitCode }));
            result = 0;
        }
        catch(Exception ex)
        {
            // The lifecycle hook uses only isolated fixture settings and credentials.
            // Keep diagnostics out of normal application runs and redact its fixture password.
            if(App.TestDataDirectory is not null)
                File.WriteAllText(Path.Combine(dataDirectory,"lifecycle-error.json"),JsonSerializer.Serialize(new { Error = ex.ToString().Replace("Isolated lifecycle test password 42!","[fixture password]",StringComparison.Ordinal), Status = Status.Text }));
            await StopServer();
        }
        finally { closing = true; tray.Dispose(); http.Dispose(); Application.Current.Shutdown(result); }
    }
    private string ResponseError(string text,int status)
    {
        try
        {
            using var body = JsonDocument.Parse(text);
            if(body.RootElement.TryGetProperty("code",out var code) && code.ValueKind == JsonValueKind.String)
            {
                var key = "Error_" + code.GetString();
                if(english.ContainsKey(key)) return T(key);
            }
        }
        catch(JsonException) { }
        return T("OperationFailed") + " (" + status + ")";
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
            if(!response.IsSuccessStatusCode) throw new IOException(ResponseError(text,(int)response.StatusCode));
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch(OperationCanceledException ex) when(!token.IsCancellationRequested) { throw new IOException(T("OperationFailed"),ex); }
    }
    private async Task CheckSharingNetwork()
    {
        var owned = server;
        var address = settings.Address;
        var currentGeneration = generation;
        if(checkingNetwork || stopping || owned is null || address == "127.0.0.1") return;
        checkingNetwork = true;
        try
        {
            NetworkChoice[] interfaces;
            try { interfaces = await Task.Run(NetworkDiagnostics.Interfaces); }
            catch(Exception) { interfaces = []; }
            if(currentGeneration != generation || !ReferenceEquals(server, owned) || stopping) return;
            var choice = interfaces.FirstOrDefault(n => n.Address == address);
            if(NetworkDiagnostics.CanContinueSharing(choice?.Profile)) return;
            var reason = T(choice is null ? "NetworkUnavailableStopped" : "NetworkUnsafeStopped");
            await StopServer();
            if(server is null) Status.Text = reason;
        }
        finally { checkingNetwork = false; }
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
        renderingState = true;
        var selected = Recipient.SelectedValue as string;
        Recipient.ItemsSource = state.Devices.Where(d => d.Approved).ToArray();
        Recipient.SelectedValue = selected;
        if(Recipient.SelectedItem is null && Recipient.Items.Count == 1 && retryPaths.Length == 0) Recipient.SelectedIndex = 0;
        renderingState = false;
        SendButton.IsEnabled = !uploading && retryPaths.Length == 0 && Recipient.Items.Count > 0 && server is not null;
        UpdateRetry(); TransferFolderButton.IsEnabled = server is not null && Directory.Exists(inbox);
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
        OffersPanel.Children.Clear();
        var offers = state.Offers ?? [];
        var batches = state.Batches ?? [];
        if(offers.Length == 0 && batches.Length == 0) OffersPanel.Children.Add(new TextBlock { Text = T("NoOffers") });
        foreach(var batch in batches)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"{batch.DeviceName} · {batch.Files.Length} {T("BatchFiles")} · {batch.TotalSize:N0} B" });
            var details = new StackPanel();
            foreach(var file in batch.Files) details.Children.Add(new TextBlock { Text = $"{file.Name} · {file.Size:N0} B" });
            panel.Children.Add(new Expander { Header = T("BatchDetails"), Content = details, Margin = new Thickness(0,0,0,8) });
            var actions = new WrapPanel(); var accept = new Button { Content = T("AcceptAll") }; var reject = new Button { Content = T("RejectAll") };
            async Task Decide(string action)
            {
                accept.IsEnabled = reject.IsEnabled = false;
                try { await DecideBatch(batch.Id,action); }
                catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; accept.IsEnabled = reject.IsEnabled = server is not null; }
            }
            accept.Click += async (_,_) => await Decide("approve"); reject.Click += async (_,_) => await Decide("reject");
            actions.Children.Add(accept); actions.Children.Add(reject); panel.Children.Add(actions); OffersPanel.Children.Add(panel);
        }
        foreach(var offer in offers)
        {
            var row = new DockPanel();
            var accept = new Button { Content = T("AcceptFile") };
            var reject = new Button { Content = T("Reject") };
            DockPanel.SetDock(accept,Dock.Right); DockPanel.SetDock(reject,Dock.Right);
            async Task Decide(string action)
            {
                accept.IsEnabled = reject.IsEnabled = false;
                try { await DecideOffer(offer.Id,action); }
                catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; accept.IsEnabled = reject.IsEnabled = server is not null; }
            }
            accept.Click += async (_,_) => await Decide("approve");
            reject.Click += async (_,_) => await Decide("reject");
            row.Children.Add(reject); row.Children.Add(accept);
            row.Children.Add(new TextBlock { Text = $"{offer.DeviceName} · {offer.Name} · {offer.Size:N0} B", VerticalAlignment = VerticalAlignment.Center });
            OffersPanel.Children.Add(row);
        }
        FilesPanel.Children.Clear();
        if(state.Files.Length == 0) FilesPanel.Children.Add(new TextBlock { Text = T("NoFiles") });
        foreach(var f in state.Files)
        {
            var row = new DockPanel(); var remove = new Button { Content = T(f.Direction == "outbound" ? "Unshare" : "Dismiss") }; DockPanel.SetDock(remove,Dock.Right);
            remove.Click += async (_,_) => { try { await Api("/api/admin/files/"+f.Id,HttpMethod.Delete); await Poll(); } catch(Exception ex) when(ex is IOException or HttpRequestException) { Status.Text = ex.Message; } };
            var verified = f.Verified && UploadReceipt.IsSha256(f.Sha256);
            var label = f.Direction == "outbound" ? (verified ? "ReadyVerified" : "Ready") : (verified ? "ReceivedVerified" : "Received");
            row.Children.Add(remove); row.Children.Add(new TextBlock { Text = $"{f.Name} · {f.Size:N0} B · {T(label)}",VerticalAlignment = VerticalAlignment.Center }); FilesPanel.Children.Add(row);
        }
        TransfersPanel.Children.Clear();
        var transfers = state.Transfers ?? [];
        if(transfers.Length == 0) TransfersPanel.Children.Add(new TextBlock { Text = T("NoTransfers") });
        foreach(var item in transfers.OrderByDescending(t => t.Updated))
        {
            var panel = new StackPanel { Margin = new Thickness(0,0,0,12) };
            var error = string.IsNullOrEmpty(item.ErrorCode) ? "" : " · " + (english.ContainsKey("Error_" + item.ErrorCode) ? T("Error_" + item.ErrorCode) : T("OperationFailed"));
            panel.Children.Add(new TextBlock { Text = $"{item.Name} · {item.DeviceName} · {T(item.Direction == "inbound" ? "TransferInbound" : "TransferOutbound")} · {T("Transfer_" + item.Status)}{error}" });
            var bytes = Math.Clamp(item.BytesTransferred,0,Math.Max(0,item.Size));
            panel.Children.Add(new TextBlock { Text = $"{bytes:N0} / {item.Size:N0} B · {item.Updated.LocalDateTime:t}", Margin = new Thickness(0,0,0,4) });
            if(item.Status is "transferring" or "verifying") panel.Children.Add(new ProgressBar { Maximum = 100, Value = item.Size == 0 ? 100 : bytes * 100d / item.Size, Height = 8 });
            TransfersPanel.Children.Add(panel);
        }
    }
    private async Task DecideOffer(string id,string action)
    {
        await Api($"/api/admin/offers/{Uri.EscapeDataString(id)}/{action}",HttpMethod.Post);
        await Poll();
    }
    private async Task DecideBatch(string id,string action)
    {
        await Api($"/api/admin/offer-batches/{Uri.EscapeDataString(id)}/{action}",HttpMethod.Post);
        await Poll();
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
    private void UpdateRetry()
    {
        RetryButton.IsEnabled = retryPaths.Length > 0 && server is not null && !uploading;
        ClearRetryButton.IsEnabled = retryPaths.Length > 0 && !uploading;
        RetryHelp.Text = retryPaths.Length == 0 ? "" : string.Format(CultureInfo.CurrentCulture,T("RetryHelp"),retryPaths.Length);
    }
    private async void RetryFiles(object sender,RoutedEventArgs e) => await RetryRetainedFiles();
    private void ClearRetry(object sender,RoutedEventArgs e)
    {
        if(uploading) return;
        retryQueue.Clear(); retryTargetExplicitlySelected = false; UpdateRetry();
        SendButton.IsEnabled = server is not null && Recipient.Items.Count > 0;
        Status.Text = T("RetryCleared");
    }
    private async Task RetryRetainedFiles()
    {
        if(retryPaths.Length == 0 || uploading) return;
        if(Recipient.SelectedValue is not string selected || lastState?.Devices.Any(d => d.Id == selected && d.Approved) != true)
        { Status.Text = T("SelectRecipient"); return; }
        // A lost/revoked target must be explicitly replaced by the user in the recipient picker.
        if(selected != retryRecipient && !retryTargetExplicitlySelected) { Status.Text = T("RetrySelectRecipient"); return; }
        await SendFiles(retryPaths.ToArray());
    }
    private bool retryTargetExplicitlySelected;
    private bool renderingState;
    private async Task SendFiles(string[] paths)
    {
        if(uploading) return;
        if(retryPaths.Length > 0 && !retryPaths.SequenceEqual(paths)) { Status.Text = T("RetryPending"); return; }
        if(Recipient.SelectedValue is not string recipient || server is null) { Status.Text = T("SelectRecipient"); return; }
        retryQueue.Begin(paths,recipient); retryTargetExplicitlySelected = false;
        transfer = new CancellationTokenSource(); uploading = true; SendButton.IsEnabled = false; CancelButton.IsEnabled = true; Recipient.IsEnabled = false;
        UpdateRetry();
        try
        {
            foreach(var path in paths)
            {
                transfer.Token.ThrowIfCancellationRequested();
                var info = new FileInfo(path); if(!info.Exists || info.Length > 10L*1024*1024*1024) throw new IOException(T("InvalidFile"));
                using var request = new HttpRequestMessage(HttpMethod.Put,baseUrl+"/api/files?name="+Uri.EscapeDataString(info.Name)+"&recipient="+Uri.EscapeDataString(recipient));
                request.Headers.Add("X-LanDrop-Admin",adminKey);
                await using var file = File.OpenRead(path);
                var length = file.Length;
                if(length > 10L*1024*1024*1024) throw new IOException(T("InvalidFile"));
                Status.Text = T("PreparingFile"); UploadProgress.Value = 0;
                ProgressText.Text = info.Name + " · " + T("CheckingFile");
                byte[] digest;
                using(var checking = new ProgressReadStream(file, read => Dispatcher.InvokeAsync(() => {
                    UploadProgress.Value = length == 0 ? 100 : read*100d/length;
                    ProgressText.Text = info.Name + " · " + T("CheckingFile") + " · " + Math.Round(UploadProgress.Value) + " %";
                }))) digest = await SHA256.HashDataAsync(checking, transfer.Token);
                var expectedSha256 = Convert.ToHexString(digest).ToLowerInvariant();
                file.Position = 0;
                request.Headers.Add("X-LanDrop-SHA256",expectedSha256);
                UploadProgress.Value = 0; Status.Text = T("UploadingFile");
                var clock = Stopwatch.StartNew();
                using var progress = new ProgressReadStream(file, read => {
                    var seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.001);
                    var speed = read / seconds;
                    var remaining = speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, length-read) / speed) : TimeSpan.Zero;
                    Dispatcher.InvokeAsync(() => {
                        UploadProgress.Value = length == 0 ? 100 : read*100d/length;
                        ProgressText.Text = info.Name + " · " + Math.Round(UploadProgress.Value) + " % · " + (speed/1048576).ToString("N1") + " MB/s · " + T("Eta") + " " + remaining.ToString(@"hh\:mm\:ss");
                    });
                });
                request.Content = new StreamContent(progress); request.Content.Headers.ContentLength = length;
                using var response = await http.SendAsync(request,transfer.Token);
                if(!response.IsSuccessStatusCode) throw new IOException(ResponseError(await response.Content.ReadAsStringAsync(transfer.Token),(int)response.StatusCode));
                try
                {
                    using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(transfer.Token));
                    if(!UploadReceipt.Validate(receipt.RootElement,length,expectedSha256)) throw new IOException(T("IntegrityFailed"));
                }
                catch(JsonException ex) { throw new IOException(T("IntegrityFailed"),ex); }
                UploadProgress.Value = 100; ProgressText.Text = info.Name + " · " + T("ReadyVerified");
                retryQueue.ConfirmCurrent();
            }
            Status.Text = T("ReadyForPhone"); await Poll();
        }
        catch(OperationCanceledException) { Status.Text = T("Cancelled"); }
        catch(Exception ex) when(ex is IOException or HttpRequestException or UnauthorizedAccessException) { Status.Text = ex.Message; }
        finally { uploading = false; Recipient.IsEnabled = true; CancelButton.IsEnabled = false; SendButton.IsEnabled = retryPaths.Length == 0 && server is not null && Recipient.Items.Count > 0; transfer.Dispose(); transfer = null; UpdateRetry(); }
    }
    private void CancelTransfer(object sender,RoutedEventArgs e) => transfer?.Cancel();
    private void HideToTray(object sender,RoutedEventArgs e) { tray.Visible = true; Hide(); }
    private async void StopSharing(object sender,RoutedEventArgs e) => await StopServer();
    private Task StopServer() => stopTask is { IsCompleted: false } ? stopTask : stopTask = StopServerCore();
    private async Task StopServerCore()
    {
        stopping = true; generation++; StartButton.IsEnabled = StopButton.IsEnabled = false; timer.Stop(); networkTimer.Stop(); transfer?.Cancel(); pairLink = null; QrImage.Source = null; LinkText.Text = "";
        var owned = server; server = null;
        if(owned is not null)
        {
            try { if(!owned.HasExited) { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await Api("/api/admin/shutdown",HttpMethod.Post,token:deadline.Token); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
            catch(Exception e) when(e is IOException or HttpRequestException or TimeoutException or InvalidOperationException or OperationCanceledException) { if(!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync(); }
            lastStopExitCode = owned.ExitCode;
            owned.Dispose();
        }
        baseUrl = adminKey = null; lastState = null; DevicesPanel.Children.Clear(); OffersPanel.Children.Clear(); FilesPanel.Children.Clear(); TransfersPanel.Children.Clear(); Recipient.ItemsSource = null;
        TransferFolderButton.IsEnabled = false; UpdateRetry();
        StartButton.IsEnabled = Network.IsEnabled = true; StopButton.IsEnabled = ChooseFolderButton.IsEnabled = OpenFolderButton.IsEnabled = NewQrButton.IsEnabled = SendButton.IsEnabled = CopyLinkButton.IsEnabled = false;
        Status.Text = T("Stopped"); stopping = false;
        secureSharing = false; UpdateTransportControls();
    }
    private async void ClosingWindow(object? sender,CancelEventArgs e)
    {
        if(closing) return; e.Cancel = true; closing = true;
        await StopServer(); tray.Dispose(); http.Dispose(); Close();
    }
}
record LanguageChoice(string Code,string Name);

record DesktopSettings(string Language = "en",string? Address = null,bool SecureHttps = true,string? CertificatePath = null);
record DeviceInfo(string Id,string Name,bool Approved);
record FileInfoDto(string Id,string Name,long Size,string Direction,string? Sha256 = null,bool Verified = false);
record OfferInfo(string Id,string Name,long Size,string DeviceId,string DeviceName,string Status,DateTimeOffset Expires);
record BatchFileInfo(string Id,string Name,long Size);
record BatchInfo(string Id,string DeviceId,string DeviceName,long TotalSize,DateTimeOffset Expires,BatchFileInfo[] Files);
record TransferInfo(string Id,string Name,long Size,string DeviceId,string DeviceName,string Direction,string Status,long BytesTransferred,DateTimeOffset Updated,string? ErrorCode = null,string? FileId = null);
record ServerState(string Inbox,DeviceInfo[] Devices,FileInfoDto[] Files,OfferInfo[]? Offers = null,BatchInfo[]? Batches = null,TransferInfo[]? Transfers = null);

sealed class ProgressReadStream(Stream inner,Action<long> progress) : Stream
{
    private long count;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long lastReport = -250;
    private void Report() { var now = clock.ElapsedMilliseconds; if(now-lastReport < 250) return; lastReport = now; progress(count); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer,int offset,int length) { var read = inner.Read(buffer,offset,length); count += read; Report(); return read; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token = default) { var read = await inner.ReadAsync(buffer,token); count += read; Report(); return read; }
    public override Task<int> ReadAsync(byte[] buffer,int offset,int length,CancellationToken token) => ReadAsync(buffer.AsMemory(offset,length),token).AsTask();
    public override void Flush() { }
    public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int length) => throw new NotSupportedException();
}
