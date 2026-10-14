using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace JoyIptv;

internal sealed record SavedSource(string Name, int Type, string Address, string Username, string Password,
    string EpgUrl, string Started = "", string Expires = "");
internal sealed record TvProgramme(DateTimeOffset Start, DateTimeOffset End, string Title);

internal sealed partial class PlayerForm
{
    private readonly ComboBox kinds = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 34 };
    private readonly ComboBox savedSourceBox = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox epgAddress = new() { Width = 260, PlaceholderText = "XMLTV EPG URL (M3U için)" };
    private readonly TrackBar seek = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None };
    private readonly Panel playbackBar = new() { Dock = DockStyle.Bottom, Height = 43,
        BackColor = Color.FromArgb(27, 37, 51) };
    private readonly Label clock = new() { Dock = DockStyle.Right, Width = 125, TextAlign = ContentAlignment.MiddleCenter, Text = "--:-- / --:--" };
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 1000 };
    private readonly Dictionary<string, List<TvProgramme>> programmes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> resumePositions = new(StringComparer.Ordinal);
    private readonly List<SavedSource> savedSources = new();
    private List<Channel>? catalogBeforeEpisodes;
    private string currentStarted = "", currentExpires = "";
    private readonly string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JOY_IPTV_Player");
    private bool draggingSeek;
    private bool offlineCache;
    private long pendingResume;
    private string playingKey = "";
    private string playingSourceKey = "";
    private DateTime lastResumeSave = DateTime.MinValue;

    private void SetupExtras(FlowLayoutPanel toolbar, Panel sidebar)
    {
        kinds.Items.AddRange(new object[] { "Canlı TV", "Film", "Dizi", "Bölüm", "Önerilen", "Tümü" });
        kinds.SelectedIndex = 0;
        kinds.SelectedIndexChanged += (_, _) => SwitchLibraryKind();
        sidebar.Controls.Add(kinds);
        kinds.BringToFront();

        toolbar.Controls.Add(savedSourceBox);
        toolbar.Controls.Add(Button("Kaynağı kaydet", (_, _) => SaveSource()));
        toolbar.Controls.Add(Button("Kaynağı aç", (_, _) => OpenSource()));
        toolbar.Controls.Add(Button("Kaynağı sil", (_, _) => RemoveSource()));
        toolbar.Controls.Add(Button("Dizilere dön", (_, _) => ReturnToCatalog()));
        toolbar.Controls.Add(Button("Kaynak süresi", (_, _) => ShowSourceDates()));
        toolbar.Controls.Add(epgAddress);
        toolbar.Controls.Add(Button("EPG yükle", async (_, _) => await LoadXmltvAsync(false)));
        toolbar.Controls.Add(Button("Bağlantı tanıla", async (_, _) => await DiagnoseAsync()));

        playbackBar.Controls.Add(seek); playbackBar.Controls.Add(clock);
        Controls.Add(playbackBar);
        seek.MouseDown += (_, _) => draggingSeek = true;
        seek.MouseUp += (_, _) =>
        {
            if (player.IsSeekable && player.Length > 0)
                player.Time = (long)(player.Length * (seek.Value / 1000.0));
            draggingSeek = false;
        };
        playbackTimer.Tick += (_, _) => { UpdatePlayback(); MonitorLivePlayback(); };
        playbackTimer.Start();
        LoadSavedSources(); LoadResumePositions(); LoadContinueItems(); LoadWatchHistory(); LoadRecentItems();
    }

    private void OnChannelsLoaded()
    {
        catalogBeforeEpisodes = null;
        programmes.Clear();
        kinds.SelectedItem = "Canlı TV";
        if (activeSource == 2 && String.IsNullOrWhiteSpace(epgAddress.Text))
        {
            try
            {
                string root = RequireHttp(address.Text);
                epgAddress.Text = root + "/xmltv.php?username=" + Uri.EscapeDataString(username.Text.Trim()) +
                    "&password=" + Uri.EscapeDataString(password.Text);
            }
            catch { /* EPG is optional. */ }
        }
        if (!offlineCache && (activeSource is 0 or 1 or 2))
            _ = LoadXmltvAsync(true);
    }

    private static string PromptName(IWin32Window owner, string initial)
    {
        using var dialog = new Form { Text = "Kaynak adı", Width = 360, Height = 145, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var box = new TextBox { Text = initial, Left = 15, Top = 12, Width = 310 };
        var ok = new Button { Text = "Kaydet", Left = 230, Top = 50, Width = 95, DialogResult = DialogResult.OK };
        dialog.Controls.Add(box); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
        return dialog.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : "";
    }

    private string SourcesFile => Path.Combine(settingsDir, "kaynaklar.dat");
    private string LastSourceFile => Path.Combine(settingsDir, "son_kaynak.txt");
    private string ResumeFile => Path.Combine(settingsDir, "izleme.json");

    private void LoadSavedSources()
    {
        try
        {
            if (!File.Exists(SourcesFile)) { RefreshSources(); return; }
            byte[] encoded = File.ReadAllBytes(SourcesFile);
            byte[] plain = ProtectedData.Unprotect(encoded, null, DataProtectionScope.CurrentUser);
            savedSources.AddRange(JsonSerializer.Deserialize<List<SavedSource>>(plain) ?? new());
            RefreshSources();
        }
        catch (Exception e) { status.Text = "Kayıtlı kaynaklar açılamadı: " + e.Message; }
    }

    private void PersistSources()
    {
        Directory.CreateDirectory(settingsDir);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(savedSources);
        byte[] encoded = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        string temp = SourcesFile + ".tmp";
        File.WriteAllBytes(temp, encoded);
        File.Move(temp, SourcesFile, true);
    }

    private void RefreshSources()
    {
        int previous = savedSourceBox.SelectedIndex;
        savedSourceBox.Items.Clear();
        foreach (var item in savedSources)
            savedSourceBox.Items.Add(item.Name + (SourceRemaining(item.Expires ?? "") is string remaining ? " (" + remaining + ")" : ""));
        if (savedSources.Count == 0) savedSourceBox.Items.Add("Henüz kayıtlı kaynak yok");
        savedSourceBox.SelectedIndex = Math.Clamp(previous, 0, savedSourceBox.Items.Count - 1);
        RefreshSourceDialogProfiles();
    }

    private void SaveSource()
    {
        string name = PromptName(this, "Yeni kaynak");
        if (name.Length == 0 || String.IsNullOrWhiteSpace(address.Text)) return;
        try
        {
            NormalizeSourceInput();
            var entry = new SavedSource(name, source.SelectedIndex, address.Text.Trim(), username.Text.Trim(), password.Text,
                epgAddress.Text.Trim(), currentStarted, currentExpires);
            int index = savedSources.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && MessageBox.Show(this, "Bu adla kayıtlı kaynağı güncellemek istiyor musunuz?",
                "Kayıtlı kaynak", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (index >= 0) savedSources[index] = entry; else savedSources.Add(entry);
            PersistSources(); RefreshSources(); savedSourceBox.SelectedIndex = savedSources.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            RememberLastSource(name);
            status.Text = "Kaynak Windows kullanıcı hesabınızla şifrelenerek kaydedildi.";
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Kaynak kaydedilemedi"); }
    }

    private void OpenSource()
    {
        int i = savedSourceBox.SelectedIndex;
        if (i < 0 || i >= savedSources.Count) return;
        var s = savedSources[i]; source.SelectedIndex = s.Type;
        address.Text = s.Address; username.Text = s.Username; password.Text = s.Password; epgAddress.Text = s.EpgUrl;
        currentStarted = s.Started ?? ""; currentExpires = s.Expires ?? "";
        RememberLastSource(s.Name);
        status.Text = "Kaynak seçildi; Yükle'ye basın.";
    }

    private void RemoveSource()
    {
        int i = savedSourceBox.SelectedIndex;
        if (i < 0 || i >= savedSources.Count) return;
        if (MessageBox.Show(this, "Kayıtlı kaynağı silmek istiyor musunuz?", "Kaynağı sil",
            MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            string removed = savedSources[i].Name;
            savedSources.RemoveAt(i); PersistSources(); RefreshSources();
            if (File.Exists(LastSourceFile) && File.ReadAllText(LastSourceFile) == removed)
                File.Delete(LastSourceFile);
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Kaynak silinemedi"); }
    }

    private void RememberLastSource(string name)
    {
        try { Directory.CreateDirectory(settingsDir); File.WriteAllText(LastSourceFile, name); }
        catch { }
    }

    private async Task RestoreLastSourceAsync()
    {
        try
        {
            if (!File.Exists(LastSourceFile) || savedSources.Count == 0) return;
            string name = File.ReadAllText(LastSourceFile);
            int index = savedSources.FindIndex(s => s.Name == name);
            if (index < 0) return;
            savedSourceBox.SelectedIndex = index;
            OpenSource();
            await LoadAsync();
        }
        catch (Exception) { status.Text = "Son kaynak otomatik yüklenemedi. Kaynaklar menüsünden tekrar açabilirsiniz."; }
    }

    private void ReturnToCatalog()
    {
        if (catalogBeforeEpisodes == null) { status.Text = "Önce bir dizi açın."; return; }
        channels.Clear(); channels.AddRange(catalogBeforeEpisodes);
        catalogBeforeEpisodes = null;
        kinds.SelectedItem = "Dizi"; RefreshGroups();
        status.Text = "Dizi kataloğuna dönüldü.";
    }

    private void LoadResumePositions()
    {
        try
        {
            if (!File.Exists(ResumeFile)) return;
            foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(ResumeFile)) ?? new())
                resumePositions[entry.Key] = entry.Value;
        }
        catch { status.Text = "İzleme konumları okunamadı."; }
    }

    private static bool IsResumeContent(Channel item) => item.Kind is "Film" or "Bölüm" ||
        (item.Kind == "Dizi" && String.IsNullOrEmpty(item.Command));

    private void PrepareResume(Channel item)
    {
        playingKey = FavoriteKey(item); playingSourceKey = CacheKey(); pendingResume = 0;
        if (IsResumeContent(item)) resumePositions.TryGetValue(playingKey, out pendingResume);
        if (pendingResume > 30000) status.Text = "Kaldığınız yer hazırlanıyor...";
    }

    private void SavePlaybackPosition()
    {
        if (playingChannel == null || !IsResumeContent(playingChannel) || playingKey.Length == 0) return;
        long elapsed = player.Time, length = player.Length;
        if (elapsed < 30000 && pendingResume > 0) return;
        if (length > 0 && elapsed > length - 30000)
        { resumePositions.Remove(playingKey); ForgetContinue(playingKey); }
        else if (elapsed >= 30000)
        { resumePositions[playingKey] = elapsed; RememberContinue(playingKey, playingChannel); }
        SaveResumePositions();
    }

    private void SaveResumePositions()
    {
        try
        {
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(ResumeFile, JsonSerializer.Serialize(resumePositions));
        }
        catch { /* Playback continues even if progress cannot be saved. */ }
    }

    private void UpdatePlayback()
    {
        UpdateWatchSession();
        if (pendingResume > 30000 && player.IsSeekable && player.Length > pendingResume + 30000)
        { player.Time = pendingResume; pendingResume = 0; }
        long elapsed = Math.Max(0, player.Time), length = Math.Max(0, player.Length);
        clock.Text = length > 0 ? TimeSpan.FromMilliseconds(elapsed).ToString(@"hh\:mm\:ss") + " / " +
            TimeSpan.FromMilliseconds(length).ToString(@"hh\:mm\:ss") : "CANLI";
        seek.Enabled = player.IsSeekable && length > 0;
        if (!draggingSeek) seek.Value = length > 0 ? (int)Math.Clamp(elapsed * 1000.0 / length, 0, 1000) : 0;
        if ((playingChannel != null && IsResumeContent(playingChannel)) && (DateTime.UtcNow - lastResumeSave).TotalSeconds >= 10)
        { SavePlaybackPosition(); lastResumeSave = DateTime.UtcNow; }
    }

    private bool HandlePlaybackKey(Keys key)
    {
        if (cinemaGallery?.ContainsFocus == true) return false;
        if (ActiveControl is TextBox or ComboBox or TrackBar || playingChannel == null) return false;
        if (key == Keys.Space)
        { if (player.IsPlaying) player.Pause(); else player.Play(); return true; }
        if (key is Keys.Left or Keys.Right)
        {
            int delta = key == Keys.Right ? 1 : -1;
            if (playingChannel.Kind == "Canlı TV") _ = StepAsync(delta);
            else if (player.IsSeekable) player.Time = Math.Clamp(player.Time + delta * 10000, 0, player.Length);
            return true;
        }
        return false;
    }

    private static string ExplainError(Exception ex)
    {
        if (ex is HttpRequestException request) return request.StatusCode == HttpStatusCode.TooManyRequests
            ? "Portal istek sınırına ulaştı (HTTP 429). Bir süre bekleyip tekrar deneyin."
            : "Sunucuya erişilemedi veya sunucu hata döndürdü." +
              (request.StatusCode is { } code ? " HTTP: " + (int)code : "");
        if (ex is TaskCanceledException) return "Sunucudan yanıt zaman aşımına uğradı.";
        if (ex is JsonException) return "Sağlayıcı beklenen API biçiminde yanıt vermedi.";
        return ex.Message;
    }

    private async Task DiagnoseAsync()
    {
        try
        {
            Uri uri = new(RequireHttp(currentStreamUrl.Length > 0 ? currentStreamUrl : address.Text));
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            int port = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
            await tcp.ConnectAsync(uri.Host, port, timeout.Token);
            string httpResult = await ProbePlaybackAsync(uri);
            MessageBox.Show(this, $"Sunucu: {uri.Host}:{port}\nTCP bağlantısı: başarılı\n" +
                $"HTTP: {httpResult}\nOynatıcı: {player.State}\n" +
                (activeSource == 3 ? $"Son portal adımı: {stalkerLastAction}; HTTP: {stalkerLastStatus}\n" : "") +
                "TCP ve HTTP sonuçları tek başına yayın yetkisini doğrulamaz.", "Bağlantı tanılama");
        }
        catch (Exception e) { MessageBox.Show(this, ExplainError(e), "Bağlantı tanılama"); }
    }

    private async Task DiagnoseSourceNetworkAsync()
    {
        try
        {
            // Diagnose the source, not a previously played stream. Never show its query or credentials.
            Uri uri = new(RequireHttp(address.Text));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.Host, timeout.Token);
            if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
            int port = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
            using var tcp = new TcpClient();
            string tcpResult;
            try
            {
                await tcp.ConnectAsync(uri.Host, port, timeout.Token);
                tcpResult = "Bağlandı";
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException)
            {
                tcpResult = "Bağlanamadı: " + e.GetType().Name;
            }
            string httpResult = tcp.Connected ? await ProbePlaybackAsync(uri) : "TCP bağlantısı kurulamadı";
            MessageBox.Show(this, $"Kaynak: {uri.Host}:{port}\nDNS: çözümlendi ({addresses.Length} adres)\n" +
                $"TCP: {tcpResult}\nHTTP: {httpResult}\n" +
                "VPN açıksa Windows bağlantısı üzerinden kullanılır. DNS değişimi sağlayıcı erişim izni vermez.",
                "Kaynak ağ tanısı");
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Kaynak ağ tanısı: " + (e is SocketException ? "Alan adı DNS ile çözümlenemedi." : ExplainError(e)),
                "Kaynak ağ tanısı");
        }
    }

    private void OpenWindowsVpnSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:network-vpn") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows VPN ayarları"); }
    }
}


