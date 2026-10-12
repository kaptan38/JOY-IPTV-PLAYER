using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private TableLayoutPanel? cinemaRoot;
    private Panel? cinemaVideoHost, cinemaMainHost;
    private PosterGallery? cinemaGallery;
    private FlowLayoutPanel? cinemaDetails;
    private readonly Dictionary<string, Button> cinemaRail = new();
    private readonly List<Button> cinemaAllRail = new();
    private readonly ContextMenuStrip cinemaToolsMenu = new();
    private readonly Label cinemaTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold) };
    private readonly Label cinemaPlayingTitle = new() { Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, Text = "JOY IPTV • Hazır" };
    private readonly Label cinemaItemTitle = new() { Width = 242, Height = 72, AutoEllipsis = true,
        Font = new Font("Segoe UI", 17, FontStyle.Bold), Text = "İçerik seçin" };
    private readonly Label cinemaItemMeta = new() { Width = 242, Height = 46, AutoEllipsis = true };
    private readonly Label cinemaItemPlot = new() { Width = 242, Height = 110 };
    private readonly Label cinemaRecommendation = new() { Width = 242, Height = 64 };
    private readonly PictureBox cinemaPoster = new() { Width = 242, Height = 190, SizeMode = PictureBoxSizeMode.Zoom };
    private bool cinemaPlayingView;
    private string cinemaPosterIdentity = "";
    private readonly Dictionary<string, int> cinemaScroll = new();

    private static string CatalogImage(JsonElement row, string baseUrl)
    {
        foreach (string key in new[] { "stream_icon", "cover", "movie_image", "screenshot_uri", "logo" })
        {
            string value = Str(row, key);
            if (value.Length == 0) continue;
            if (Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https") return url.ToString();
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) && Uri.TryCreate(root, value, out url) &&
                url.Scheme is "http" or "https") return url.ToString();
        }
        return "";
    }

    private void SetupCinemaShell()
    {
        MinimumSize = new Size(1060, 660);
        SuspendLayout();
        Controls.Remove(top); Controls.Remove(left); Controls.Remove(video); Controls.Remove(playbackBar);
        top.Visible = false;
        cinemaRoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Padding = Padding.Empty, Margin = Padding.Empty };
        cinemaRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        cinemaRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cinemaRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cinemaRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        var rail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(12, 10, 12, 12), Margin = Padding.Empty };
        rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        rail.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "▶ JOY IPTV", TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 18, FontStyle.Bold) }, 0, 0);
        var railItems = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true };
        foreach (var item in new[] { ("Tümü", "⌂  Ana Sayfa"), ("Canlı TV", "▣  Canlı TV"), ("Film", "▤  Filmler"),
            ("Dizi", "▥  Diziler"), ("Önerilen", "✦  Sana Özel") })
        {
            string kind = item.Item1;
            var button = RailButton(item.Item2, (_, _) =>
            {
                favoritesOnly.Checked = false; cinemaPlayingView = false;
                kinds.SelectedItem = kind; UpdateCinemaMode();
            });
            cinemaRail[kind] = button; railItems.Controls.Add(button);
        }
        railItems.Controls.Add(RailButton("♡  Favoriler", (_, _) =>
        { cinemaPlayingView = false; kinds.SelectedItem = "Tümü"; favoritesOnly.Checked = true; UpdateCinemaMode(); }));
        railItems.Controls.Add(RailButton("◉  Kayıtlar", (_, _) => ShowRecordingManager()));
        railItems.Controls.Add(RailButton("⋯  Araçlar", (sender, _) =>
        { if (sender is Control control) cinemaToolsMenu.Show(control, new Point(control.Width, 0)); }));
        rail.Controls.Add(railItems, 0, 1);
        var lower = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        lower.Controls.Add(RailButton("▣  Kaynaklar", (_, _) => ShowCinemaSources()));
        lower.Controls.Add(RailButton("⚙  Ayarlar", (_, _) => showSettings?.Invoke()));
        rail.Controls.Add(lower, 0, 2);
        cinemaRoot.Controls.Add(rail, 0, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(16, 14, 12, 6), Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cinemaTitle.Dock = DockStyle.Fill; header.Controls.Add(cinemaTitle, 0, 0);
        filter.Dock = DockStyle.Fill; filter.Margin = new Padding(8, 10, 8, 14); header.Controls.Add(filter, 1, 0);
        savedSourceBox.Dock = DockStyle.Fill; savedSourceBox.Margin = new Padding(6, 10, 2, 14); header.Controls.Add(savedSourceBox, 2, 0);
        content.Controls.Add(header, 0, 0);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        groups.Dock = DockStyle.Fill; filters.Controls.Add(groups, 0, 0);
        favoritesOnly.Dock = DockStyle.Fill; filters.Controls.Add(favoritesOnly, 1, 0);
        librarySummary.Dock = DockStyle.Fill; filters.Controls.Add(librarySummary, 2, 0);
        if (navigation != null)
        {
            navigation.Parent?.Controls.Remove(navigation);
            foreach (ToolStripMenuItem section in navigation.Items.Cast<ToolStripMenuItem>().ToArray())
            { section.Alignment = ToolStripItemAlignment.Left; cinemaToolsMenu.Items.Add(section); }
            MainMenuStrip = null;
        }
        content.Controls.Add(filters, 0, 1);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 276));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cinemaMainHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        cinemaVideoHost = new Panel { Dock = DockStyle.Fill };
        left.Controls.Clear(); left.Width = 245; left.Padding = new Padding(0, 4, 8, 0);
        list.Dock = DockStyle.Fill; left.Controls.Add(list); left.Dock = DockStyle.Left;
        video.Dock = DockStyle.Fill; cinemaVideoHost.Controls.Add(video); cinemaVideoHost.Controls.Add(left);
        cinemaGallery = new PosterGallery { Dock = DockStyle.Fill };
        cinemaMainHost.Controls.Add(cinemaVideoHost); cinemaMainHost.Controls.Add(cinemaGallery);
        body.Controls.Add(cinemaMainHost, 0, 0);
        cinemaDetails = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 4, 0),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        cinemaDetails.Controls.Add(cinemaPoster); cinemaDetails.Controls.Add(cinemaItemTitle);
        cinemaDetails.Controls.Add(cinemaItemMeta); cinemaDetails.Controls.Add(cinemaItemPlot);
        var play = Button("▶  Şimdi İzle", async (_, _) => await PlaySelectedAsync());
        play.Width = 242; play.Height = 42; play.AutoSize = false; cinemaDetails.Controls.Add(play);
        var favorite = Button("♡  Favoriye ekle / çıkar", (_, _) => ToggleFavorite());
        favorite.Width = 242; favorite.Height = 40; favorite.AutoSize = false; cinemaDetails.Controls.Add(favorite);
        cinemaDetails.Controls.Add(cinemaRecommendation);
        cinemaDetails.Controls.Add(Button("✦  Sana özel öneriler", (_, _) =>
        { favoritesOnly.Checked = false; cinemaPlayingView = false; kinds.SelectedItem = "Önerilen"; UpdateCinemaMode(); }));
        body.Controls.Add(cinemaDetails, 1, 0); content.Controls.Add(body, 0, 2);
        cinemaRoot.Controls.Add(content, 1, 0);

        playbackBar.Controls.Clear(); playbackBar.Dock = DockStyle.Fill;
        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1,
            Padding = new Padding(14, 12, 12, 12) };
        foreach (var style in new[] { new ColumnStyle(SizeType.Absolute, 220), new ColumnStyle(SizeType.Absolute, 155),
            new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.Absolute, 120),
            new ColumnStyle(SizeType.Absolute, 105), new ColumnStyle(SizeType.Absolute, 80) }) transport.ColumnStyles.Add(style);
        transport.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transport.Controls.Add(cinemaPlayingTitle, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(TransportButton("|◀", async (_, _) => await StepAsync(-1)));
        actions.Controls.Add(TransportButton("▶ / Ⅱ", (_, _) => { if (player.IsPlaying) player.Pause(); else player.Play(); }));
        actions.Controls.Add(TransportButton("▶|", async (_, _) => await StepAsync(1)));
        transport.Controls.Add(actions, 1, 0); seek.Dock = DockStyle.Fill; transport.Controls.Add(seek, 2, 0);
        clock.Dock = DockStyle.Fill; transport.Controls.Add(clock, 3, 0);
        var volume = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = player.Volume,
            TickStyle = TickStyle.None };
        volume.Scroll += (_, _) => player.Volume = volume.Value; transport.Controls.Add(volume, 4, 0);
        var fullscreenButton = TransportButton("⛶", (_, _) => ToggleFullscreen()); fullscreenButton.Dock = DockStyle.Fill;
        transport.Controls.Add(fullscreenButton, 5, 0); playbackBar.Controls.Add(transport);
        cinemaRoot.Controls.Add(playbackBar, 0, 1); cinemaRoot.SetColumnSpan(playbackBar, 2);
        Controls.Add(cinemaRoot); cinemaRoot.BringToFront();
        cinemaGallery.SelectionChanged += (_, _) =>
        { if (cinemaGallery.SelectedIndex >= 0 && cinemaGallery.SelectedIndex < list.Items.Count) list.SelectedIndex = cinemaGallery.SelectedIndex; };
        cinemaGallery.ItemActivated += async (_, _) => await PlaySelectedAsync();
        cinemaGallery.ImageReady += (_, _) => RefreshCinemaDetails();
        list.SelectedIndexChanged += (_, _) =>
        { cinemaGallery.SelectItem(list.SelectedIndex); RefreshCinemaDetails(); };
        kinds.SelectedIndexChanged += (_, _) => { cinemaPlayingView = false; UpdateCinemaMode(); };
        FormClosed += (_, _) => { cinemaPoster.Image?.Dispose(); cinemaToolsMenu.Dispose(); top.Dispose(); };
        ResumeLayout(); ApplyTheme();
        if (channels.Count == 0) kinds.SelectedItem = "Tümü";
        RefreshCinemaCatalog();
    }

    private Button RailButton(string text, EventHandler click)
    {
        var button = Button(text, click); button.Width = 150; button.Height = 48; button.AutoSize = false;
        button.TextAlign = ContentAlignment.MiddleLeft; button.Margin = new Padding(0, 0, 0, 9);
        button.FlatAppearance.BorderSize = 0; cinemaAllRail.Add(button); return button;
    }
    private static Button TransportButton(string text, EventHandler click)
    { var button = Button(text, click); button.AutoSize = false; button.Width = 44; button.Height = 42; return button; }

    private void ShowCinemaSources()
    {
        using var window = new Form { Text = "JOY IPTV • Kaynak ekle ve yönet", Width = 840, Height = 340,
            StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor, Font = Font };
        top.Dock = DockStyle.Fill; top.Visible = true; window.Controls.Add(top);
        try { window.ShowDialog(this); }
        finally { window.Controls.Remove(top); top.Visible = false; top.Dock = DockStyle.Top; }
    }

    private void RefreshCinemaCatalog()
    {
        if (cinemaGallery == null) return;
        cinemaGallery.SetItems(visible, list.SelectedIndex);
        if (visible.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
        UpdateCinemaMode(); RefreshCinemaDetails();
    }

    private void UpdateCinemaMode()
    {
        if (cinemaGallery == null || cinemaVideoHost == null || fullscreen) return;
        string kind = kinds.SelectedItem as string ?? "Tümü";
        cinemaTitle.Text = kind switch { "Film" => "Film Kütüphanesi", "Dizi" => "Dizi Kütüphanesi",
            "Bölüm" => "Dizi Bölümleri", "Canlı TV" => "Canlı TV", "Önerilen" => "Sana Özel", _ => "Ana Sayfa" };
        bool showVideo = cinemaPlayingView || kind == "Canlı TV";
        cinemaGallery.Visible = !showVideo; cinemaVideoHost.Visible = showVideo;
        left.Visible = kind == "Canlı TV";
        if (showVideo) cinemaVideoHost.BringToFront(); else cinemaGallery.BringToFront();
        foreach (var pair in cinemaRail)
        {
            bool active = pair.Key == kind;
            pair.Value.BackColor = active ? selectionColor : top.BackColor;
            pair.Value.ForeColor = active ? Color.FromArgb(14, 40, 46) : ForeColor;
        }
    }

    private void RefreshCinemaDetails()
    {
        if (cinemaGallery == null) return;
        var item = list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count ? visible[list.SelectedIndex] : null;
        if (item == null)
        {
            cinemaItemTitle.Text = "İçerik seçin"; cinemaItemMeta.Text = ""; cinemaItemPlot.Text = "Kaynaklar bölümünden M3U, Xtream veya Stalker kaynağı ekleyebilirsiniz.";
            cinemaRecommendation.Text = ""; cinemaPosterIdentity = "";
            var previous = cinemaPoster.Image; cinemaPoster.Image = null; previous?.Dispose(); return;
        }
        cinemaItemTitle.Text = item.Name; cinemaItemMeta.Text = item.Kind + " • " + item.Group;
        cinemaItemPlot.Text = string.IsNullOrWhiteSpace(item.Description)
            ? "Sağlayıcı bu içerik için özet göndermedi." : item.Description;
        cinemaRecommendation.Text = (kinds.SelectedItem as string) == "Önerilen" ? "Sana özel • " + RecommendationReason(item)
            : "Kategori ve sıralama yayıncının kataloğuna göre gösterilir.";
        Image? poster = cinemaGallery.GetPoster(item.PosterUrl);
        string identity = item.Name + "|" + item.PosterUrl + "|" + (poster == null ? "fallback" : "image");
        if (identity == cinemaPosterIdentity) return;
        cinemaPosterIdentity = identity;
        Image? old = cinemaPoster.Image;
        if (poster != null) cinemaPoster.Image = new Bitmap(poster);
        else
        {
            var bitmap = new Bitmap(242, 190);
            using var graphics = Graphics.FromImage(bitmap);
            using var gradient = new LinearGradientBrush(new Rectangle(0, 0, 242, 190),
                Color.FromArgb(33, 75, 91), Color.FromArgb(92, 55, 83), 45f);
            graphics.FillRectangle(gradient, 0, 0, 242, 190);
            using var font = new Font("Segoe UI", 26, FontStyle.Bold);
            TextRenderer.DrawText(graphics, item.Kind == "Canlı TV" ? "TV" : item.Kind == "Film" ? "FİLM" : "DİZİ",
                font, new Rectangle(0, 0, 242, 190), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            cinemaPoster.Image = bitmap;
        }
        old?.Dispose();
    }

    private void ApplyCinemaTheme()
    {
        if (cinemaGallery == null) return;
        cinemaGallery.BackColor = BackColor; cinemaGallery.ForeColor = ForeColor;
        cinemaGallery.Muted = detailColor; cinemaGallery.Accent = selectionColor; cinemaGallery.Invalidate();
        cinemaItemPlot.ForeColor = detailColor; cinemaItemMeta.ForeColor = detailColor;
        foreach (var button in cinemaAllRail) { button.BackColor = top.BackColor; button.ForeColor = ForeColor; }
        cinemaToolsMenu.BackColor = top.BackColor; cinemaToolsMenu.ForeColor = ForeColor;
        cinemaToolsMenu.Renderer = new ToolStripProfessionalRenderer(new NavigationColors(top.BackColor, ForeColor));
        foreach (ToolStripMenuItem section in cinemaToolsMenu.Items)
        {
            section.ForeColor = ForeColor; section.DropDown.BackColor = top.BackColor;
            foreach (ToolStripItem action in section.DropDownItems) action.ForeColor = ForeColor;
        }
        UpdateCinemaMode();
    }

    private void ShowCinemaPlayback()
    {
        if (cinemaRoot == null) return;
        cinemaPlayingView = true;
        cinemaPlayingTitle.Text = playingChannel == null ? "JOY IPTV • Hazır" : playingChannel.Name;
        UpdateCinemaMode();
    }

    private void ToggleCinemaFullscreen()
    {
        if (cinemaRoot == null || cinemaVideoHost == null) return;
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            previousWindowState = WindowState;
            cinemaRoot.Hide(); status.Hide();
            video.Parent?.Controls.Remove(video); Controls.Add(video); video.Dock = DockStyle.Fill; video.Show(); video.BringToFront();
            FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; video.Focus();
        }
        else
        {
            Controls.Remove(video); cinemaVideoHost.Controls.Add(video); video.Dock = DockStyle.Fill;
            video.BringToFront();
            WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.Sizable; WindowState = previousWindowState;
            cinemaRoot.Show(); status.Show(); UpdateCinemaMode();
        }
    }
}
