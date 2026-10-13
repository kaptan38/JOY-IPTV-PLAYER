using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace JoyIptv;

internal sealed record Channel(string Name, string Group, string Url, string Kind = "Canlı TV", string? Command = null,
    int BouquetOrder = 0, int ChannelOrder = 0, string EpgId = "", string PosterUrl = "", string Description = "");

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Core.Initialize(NativeRuntime.EnsureReady());
            if (args.Contains("--verify-native", StringComparer.Ordinal))
            {
                using var probe = new LibVLC("--no-video-title-show");
                return;
            }
            ApplicationConfiguration.Initialize();
            if (args.Length == 2 && args[0] == "--verify-ui")
            {
                using var preview = new PlayerForm();
                preview.VerifyVisualLayout(args[1]);
                return;
            }
            Application.Run(new PlayerForm());
        }
        catch (Exception ex)
        {
            if (args.Contains("--verify-native", StringComparer.Ordinal) || args.Contains("--verify-ui", StringComparer.Ordinal))
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
                return;
            }
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "JOY_IPTV_acilis_hatasi.txt");
            try { File.WriteAllText(log, ex.ToString()); }
            catch { log = "Masaüstüne hata kaydı yazılamadı"; }
            MessageBox.Show(ex.ToString() + "\n\nKayıt: " + log, "JOY IPTV açılış hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed partial class PlayerForm : Form
{
    private sealed class FullscreenKeys : IMessageFilter
    {
        private readonly PlayerForm form;
        public FullscreenKeys(PlayerForm owner) { form = owner; }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != 0x100) return false;
            Keys key = (Keys)message.WParam.ToInt32();
            if (form.HandlePlaybackKey(key)) return true;
            if (key != Keys.F11 && !(key == Keys.Escape && form.fullscreen)) return false;
            form.ToggleFullscreen(); return true;
        }
    }

    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox address = new() { Width = 330, PlaceholderText = "Dosya yolu veya URL" };
    private readonly TextBox username = new() { Width = 145, PlaceholderText = "Kullanıcı / MAC" };
    private readonly TextBox password = new() { Width = 145, PlaceholderText = "Parola", UseSystemPasswordChar = true };
    private readonly TextBox filter = new() { PlaceholderText = "Kanal, film veya dizi ara...", Dock = DockStyle.Top };
    private readonly ComboBox groups = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox picture = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly CheckBox favoritesOnly = new() { Dock = DockStyle.Top, Text = "Yalnızca favoriler", Height = 28, ForeColor = Color.White };
    private readonly ListBox list = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 31, 42), ForeColor = Color.White,
        BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 29, ForeColor = Color.Gainsboro, Text = "Kaynak ekleyerek başlayın." };
    private readonly VideoView video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly LibVLC libvlc = new("--no-video-title-show", "--network-caching=900", "--avcodec-hw=any");
    private readonly MediaPlayer player;
    private readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromSeconds(25) };
    private readonly List<Channel> channels = new();
    private bool loadingSource;
    private int stalkerCatalogGeneration;
    private readonly HashSet<string> favorites = new(StringComparer.Ordinal);
    private readonly string favoritesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JOY_IPTV_Player", "favoriler.json");
    private List<Channel> visible = new();
    private Channel? playingChannel;
    private string stalkerBase = "", stalkerMac = "", stalkerToken = "";
    private readonly SemaphoreSlim stalkerRequestGate = new(1, 1);
    private DateTime stalkerNextRequestUtc;
    private string stalkerLastAction = "Henüz istek yapılmadı";
    private int stalkerLastStatus;
    private int activeSource = -1;
    private bool fullscreen;
    private FormWindowState previousWindowState;
    private readonly FlowLayoutPanel top;
    private readonly Panel left;
    private readonly FullscreenKeys fullscreenKeys;

    public PlayerForm()
    {
        Text = "JOY IPTV Player"; Width = 1300; Height = 780; MinimumSize = new Size(900, 550);
        KeyPreview = true;
        fullscreenKeys = new FullscreenKeys(this);
        Application.AddMessageFilter(fullscreenKeys);
        BackColor = Color.FromArgb(16, 22, 32); ForeColor = Color.White; Font = new Font("Segoe UI", 10);
        source.Items.AddRange(new object[] { "M3U dosya", "M3U URL", "Xtream", "Stalker" }); source.SelectedIndex = 0;
        source.SelectedIndexChanged += (_, _) => UpdateFields(); UpdateFields();
        LoadFavorites();
        player = new MediaPlayer(libvlc); video.MediaPlayer = player;
        top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(10), AutoScroll = true,
            BackColor = Color.FromArgb(27, 37, 51) };
        top.Controls.Add(source); top.Controls.Add(address); top.Controls.Add(username); top.Controls.Add(password);
        top.Controls.Add(Button("Yükle", async (_, _) => await LoadAsync()));
        top.Controls.Add(Button("Dosya seç", (_, _) => PickFile()));
        top.Controls.Add(Button("Duraklat / sürdür", (_, _) => { if (player.IsPlaying) player.Pause(); else player.Play(); }));
        top.Controls.Add(Button("Durdur", (_, _) => StopPlayback()));
        top.Controls.Add(Button("Önceki", async (_, _) => await StepAsync(-1)));
        top.Controls.Add(Button("Sonraki", async (_, _) => await StepAsync(1)));
        top.Controls.Add(Button("★ Favori", (_, _) => ToggleFavorite()));
        top.Controls.Add(Button("EPG", async (_, _) => await ShowEpgAsync()));
        top.Controls.Add(Button("Ses / altyazı", (_, _) => ShowTracks()));
        top.Controls.Add(Button("Tam ekran", (_, _) => ToggleFullscreen()));
        picture.Items.AddRange(new object[] { "Görüntü: Kaynağa sadık", "Görüntü: Taramalı yayın", "Görüntü: Akıcı", "Görüntü: Net görünüm" });
        picture.SelectedIndex = 0; top.Controls.Add(picture);
        top.Controls.Add(new Label { Text = "Ses", AutoSize = true, Margin = new Padding(8, 7, 0, 0) });
        var volume = new TrackBar { Width = 95, Minimum = 0, Maximum = 100, Value = 80, TickStyle = TickStyle.None,
            Margin = new Padding(0, 7, 0, 0) };
        volume.Scroll += (_, _) => player.Volume = volume.Value;
        top.Controls.Add(volume);
        left = new Panel { Dock = DockStyle.Left, Width = 340, BackColor = Color.FromArgb(24, 31, 42), Padding = new Padding(10) };
        left.Controls.Add(list); left.Controls.Add(filter); left.Controls.Add(groups); left.Controls.Add(favoritesOnly);
        filter.TextChanged += (_, _) => ApplyFilter();
        groups.SelectedIndexChanged += (_, _) => ApplyFilter();
        favoritesOnly.CheckedChanged += (_, _) => ApplyFilter();
        list.DoubleClick += async (_, _) => await PlaySelectedAsync();
        list.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; await PlaySelectedAsync(); } };
        video.DoubleClick += (_, _) => ToggleFullscreen();
        var videoMenu = new ContextMenuStrip();
        videoMenu.Items.Add("Tam ekran / çık (F11)", null, (_, _) => ToggleFullscreen());
        video.ContextMenuStrip = videoMenu;
        player.Volume = volume.Value;
        player.EncounteredError += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() => { if (!IsDisposed) ScheduleLiveRecovery(); });
        };
        player.EndReached += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(() => { if (!IsDisposed) ScheduleLiveRecovery(); });
        };
        Controls.Add(video); Controls.Add(left); Controls.Add(status); Controls.Add(top);
        SetupExtras(top, left);
        SetupSubtitles(top);
        SetupRecordings(top);
        SetupSchedules();
        SetupPictureControls(top);
        SetupThemes(top);
        SetupSettings(top);
        SetupNavigation();
        SetupVisuals();
        SetupCinemaShell();
        Shown += async (_, _) => await RestoreLastSourceAsync();
        FormClosing += (_, _) => { SavePlaybackPosition(); Application.RemoveMessageFilter(fullscreenKeys);
            CancelSubtitleJob(); scheduleTimer.Stop(); scheduleTimer.Dispose(); StopAllRecordings(); playbackTimer.Stop(); player.Stop(); player.Dispose(); libvlc.Dispose(); http.Dispose(); epgHttp.Dispose(); stalkerRequestGate.Dispose(); };
    }

    private static Button Button(string caption, EventHandler action)
    {
        var b = new Button { Text = caption, AutoSize = true, Height = 32, Margin = new Padding(4, 0, 4, 0),
            BackColor = Color.FromArgb(57, 94, 140), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        b.Click += action; return b;
    }

    private void UpdateFields()
    {
        bool login = source.SelectedIndex >= 2;
        username.Visible = login; password.Visible = source.SelectedIndex == 2;
        username.PlaceholderText = source.SelectedIndex == 3 ? "MAC (00:1A:79:...)" : "Kullanıcı";
        address.PlaceholderText = source.SelectedIndex == 0 ? "M3U dosya yolu" : "http(s):// adres";
        UpdateSourceDialogFields();
    }

    private void PickFile()
    {
        using var dialog = new OpenFileDialog { Filter = "M3U listesi|*.m3u;*.m3u8;*.txt|Tüm dosyalar|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { source.SelectedIndex = 0; address.Text = dialog.FileName; }
    }

    private async Task LoadAsync()
    {
        if (loadingSource) return;
        loadingSource = true;
        using var cancellation = new CancellationTokenSource();
        sourceLoadCancellation = cancellation;
        sourceCloseRequested = false;
        int catalogGeneration = ++stalkerCatalogGeneration;
        try
        {
            NormalizeSourceInput();
            SetCinemaLoading(true);
            status.Text = "Kaynak yükleniyor...";
            var previous = await ReadChannelCacheAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (previous.Items.Count > 0)
            {
                activeSource = source.SelectedIndex; offlineCache = true;
                libraryPositions.Clear(); cinemaScroll.Clear();
                ReplaceLoadedCatalog(previous.Items);
                status.Text = $"Kayıtlı liste hazır: {previous.Items.Count:N0} yayın. Sunucudan yenileniyor...";
            }
            currentStarted = ""; currentExpires = "";
            List<Channel> loaded = source.SelectedIndex switch
            {
                0 => await ParseM3uFileAsync(),
                1 => await ParseM3uUrlAsync(),
                2 => await LoadXtreamAsync(),
                3 => await LoadStalkerAsync(),
                _ => new List<Channel>()
            };
            RequirePlaylist(loaded);
            activeSource = source.SelectedIndex;
            offlineCache = false;
            libraryPositions.Clear();
            cinemaScroll.Clear();
            ReplaceLoadedCatalog(loaded);
            if (loaded.Count > 0) _ = SaveChannelCache(loaded);
            status.Text = $"{channels.Count} öğe yüklendi. Oynatmak için çift tıklayın." +
                (activeSource == 3 && stalkerCatalogNote.Length > 0 ? " " + stalkerCatalogNote : "");
            sourceCloseRequested = sourceDialog != null;
            if (activeSource == 3)
            {
                status.Text = $"{channels.Count} canlı yayın hazır. Film ve diziler arka planda yükleniyor...";
                _ = LoadStalkerCatalogLaterAsync(loaded.ToList(), catalogGeneration);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { status.Text = "Yükleme iptal edildi."; }
        catch (Exception e)
        {
            var fallback = e is HttpRequestException or TaskCanceledException or TimeoutException
                ? await ReadChannelCacheAsync() : (new List<Channel>(), default(DateTime));
            var (cached, cachedAt) = fallback;
            if (cached.Count > 0)
            {
                activeSource = source.SelectedIndex;
                offlineCache = true;
                libraryPositions.Clear();
                cinemaScroll.Clear();
                ReplaceLoadedCatalog(cached);
                status.Text = $"Çevrimdışı önbellek: {cached.Count} öğe ({cachedAt:g}). Yayın için ağ gerekir.";
            }
            else
            {
                status.Text = "Yüklenemedi: " + ExplainError(e);
                MessageBox.Show(sourceDialog is { IsDisposed: false } ? sourceDialog : this,
                    ExplainError(e), "Kaynak hatası", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            UseWaitCursor = false; loadingSource = false; sourceLoadCancellation = null;
            SetCinemaLoading(false);
            if (sourceCloseRequested && sourceDialog is { IsDisposed: false }) sourceDialog.Close();
        }
    }

    private static string RequireHttp(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            throw new FormatException("Geçerli bir http(s) adresi girin.");
        return u.ToString().TrimEnd('/');
    }

    private async Task<List<Channel>> ParseM3uUrlAsync()
    {
        NormalizeSourceInput();
        if (source.SelectedIndex == 0) return await ParseM3uFileAsync();
        string url = RequireHttp(address.Text);
        int generation = stalkerCatalogGeneration;
        var progress = M3uProgress(generation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(SourceLoadToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            var parsed = await Task.Run(async () =>
            {
                using var client = new HttpClient(new HttpClientHandler
                { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
                { Timeout = Timeout.InfiniteTimeSpan };
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                return await ReadM3uStreamAsync(stream, new Uri(url), progress, deadline.Token).ConfigureAwait(false);
            }, deadline.Token);
            DiscoverEpgUrl(parsed.Header, new Uri(url));
            RequirePlaylist(parsed.Items);
            _ = TryLoadM3uAccountDatesAsync(url, generation);
            return parsed.Items;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !SourceLoadToken.IsCancellationRequested)
        { throw new TimeoutException("M3U listesi dört dakikada indirilemedi. Kaynak ağ tanısını kontrol edin."); }
    }

    private async Task<List<Channel>> ParseM3uFileAsync()
    {
        NormalizeSourceInput();
        if (source.SelectedIndex == 1) return await ParseM3uUrlAsync();
        string path = address.Text;
        var token = SourceLoadToken;
        var progress = M3uProgress(stalkerCatalogGeneration);
        var parsed = await Task.Run(async () =>
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadM3uStreamAsync(stream, null, progress, token).ConfigureAwait(false);
        }, token);
        DiscoverEpgUrl(parsed.Header, null);
        RequirePlaylist(parsed.Items);
        return parsed.Items;
    }

    private void DiscoverEpgUrl(string content, Uri? baseUrl)
    {
        string header = content.Split('\n', 2)[0];
        var match = Regex.Match(header, "(?:url-tvg|x-tvg-url)=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (!match.Success) return;
        if (Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            epgAddress.Text = url.ToString();
        else if (baseUrl != null && Uri.TryCreate(baseUrl, match.Groups[1].Value, out url) && url.Scheme is "http" or "https")
            epgAddress.Text = url.ToString();
    }

    private static List<Channel> ParseM3u(string text, Uri? baseUri)
    {
        var parser = new M3uAccumulator();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null) parser.AddLine(line, baseUri);
        return parser.Items;
    }

    private sealed class M3uAccumulator
    {
        public List<Channel> Items { get; } = new();
        private string name = "Yayın", group = "Genel", epgId = "", logo = "";
        private bool hasInfo;
        private readonly Dictionary<string, string> kindsByGroup = new(StringComparer.OrdinalIgnoreCase);

        public void AddLine(string line, Uri? baseUri)
        {
            string item = line.Trim();
            if (item.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                hasInfo = true;
                var groupMatch = Regex.Match(item, "group-title=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                group = groupMatch.Success ? groupMatch.Groups[1].Value : "Genel";
                var idMatch = Regex.Match(item, "tvg-id=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                epgId = idMatch.Success ? idMatch.Groups[1].Value : "";
                var logoMatch = Regex.Match(item, "tvg-logo=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                logo = logoMatch.Success ? logoMatch.Groups[1].Value : "";
                if (logo.Length > 0 && baseUri != null && Uri.TryCreate(baseUri, logo, out var logoUri)) logo = logoUri.ToString();
                int comma = item.LastIndexOf(','); name = comma >= 0 ? item[(comma + 1)..].Trim() : "Yayın";
            }
            else if (item.Length > 0 && !item.StartsWith('#'))
            {
                if (!kindsByGroup.TryGetValue(group, out string? kind))
                    kindsByGroup[group] = kind = KindFromGroup(group);
                if (Uri.TryCreate(item, UriKind.Absolute, out var absolute) && (absolute.Scheme == "http" || absolute.Scheme == "https"))
                    Items.Add(new Channel(name, group, absolute.ToString(), kind, EpgId: epgId, PosterUrl: logo));
                else if (hasInfo && baseUri != null && Uri.TryCreate(baseUri, item, out var relative) &&
                    (relative.Scheme == "http" || relative.Scheme == "https"))
                    Items.Add(new Channel(name, group, relative.ToString(), kind, EpgId: epgId, PosterUrl: logo));
                name = "Yayın"; group = "Genel"; epgId = ""; logo = "";
                hasInfo = false;
            }
        }
    }

    private static string KindFromGroup(string group)
    {
        if (Regex.IsMatch(group, "film|movie|vod", RegexOptions.IgnoreCase)) return "Film";
        if (Regex.IsMatch(group, "dizi|series|show", RegexOptions.IgnoreCase)) return "Dizi";
        return "Canlı TV";
    }

    private async Task<List<Channel>> LoadXtreamAsync()
    {
        string server = RequireHttp(address.Text), user = username.Text.Trim(), pass = password.Text;
        if (user.Length == 0 || pass.Length == 0) throw new FormatException("Xtream kullanıcı adı ve parola gerekli.");
        string api = $"{server}/player_api.php?username={Uri.EscapeDataString(user)}&password={Uri.EscapeDataString(pass)}";
        string authText = await http.GetStringAsync(api, SourceLoadToken);
        using var auth = await Task.Run(() => JsonDocument.Parse(authText), SourceLoadToken);
        if (!auth.RootElement.TryGetProperty("user_info", out var info) ||
            (info.TryGetProperty("auth", out var allowed) && allowed.ToString() != "1"))
            throw new InvalidOperationException("Xtream hesabı doğrulanamadı.");
        UpdateCurrentSourceDates(Str(info, "created_at"), Str(info, "exp_date"));
        var result = new List<Channel>();
        foreach (var (kind, action, route, categoryAction) in new[] { ("Canlı TV", "get_live_streams", "live", "get_live_categories"),
            ("Film", "get_vod_streams", "movie", "get_vod_categories"),
            ("Dizi", "get_series", "series", "get_series_categories") })
        {
            try
            {
                var categories = new Dictionary<string, string>();
                var categoryPositions = new Dictionary<string, int>();
                try
                {
                    string categoryText = await http.GetStringAsync(api + "&action=" + categoryAction, SourceLoadToken);
                    using var categoryDoc = await Task.Run(() => JsonDocument.Parse(categoryText), SourceLoadToken);
                    if (categoryDoc.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (var category in categoryDoc.RootElement.EnumerateArray())
                        {
                            string categoryId = Str(category, "category_id");
                            categories[categoryId] = Str(category, "category_name");
                            if (!categoryPositions.ContainsKey(categoryId)) categoryPositions[categoryId] = categoryPositions.Count;
                        }
                }
                catch (Exception) { /* Some providers do not return categories. */ }
                string catalogText = await http.GetStringAsync(api + "&action=" + action, SourceLoadToken);
                var section = await Task.Run(() =>
                {
                    using var doc = JsonDocument.Parse(catalogText);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<Channel>();
                var rows = new List<Channel>();
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    string id = Str(row, route == "series" ? "series_id" : "stream_id");
                    if (id.Length == 0) continue;
                    string ext = Str(row, "container_extension");
                    if (ext.Length == 0) ext = route == "live" ? "ts" : "mp4";
                    string url = route == "series" ? "" : $"{server}/{route}/{Uri.EscapeDataString(user)}/{Uri.EscapeDataString(pass)}/{id}.{ext}";
                    string groupId = Str(row, "category_id");
                    int.TryParse(Str(row, "num"), out int number);
                    rows.Add(new Channel(Str(row, "name"), categories.TryGetValue(groupId, out var groupName) ? groupName : groupId, url, kind,
                        route == "series" || route == "live" ? id : null,
                        categoryPositions.TryGetValue(groupId, out int groupPosition) ? groupPosition : int.MaxValue, number,
                        Str(row, "epg_channel_id"), CatalogImage(row, server), Str(row, "plot")));
                }
                return rows.OrderBy(c => c.BouquetOrder).ThenBy(c => c.ChannelOrder > 0 ? c.ChannelOrder : int.MaxValue).ToList();
                }, SourceLoadToken);
                result.AddRange(section);
            }
            catch (HttpRequestException) { /* Some providers expose only part of the API. */ }
        }
        return result;
    }

    private async Task<List<Channel>> LoadStalkerAsync()
    {
        stalkerCatalogNote = "";
        stalkerBase = RequireHttp(address.Text); stalkerMac = username.Text.Trim();
        if (!Regex.IsMatch(stalkerMac, @"^[0-9a-fA-F]{2}(:[0-9a-fA-F]{2}){5}$"))
            throw new FormatException("MAC adresini 00:1A:79:00:00:00 biçiminde girin.");
        string endpoint = stalkerBase.EndsWith("/server/load.php", StringComparison.OrdinalIgnoreCase)
            ? stalkerBase : stalkerBase + "/server/load.php";
        stalkerBase = endpoint;
        JsonDocument handshake;
        try { handshake = await StalkerRequestAsync("type=stb&action=handshake&JsHttpRequest=1-xml", false); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound &&
            !address.Text.TrimEnd('/').EndsWith("load.php", StringComparison.OrdinalIgnoreCase))
        {
            stalkerBase = RequireHttp(address.Text) + "/stalker_portal/server/load.php";
            handshake = await StalkerRequestAsync("type=stb&action=handshake&JsHttpRequest=1-xml", false);
        }
        using (handshake)
        {
        stalkerToken = Str(handshake.RootElement.GetProperty("js"), "token");
        }
        if (stalkerToken.Length == 0) throw new InvalidOperationException("Stalker token alınamadı.");
        string startDate = "", endDate = "";
        try
        {
            using var profile = await StalkerRequestAsync("type=stb&action=get_profile&JsHttpRequest=1-xml", true);
            var account = StalkerJs(profile.RootElement);
            startDate = FirstDate(account, "start_date", "created_at", "created", "activation_date");
            endDate = FirstDate(account, "expire_billing_date", "end_date", "expire_date", "expiration_date");
        }
        catch (HttpRequestException) { /* Profile is optional; proceed to channels. */ }
        UpdateCurrentSourceDates(startDate, endDate);
        var genres = new Dictionary<string, string>();
        var genrePositions = new Dictionary<string, int>();
        try
        {
            using var genreDoc = await StalkerRequestAsync("type=itv&action=get_genres&JsHttpRequest=1-xml", true);
            var genreArray = genreDoc.RootElement.GetProperty("js");
            if (genreArray.ValueKind == JsonValueKind.Array)
                foreach (var genre in genreArray.EnumerateArray())
                {
                    string id = Str(genre, "id");
                    genres[id] = Str(genre, "title");
                    if (!genrePositions.ContainsKey(id)) genrePositions[id] = genrePositions.Count;
                }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { throw; }
        catch (Exception) { /* Genre endpoint is optional. */ }
        using var doc = await StalkerRequestAsync("type=itv&action=get_all_channels&JsHttpRequest=1-xml", true);
        var js = doc.RootElement.GetProperty("js");
        if (js.ValueKind == JsonValueKind.Object && js.TryGetProperty("data", out var data)) js = data;
        var result = new List<Channel>();
        if (js.ValueKind == JsonValueKind.Array)
            foreach (var row in js.EnumerateArray())
            {
                string groupId = Str(row, "tv_genre_id");
                int.TryParse(Str(row, "number"), out int number);
                result.Add(new Channel(Str(row, "name"), genres.TryGetValue(groupId, out var groupName) ? groupName : groupId,
                    "", "Canlı TV", Str(row, "cmd"),
                    genrePositions.TryGetValue(groupId, out int groupPosition) ? groupPosition : int.MaxValue, number,
                    Str(row, "id"), CatalogImage(row, stalkerBase)));
            }
        return result.OrderBy(c => c.Kind == "Canlı TV" ? 0 : c.Kind == "Film" ? 1 : 2)
            .ThenBy(c => c.BouquetOrder).ThenBy(c => c.ChannelOrder > 0 ? c.ChannelOrder : int.MaxValue).ToList();
    }

    private async Task LoadStalkerCatalogLaterAsync(List<Channel> loaded, int generation)
    {
        try
        {
            await LoadStalkerVodAsync(loaded, generation);
            if (generation != stalkerCatalogGeneration || IsDisposed) return;
            if (catalogBeforeEpisodes != null)
            {
                catalogBeforeEpisodes = loaded.OrderBy(c => c.Kind == "Canlı TV" ? 0 : c.Kind == "Film" ? 1 : 2)
                    .ThenBy(c => c.BouquetOrder).ThenBy(c => c.ChannelOrder).ToList();
                _ = SaveChannelCache(catalogBeforeEpisodes);
                return;
            }
            channels.Clear(); channels.AddRange(loaded.OrderBy(c => c.Kind == "Canlı TV" ? 0 : c.Kind == "Film" ? 1 : 2)
                .ThenBy(c => c.BouquetOrder).ThenBy(c => c.ChannelOrder));
            string selectedKind = kinds.SelectedItem as string ?? "Canlı TV";
            string selectedGroup = groups.SelectedItem as string ?? "Tümü";
            RefreshGroups();
            if (kinds.Items.Contains(selectedKind)) kinds.SelectedItem = selectedKind;
            if (groups.Items.Contains(selectedGroup)) groups.SelectedItem = selectedGroup;
            if (channels.Count > 0) _ = SaveChannelCache(channels.ToList());
            if (playingChannel == null || !player.IsPlaying)
                status.Text = $"{channels.Count} öğe hazır." + (stalkerCatalogNote.Length > 0 ? " " + stalkerCatalogNote : "");
        }
        catch (Exception ex)
        {
            if (generation == stalkerCatalogGeneration && !IsDisposed)
                status.Text = "Canlı yayınlar hazır; film/dizi kataloğu alınamadı: " + ExplainError(ex);
        }
    }

    private async Task<JsonDocument> StalkerRequestAsync(string query, bool authorized, bool allowRefresh = true)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, stalkerBase + "?" + query);
        req.Headers.TryAddWithoutValidation("Cookie", "mac=" + Uri.EscapeDataString(stalkerMac) + "; stb_lang=en; timezone=Europe/Istanbul");
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3");
        req.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: Ethernet");
        if (authorized) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + stalkerToken);
        HttpResponseMessage response;
        await stalkerRequestGate.WaitAsync(SourceLoadToken);
        try
        {
            var delay = stalkerNextRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, SourceLoadToken);
            stalkerLastAction = query.Contains("action=create_link", StringComparison.Ordinal) ? "Oynatma bağlantısı" :
                query.Contains("action=get_ordered_list", StringComparison.Ordinal) ? "Katalog" :
                query.Contains("action=handshake", StringComparison.Ordinal) ? "Oturum" : "Portal bilgisi";
            response = await http.SendAsync(req, SourceLoadToken);
            stalkerLastStatus = (int)response.StatusCode;
            stalkerNextRequestUtc = DateTime.UtcNow.AddMilliseconds(250);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var backoff = response.Headers.RetryAfter?.Delta;
                if (backoff == null && response.Headers.RetryAfter?.Date is DateTimeOffset until)
                    backoff = until - DateTimeOffset.UtcNow;
                stalkerNextRequestUtc = DateTime.UtcNow + (backoff is { TotalSeconds: > 0 and <= 60 }
                    ? backoff.Value : TimeSpan.FromSeconds(5));
            }
        }
        finally { stalkerRequestGate.Release(); }
        using (response)
        {
        if (authorized && allowRefresh && response.StatusCode is
            (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
        {
            using var handshake = await StalkerRequestAsync("type=stb&action=handshake&JsHttpRequest=1-xml", false);
            stalkerToken = Str(StalkerJs(handshake.RootElement), "token");
            if (stalkerToken.Length > 0) return await StalkerRequestAsync(query, true, false);
        }
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Retry a playback link once; stop catalog pagination on 429.
            if (query.Contains("action=create_link", StringComparison.Ordinal) && allowRefresh &&
                stalkerNextRequestUtc - DateTime.UtcNow <= TimeSpan.FromSeconds(30))
            {
                return await StalkerRequestAsync(query, authorized, false);
            }
            throw new HttpRequestException("Portal istek sınırına ulaştı (HTTP 429). Bir süre bekleyip tekrar deneyin.",
                null, HttpStatusCode.TooManyRequests);
        }
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync(SourceLoadToken);
        return await Task.Run(() => JsonDocument.Parse(content), SourceLoadToken);
        }
    }

    private static string Str(JsonElement element, string key)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property)
            ? property.ToString() : "";
    }

    private void ApplyFilter()
    {
        if (updatingLibrary) return;
        Channel? previousSelection = list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count ? visible[list.SelectedIndex] : null;
        string q = filter.Text.Trim();
        string group = groups.SelectedItem as string ?? "Tümü";
        string kind = kinds.SelectedItem as string ?? "Canlı TV";
        var candidate = channels.Where(c => (kind is "Tümü" or "Önerilen" || c.Kind == kind) &&
            (group == "Tümü" || group == c.Kind + " / " + c.Group) &&
            (!favoritesOnly.Checked || favorites.Contains(FavoriteKey(c))));
        if (kind == "Önerilen") candidate = RankRecommendations(candidate);
        visible = q.Length == 0 ? candidate.ToList() : candidate.Select(c => (Channel: c, Score: SmartSearchScore(c, q)))
            .Where(x => x.Score >= 0).OrderByDescending(x => x.Score).Select(x => x.Channel).ToList();
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            list.Items.AddRange(visible.Select(c =>
                $"{(favorites.Count > 0 && favorites.Contains(FavoriteKey(c)) ? "★ " : "")}{c.Name}{EpgLabel(c)}").ToArray());
        }
        finally { list.EndUpdate(); }
        if (previousSelection != null)
        {
            int index = visible.IndexOf(previousSelection);
            if (index >= 0) list.SelectedIndex = index;
        }
        UpdateVisualSummary();
        RefreshCinemaCatalog();
    }

    private void RefreshGroups()
    {
        if (updatingLibrary) return;
        updatingLibrary = true;
        groups.BeginUpdate();
        try
        {
            UpdateLibraryCounts();
            groups.Items.Clear(); groups.Items.Add("Tümü");
            string selectedKind = kinds.SelectedItem as string ?? "Canlı TV";
            groups.Items.AddRange(channels.Where(c => selectedKind is "Tümü" or "Önerilen" || c.Kind == selectedKind)
                .Select(c => c.Kind + " / " + c.Group).Distinct().Cast<object>().ToArray());
            groups.SelectedIndex = 0;
        }
        finally { groups.EndUpdate(); updatingLibrary = false; }
        ApplyFilter();
    }

    private string FavoriteKey(Channel c)
    {
        string identity = activeSource + "|" + (activeSource == 3 ? stalkerBase : address.Text.Trim()) + "|" + c.Kind + "|" + c.Name + "|" +
            (c.Command ?? c.Url);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private void LoadFavorites()
    {
        try
        {
            if (File.Exists(favoritesPath))
                foreach (string key in JsonSerializer.Deserialize<string[]>(File.ReadAllText(favoritesPath)) ?? Array.Empty<string>()) favorites.Add(key);
        }
        catch { status.Text = "Favori kayıtları okunamadı."; }
    }

    private void ToggleFavorite()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= visible.Count) return;
        var item = visible[list.SelectedIndex]; string key = FavoriteKey(item);
        if (!favorites.Add(key)) favorites.Remove(key);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(favoritesPath)!);
            File.WriteAllText(favoritesPath, JsonSerializer.Serialize(favorites));
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Favori kaydedilemedi"); }
        ApplyFilter();
    }

    private async Task StepAsync(int direction)
    {
        if (visible.Count == 0) return;
        int current = playingChannel == null ? list.SelectedIndex : visible.FindIndex(c => c == playingChannel);
        list.SelectedIndex = (current + direction + visible.Count) % visible.Count;
        await PlaySelectedAsync();
    }

    private void ShowTracks()
    {
        var menu = new ContextMenuStrip();
        AddTrackMenu(menu, "Ses dili", "AudioTrackDescription", "SetAudioTrack");
        AddTrackMenu(menu, "Altyazı", "SpuDescription", "SetSpu");
        menu.Show(Cursor.Position);
    }

    private async Task ShowEpgAsync()
    {
        Channel? item = list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count
            ? visible[list.SelectedIndex] : playingChannel;
        if (item != null && await ShowUnifiedEpgAsync(item)) return;
        if (item == null || activeSource != 2 || item.Kind != "Canlı TV" || item.Command == null)
        {
            MessageBox.Show(this, "EPG, bu sürümde Xtream canlı TV kanalları için kullanılabilir.", "EPG"); return;
        }
        try
        {
            string server = RequireHttp(address.Text), user = username.Text.Trim(), pass = password.Text;
            string api = $"{server}/player_api.php?username={Uri.EscapeDataString(user)}&password={Uri.EscapeDataString(pass)}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(api + "&action=get_short_epg&stream_id=" +
                Uri.EscapeDataString(item.Command) + "&limit=8"));
            if (!doc.RootElement.TryGetProperty("epg_listings", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Sağlayıcı EPG verisi döndürmedi.");
            var lines = new List<string>();
            foreach (var row in rows.EnumerateArray())
            {
                string title = DecodeEpg(Str(row, "title"));
                string start = Str(row, "start"), end = Str(row, "end");
                lines.Add(start + " – " + end + "  " + title);
            }
            MessageBox.Show(this, lines.Count > 0 ? String.Join("\n", lines) : "Program bilgisi yok.",
                "EPG: " + item.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "EPG hatası"); }
    }

    private static string DecodeEpg(string text)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(text)); }
        catch (FormatException) { return text; }
    }

    private void AddTrackMenu(ContextMenuStrip menu, string title, string property, string setter)
    {
        var parent = new ToolStripMenuItem(title); menu.Items.Add(parent);
        try
        {
            var tracks = player.GetType().GetProperty(property)?.GetValue(player) as System.Collections.IEnumerable;
            var method = player.GetType().GetMethod(setter, new[] { typeof(int) });
            if (tracks != null && method != null)
                foreach (object track in tracks)
                {
                    var id = track.GetType().GetProperty("Id")?.GetValue(track);
                    var name = track.GetType().GetProperty("Name")?.GetValue(track)?.ToString();
                    if (id == null) continue;
                    int trackId = Convert.ToInt32(id);
                    parent.DropDownItems.Add(name ?? trackId.ToString(), null, (_, _) => method.Invoke(player, new object[] { trackId }));
                }
        }
        catch { /* The stream may not provide track metadata yet. */ }
        if (parent.DropDownItems.Count == 0) parent.DropDownItems.Add("Yayın akışında seçenek yok").Enabled = false;
    }

    private async Task PlaySelectedAsync(Channel? retryItem = null)
    {
        if (retryItem == null && (list.SelectedIndex < 0 || list.SelectedIndex >= visible.Count)) return;
        bool recovering = retryItem != null;
        int attemptGeneration = recovering ? recoveryGeneration : ++recoveryGeneration;
        if (!recovering) { recoveryAttempts = 0; recoveryPending = false; }
        try
        {
            var item = retryItem ?? visible[list.SelectedIndex]; string url = item.Url;
            if (activeSource == 3 && item.Kind == "Dizi" && item.Command != null)
            {
                status.Text = "Dizi bölümleri yükleniyor...";
                await LoadStalkerEpisodesAsync(item, attemptGeneration); return;
            }
            if (activeSource == 3 && item.Command != null)
            {
                url = await ResolveStalkerPlayUrlAsync(item);
            }
            else if (item.Kind == "Dizi" && item.Command != null)
            {
                status.Text = "Dizi bölümleri yükleniyor...";
                await LoadSeriesEpisodesAsync(item.Command, attemptGeneration); return;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new InvalidOperationException("Oynatılabilir yayın adresi alınamadı.");
            if (attemptGeneration != recoveryGeneration ||
                (recovering && !ReferenceEquals(item, playingChannel))) return;
            SavePlaybackPosition();
            if (!recovering) playingChannel = null;
            player.Stop();
            using var media = new Media(libvlc, uri);
            media.AddOption(":avcodec-hw=any");
            if (item.Kind == "Canlı TV")
            {
                media.AddOption(":http-reconnect");
                media.AddOption(":network-caching=" + SelectedLiveCacheMs);
            }
            if (picture.SelectedIndex is 1 or 3)
            {
                media.AddOption(":deinterlace=1");
                media.AddOption(":deinterlace-mode=yadif");
            }
            else if (picture.SelectedIndex == 2 && item.Kind != "Canlı TV") media.AddOption(":network-caching=1800");
            if (activeSource == 3)
            {
                media.AddOption(":http-referrer=" + new Uri(stalkerBase).GetLeftPart(UriPartial.Authority));
                media.AddOption(":http-user-agent=Mozilla/5.0 (QtEmbedded; MAG250)");
            }
            if (!player.Play(media)) throw new InvalidOperationException("VLC oynatmayı başlatamadı.");
            ApplyPictureProfile();
            playingChannel = item; currentStreamUrl = url; PrepareResume(item); StartWatchSession();
            list.Invalidate();
            ShowCinemaPlayback();
            status.Text = "Oynatılıyor: " + item.Name;
        }
        catch (Exception e)
        {
            if (attemptGeneration != recoveryGeneration) return;
            status.Text = "Oynatma başarısız: " + (e is HttpRequestException request && request.StatusCode is { } code
                ? "HTTP " + (int)code : e is TaskCanceledException ? "zaman aşımı" : "bağlantı veya akış hatası");
            if (recovering) recoveryPlayFailed = true;
            else MessageBox.Show(this, ExplainError(e), "Oynatma hatası");
        }
    }

    private async Task LoadSeriesEpisodesAsync(string seriesId, int attemptGeneration)
    {
        string server = RequireHttp(address.Text), user = username.Text.Trim(), pass = password.Text;
        string api = $"{server}/player_api.php?username={Uri.EscapeDataString(user)}&password={Uri.EscapeDataString(pass)}";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(api + "&action=get_series_info&series_id=" + Uri.EscapeDataString(seriesId)));
        if (!doc.RootElement.TryGetProperty("episodes", out var seasons) || seasons.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Bu dizinin bölüm bilgisi bulunamadı.");
        var episodes = new List<Channel>();
        foreach (var season in seasons.EnumerateObject())
        {
            if (season.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var ep in season.Value.EnumerateArray())
            {
                string id = Str(ep, "id"), ext = Str(ep, "container_extension");
                if (id.Length == 0) continue;
                episodes.Add(new Channel(Str(ep, "title"), "Sezon " + season.Name,
                    $"{server}/series/{Uri.EscapeDataString(user)}/{Uri.EscapeDataString(pass)}/{id}.{(ext.Length > 0 ? ext : "mp4")}", "Bölüm"));
            }
        }
        if (attemptGeneration != recoveryGeneration) return;
        catalogBeforeEpisodes = channels.ToList();
        channels.Clear(); channels.AddRange(episodes); filter.Clear(); kinds.SelectedItem = "Bölüm"; RefreshGroups();
        status.Text = $"{episodes.Count} bölüm listelendi. Kaynağa dönmek için Yükle'ye basın.";
    }

    private void ToggleFullscreen()
    {
        if (cinemaRoot != null) { ToggleCinemaFullscreen(); return; }
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            previousWindowState = WindowState;
            top.Hide(); left.Hide(); status.Hide(); playbackBar.Hide(); navigation?.Hide();
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            video.Focus();
            return;
        }
        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.Sizable;
        WindowState = previousWindowState;
        top.Show(); left.Show(); status.Show(); playbackBar.Show(); navigation?.Show();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11 || (keyData == Keys.Escape && fullscreen))
        { ToggleFullscreen(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

