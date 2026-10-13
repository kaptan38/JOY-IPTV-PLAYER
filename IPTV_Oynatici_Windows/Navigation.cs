using System.Drawing;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private MenuStrip? navigation;
    private readonly Dictionary<string, Button> libraryTabs = new();
    private FlowLayoutPanel? libraryBar;
    private string currentLibraryKind = "Canlı TV";
    private readonly Dictionary<string, (string Group, Channel? Selected, int Top)> libraryPositions = new();

    private void SwitchLibraryKind()
    {
        if (updatingLibrary) return;
        string nextKind = kinds.SelectedItem as string ?? "Canlı TV";
        if (nextKind != currentLibraryKind)
        {
            if (cinemaGallery != null) cinemaScroll[currentLibraryKind] = cinemaGallery.ScrollOffset;
            libraryPositions[currentLibraryKind] =
                (groups.SelectedItem as string ?? "Tümü",
                 list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count ? visible[list.SelectedIndex] : null,
                 list.Items.Count > 0 ? list.TopIndex : 0);
            currentLibraryKind = nextKind;
        }
        RefreshGroups();
        if (libraryPositions.TryGetValue(nextKind, out var position))
        {
            if (groups.Items.Contains(position.Group)) groups.SelectedItem = position.Group;
            int index = position.Selected == null ? -1 : visible.IndexOf(position.Selected);
            if (index >= 0) list.SelectedIndex = index;
            if (list.Items.Count > 0) list.TopIndex = Math.Clamp(position.Top, 0, list.Items.Count - 1);
        }
        UpdateLibrarySelection();
        if (cinemaGallery != null) cinemaGallery.ScrollOffset = cinemaScroll.GetValueOrDefault(nextKind);
    }

    private void UpdateLibraryCounts()
    {
        RefreshCinemaCounts();
        if (libraryTabs.Count == 0) return;
        var counts = channels.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (kind, button) in libraryTabs)
            button.Text = $"{kind}\n{counts.GetValueOrDefault(kind):N0}";
        UpdateLibrarySelection();
    }

    private void UpdateLibrarySelection()
    {
        foreach (var (kind, button) in libraryTabs)
        {
            bool selected = string.Equals(kinds.SelectedItem?.ToString(), kind, StringComparison.Ordinal);
            button.FlatAppearance.BorderSize = selected ? 2 : 0;
            button.FlatAppearance.BorderColor = selectionColor;
            button.AccessibleName = $"{kind}, {(selected ? "seçili" : "seçili değil")}";
        }
    }

    private void SetupNavigation()
    {
        var menu = new MenuStrip { Dock = DockStyle.None, Padding = new Padding(10, 5, 10, 5),
            AutoSize = false, Height = 38, Margin = new Padding(0, 0, 0, 8),
            Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        navigation = menu;
        var sources = new ToolStripMenuItem("▣  Kaynaklar");
        var playback = new ToolStripMenuItem("▶  Oynat");
        var recording = new ToolStripMenuItem("◉  Kayıt ve altyazı");
        var tools = new ToolStripMenuItem("◇  Araçlar");
        var settings = new ToolStripMenuItem("⚙ Ayarlar") { Alignment = ToolStripItemAlignment.Right };
        settings.Click += (_, _) => showSettings?.Invoke();

        void Add(ToolStripMenuItem group, string name, EventHandler action)
        {
            var option = new ToolStripMenuItem(name);
            option.Click += action;
            group.DropDownItems.Add(option);
        }
        Add(sources, "Kaynağı kaydet", (_, _) => SaveSource());
        Add(sources, "Yerel profilden kaynak aç", (_, _) => OpenSource());
        Add(sources, "Kayıtlı kaynağı sil", (_, _) => RemoveSource());
        Add(sources, "Dizi kataloğuna dön", (_, _) => ReturnToCatalog());
        Add(sources, "Listeyi yenile", async (_, _) => await LoadAsync());
        sources.DropDownItems.Add(new ToolStripSeparator());
        Add(sources, "Kaynak süresi", (_, _) => ShowSourceDates());

        Add(playback, "Durdur", (_, _) => StopPlayback());
        Add(playback, "Önceki kanal", async (_, _) => await StepAsync(-1));
        Add(playback, "Sonraki kanal", async (_, _) => await StepAsync(1));
        Add(playback, "★ Favori", (_, _) => ToggleFavorite());
        Add(playback, "EPG", async (_, _) => await ShowEpgAsync());
        Add(playback, "EPG içinde ara", (_, _) => ShowEpgSearch());
        Add(playback, "İzlemeye devam et", (_, _) => ShowContinueWatching());
        Add(playback, "Ses ve altyazı izleri", (_, _) => ShowTracks());

        Add(recording, "Şimdi kayda al", async (_, _) => { await StartRecordingAsync(); });
        Add(recording, "Kayıtları yönet", (_, _) => ShowRecordingManager());
        Add(recording, "Zamanlanmış kayıt ekle", (_, _) => AddScheduledRecording());
        Add(recording, "Kayıt planlarını göster", (_, _) => ShowSchedules());
        Add(recording, "Altyazı dosyası ekle", (_, _) => ImportSubtitle());
        Add(recording, "AI altyazı oluştur (10 dk)", async (_, _) => await GenerateSubtitleAsync());
        Add(tools, "Görüntü bilgisi", (_, _) => ShowVideoInfo());
        Add(tools, "Kart / kompakt liste görünümü", (_, _) => ToggleListCards());
        Add(tools, "Akıllı yardımcı ve öneriler", (_, _) => ShowSmartAssistant());
        Add(tools, "IMDb'de film/dizi ara", (_, _) => OpenImdbSearch());
        Add(tools, "Bağlantı tanıla", async (_, _) => await DiagnoseAsync());
        Add(tools, "Kaynak DNS / ağ tanısı", async (_, _) => await DiagnoseSourceNetworkAsync());
        Add(tools, "Windows VPN ayarları", (_, _) => OpenWindowsVpnSettings());

        menu.Items.AddRange(new ToolStripItem[] { sources, playback, recording, tools, settings });
        foreach (ToolStripMenuItem section in menu.Items)
        {
            section.Padding = new Padding(9, 2, 9, 2);
            foreach (ToolStripItem action in section.DropDownItems)
                action.Font = new Font("Segoe UI", 10);
        }
        MainMenuStrip = menu;
        var quickNav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 75, Padding = new Padding(4, 7, 0, 5),
            WrapContents = false, AutoScroll = false, BackColor = left.BackColor };
        libraryBar = quickNav;
        foreach (string kind in new[] { "Canlı TV", "Film", "Dizi" })
        {
            var button = Button(kind, (_, _) => { kinds.SelectedItem = kind; });
            button.Width = 98; button.Height = 55;
            button.Margin = new Padding(1, 1, 5, 1);
            button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            button.FlatStyle = FlatStyle.Flat;
            libraryTabs.Add(kind, button);
            quickNav.Controls.Add(button);
        }
        kinds.SelectedIndexChanged += (_, _) => UpdateLibrarySelection();
        var libraryHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = left.BackColor
        };
        libraryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 75, 36, 30, 38, 39 })
            libraryHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Control[] headerControls = { quickNav, kinds, favoritesOnly, groups, filter };
        left.SuspendLayout();
        libraryHeader.SuspendLayout();
        for (int row = 0; row < headerControls.Length; row++)
        {
            var control = headerControls[row];
            control.Dock = DockStyle.Fill;
            control.Margin = row == 0 ? Padding.Empty : new Padding(2, 2, 2, 2);
            libraryHeader.Controls.Add(control, 0, row);
        }
        libraryHeader.ResumeLayout();
        // Give the list its own row. Dock order alone lets the header paint over
        // the list's first items on some display scaling settings.
        var libraryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        list.Dock = DockStyle.Fill;
        list.Margin = Padding.Empty;
        libraryLayout.Controls.Add(libraryHeader, 0, 0);
        libraryLayout.Controls.Add(librarySummary, 0, 1);
        libraryLayout.Controls.Add(list, 0, 2);
        left.Controls.Add(libraryLayout);
        left.ResumeLayout();
        UpdateLibraryCounts();
        top.Controls.Add(menu);
        top.Controls.SetChildIndex(menu, 0);
        top.SetFlowBreak(menu, true);
        top.Resize += (_, _) => menu.Width = Math.Max(100, top.ClientSize.Width - top.Padding.Horizontal - 25);
        menu.Width = Math.Max(100, top.ClientSize.Width - top.Padding.Horizontal - 25);
        top.Height = 175;
        foreach (var button in top.Controls.OfType<Button>().ToArray())
        {
            if (button.Text is "Durdur" or "Önceki" or "Sonraki" or "★ Favori" or "EPG" or
                "Ses / altyazı" or "Kaynağı kaydet" or "Kaynağı aç" or "Kaynağı sil" or
                "Dizilere dön" or "Kayıtlar" or "Altyazı dosyası" or "AI altyazı (10 dk)")
            {
                top.Controls.Remove(button);
                button.Dispose();
            }
        }
        ApplyMenuColors();
    }

    private void StopPlayback()
    {
        SavePlaybackPosition();
        StopLiveRecovery();
        player.Stop();
        status.Text = "Durduruldu.";
        cinemaPlayingView = false; UpdateCinemaMode();
    }

    private void OpenImdbSearch()
    {
        Channel? selected = list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count
            ? visible[list.SelectedIndex] : playingChannel;
        if (selected == null || selected.Kind is not ("Film" or "Dizi" or "Bölüm"))
        {
            MessageBox.Show(this, "Önce bir film veya dizi seçin.", "IMDb araması"); return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://www.imdb.com/find/?q=" + Uri.EscapeDataString(selected.Name)) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "IMDb araması"); }
    }

    private void ApplyMenuColors()
    {
        if (navigation == null) return;
        navigation.BackColor = top.BackColor;
        navigation.ForeColor = ForeColor;
        navigation.Renderer = new ToolStripProfessionalRenderer(new NavigationColors(top.BackColor, ForeColor));
        foreach (ToolStripMenuItem group in navigation.Items)
        {
            group.ForeColor = ForeColor;
            group.DropDown.BackColor = top.BackColor;
            foreach (ToolStripItem option in group.DropDownItems)
                option.ForeColor = ForeColor;
        }
        if (libraryBar != null) libraryBar.BackColor = left.BackColor;
        UpdateLibrarySelection();
    }

    private sealed class NavigationColors(Color background, Color foreground) : ProfessionalColorTable
    {
        private readonly Color hover = Color.FromArgb(
            Math.Min(255, background.R + 28), Math.Min(255, background.G + 28),
            Math.Min(255, background.B + 28));
        public override Color MenuStripGradientBegin => background;
        public override Color MenuStripGradientEnd => background;
        public override Color ToolStripDropDownBackground => background;
        public override Color ImageMarginGradientBegin => background;
        public override Color ImageMarginGradientMiddle => background;
        public override Color ImageMarginGradientEnd => background;
        public override Color MenuItemSelected => hover;
        public override Color MenuItemSelectedGradientBegin => hover;
        public override Color MenuItemSelectedGradientEnd => hover;
        public override Color MenuItemPressedGradientBegin => hover;
        public override Color MenuItemPressedGradientMiddle => hover;
        public override Color MenuItemPressedGradientEnd => hover;
        public override Color MenuItemBorder => foreground;
        public override Color MenuBorder => foreground;
    }
}

