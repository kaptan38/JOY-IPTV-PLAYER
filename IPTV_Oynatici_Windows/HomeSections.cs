using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JoyIptv;

internal sealed record HomeItem(Channel Item, string SourceKey, DateTime Watched, long ResumeMs);
internal sealed record HomeSection(string Title, IReadOnlyList<HomeItem> Items);

internal sealed partial class PlayerForm
{
    private readonly Dictionary<string, ContinueEntry> recentItems = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim recentWriteGate = new(1, 1);
    private Task recentSaveTask = Task.CompletedTask;
    private string loadedSourceKey = "", selectedHomeSection = "";
    private IReadOnlyList<HomeSection> homeSections = Array.Empty<HomeSection>();
    private readonly Dictionary<string, Button> homeButtons = new();
    private FlowLayoutPanel? cinemaHomeBar;
    private string RecentFile => Path.Combine(settingsDir, "son_izlenenler.dat");
    private void LoadRecentItems()
    {
        try
        {
            if (!File.Exists(RecentFile)) return;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(RecentFile), null, DataProtectionScope.CurrentUser);
            foreach (var entry in JsonSerializer.Deserialize<List<ContinueEntry>>(plain) ?? new()) recentItems[entry.Key] = entry;
        }
        catch { /* Missing history leaves the home sections empty. */ }
    }
    private void RememberRecent(Channel item)
    {
        string key = FavoriteKey(item);
        recentItems[key] = new ContinueEntry(key, playingSourceKey, item, DateTime.UtcNow);
        foreach (var old in recentItems.Values.OrderByDescending(x => x.Watched).Skip(100).ToArray()) recentItems.Remove(old.Key);
        var snapshot = recentItems.Values.ToArray(); recentSaveTask = SaveRecentItemsAsync(snapshot);
    }
    private async Task SaveRecentItemsAsync(ContinueEntry[] snapshot)
    {
        string path = RecentFile;
        try
        {
            await recentWriteGate.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    byte[] plain = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                    string temp = path + ".tmp";
                    File.WriteAllBytes(temp, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); File.Move(temp, path, true);
                });
            }
            finally { recentWriteGate.Release(); }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private IReadOnlyList<HomeSection> BuildHomeSections() => BuildHomeSections(
        new LibrarySnapshot(channels.ToArray(), FavoritePrefix, new HashSet<string>(favorites), new Dictionary<string, int>(watchCounts), libraryCatalogVersion),
        loadedSourceKey.Length > 0 ? loadedSourceKey : CacheKey(), recentItems.Values.ToArray(), continueItems.Values.ToArray(),
        new Dictionary<string, long>(resumePositions), CancellationToken.None);

    private static IReadOnlyList<HomeSection> BuildHomeSections(LibrarySnapshot snapshot, string sourceKey,
        ContinueEntry[] recent, ContinueEntry[] continues, Dictionary<string, long> positions, CancellationToken token)
    {
        var lookup = new Dictionary<(string Kind, string Name, string Address), Channel>();
        var favoriteItems = new List<HomeItem>();
        foreach (var item in snapshot.Items)
        {
            token.ThrowIfCancellationRequested(); lookup.TryAdd((item.Kind, item.Name, item.Command ?? item.Url), item);
            if (snapshot.FavoriteIds.Count > 0 && snapshot.FavoriteIds.Contains(LibrarySearch.FavoriteId(snapshot.FavoritePrefix, item)))
                favoriteItems.Add(new HomeItem(item, sourceKey, default, 0));
        }
        HomeItem? Match(ContinueEntry entry, bool allowEpisode)
        {
            if (entry.SourceKey != sourceKey) return null;
            if (!lookup.TryGetValue((entry.Item.Kind, entry.Item.Name, entry.Item.Command ?? entry.Item.Url), out var current))
            { if (!allowEpisode || !(entry.Item.Kind == "Bölüm" || (entry.Item.Kind == "Dizi" && String.IsNullOrEmpty(entry.Item.Command)))) return null; current = entry.Item; }
            return new HomeItem(current, sourceKey, entry.Watched, positions.GetValueOrDefault(entry.Key));
        }
        var last = recent.Concat(continues).Where(x => x.SourceKey == sourceKey).OrderByDescending(x => x.Watched).DistinctBy(x => x.Key)
            .Select(x => Match(x, true)).Where(x => x != null).Cast<HomeItem>().Take(30).ToArray();
        var resume = continues.Where(x => positions.GetValueOrDefault(x.Key) > 0).OrderByDescending(x => x.Watched)
            .Select(x => Match(x, true)).Where(x => x != null).Cast<HomeItem>().Take(100).ToArray();
        return new[] { new HomeSection("Son izlenenler", last), new HomeSection("İzlemeye devam", resume), new HomeSection("Favoriler", favoriteItems) };
    }
    private void SetupHomeNavigation(TableLayoutPanel content)
    {
        cinemaHomeBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 4) };
        foreach (string title in new[] { "Kütüphane", "Son izlenenler", "İzlemeye devam", "Favoriler" })
        {
            string section = title == "Kütüphane" ? "" : title;
            var button = Button(title, (_, _) => { selectedHomeSection = section; ApplyFilter(); });
            button.AccessibleName = title; button.Tag = title; button.AutoSize = false; button.Size = new Size(156, 36);
            homeButtons[section] = button; cinemaHomeBar.Controls.Add(button);
        }
        cinemaHomeBar.Controls.Add(Button("Diğer kaynaklar", (_, _) => ShowOtherHomeSources()));
        content.Controls.Add(cinemaHomeBar, 0, 2);
    }
    private void RefreshHomeNavigation()
    {
        if (cinemaHomeBar?.Parent is not TableLayoutPanel content) return;
        bool show = kinds.SelectedItem as string == "Tümü" && !cinemaPlayingView && !favoritesOnly.Checked && filter.Text.Trim().Length == 0;
        cinemaHomeBar.Visible = show; content.RowStyles[2].Height = show ? (int)(60 * InterfaceScale) : 0;
        foreach (var pair in homeButtons)
        {
            var section = homeSections.FirstOrDefault(s => s.Title == pair.Key);
            pair.Value.Text = pair.Value.Tag + (section == null ? "" : " · " + section.Items.Count);
            pair.Value.BackColor = selectedHomeSection == pair.Key ? selectionColor : top.BackColor;
            pair.Value.ForeColor = selectedHomeSection == pair.Key ? Color.FromArgb(14, 40, 46) : ForeColor;
        }
    }
    private void ShowOtherHomeSources()
    {
        string current = loadedSourceKey.Length > 0 ? loadedSourceKey : CacheKey();
        var menu = new ContextMenuStrip();
        foreach (var group in continueItems.Values.Where(x => x.SourceKey != current && resumePositions.ContainsKey(x.Key)).GroupBy(x => x.SourceKey))
        {
            var profile = savedSources.FirstOrDefault(s => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                s.Type + "|" + s.Address.Trim() + "|" + s.Username.Trim() + "|" + s.Password))) == group.Key);
            if (profile == null) continue;
            var option = menu.Items.Add(profile.Name + " · " + group.Count());
            option.Click += async (_, _) =>
            {
                if (MessageBox.Show(this, $"İzlemeye devam etmek için '{profile.Name}' kaynağı yüklensin mi?", "Kaynak seçimi", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                int index = savedSources.IndexOf(profile); if (index < 0) return;
                savedSourceBox.SelectedIndex = index; OpenSource(); await LoadAsync();
                selectedHomeSection = "İzlemeye devam"; kinds.SelectedItem = "Tümü"; ApplyFilter();
            };
        }
        if (menu.Items.Count == 0) menu.Items.Add("Diğer kaynaklarda devam kaydı yok").Enabled = false;
        menu.Closed += (_, _) => menu.Dispose(); menu.Show(cinemaHomeBar!, new Point(0, cinemaHomeBar!.Height));
    }
}
