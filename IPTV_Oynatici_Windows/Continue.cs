using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;

namespace JoyIptv;

internal sealed record ContinueEntry(string Key, string SourceKey, Channel Item, DateTime Watched);

internal sealed partial class PlayerForm
{
    private readonly Dictionary<string, ContinueEntry> continueItems = new(StringComparer.Ordinal);
    private string ContinueFile => Path.Combine(settingsDir, "devam.dat");

    private void LoadContinueItems()
    {
        try
        {
            if (!File.Exists(ContinueFile)) return;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(ContinueFile), null, DataProtectionScope.CurrentUser);
            foreach (var item in JsonSerializer.Deserialize<List<ContinueEntry>>(plain) ?? new())
                if (item.Key.Length > 0 && item.Item.Kind is ("Film" or "Bölüm")) continueItems[item.Key] = item;
        }
        catch { status.Text = "İzlemeye devam listesi okunamadı."; }
    }

    private void SaveContinueItems()
    {
        try
        {
            Directory.CreateDirectory(settingsDir);
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(continueItems.Values
                .OrderByDescending(x => x.Watched).Take(100).ToList());
            string temp = ContinueFile + ".tmp";
            File.WriteAllBytes(temp, ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser));
            File.Move(temp, ContinueFile, true);
        }
        catch { /* Playback and resume positions remain available. */ }
    }

    private void RememberContinue(string key, Channel item)
    {
        continueItems[key] = new ContinueEntry(key, playingSourceKey, item, DateTime.UtcNow);
        if (continueItems.Count > 100)
        {
            foreach (var old in continueItems.Values.OrderByDescending(x => x.Watched).Skip(100).ToArray())
                continueItems.Remove(old.Key);
        }
        SaveContinueItems();
    }

    private void ForgetContinue(string key)
    {
        if (continueItems.Remove(key)) SaveContinueItems();
    }

    private void ShowContinueWatching()
    {
        if (activeSource < 0)
        { MessageBox.Show(this, "Önce kaynağı yükleyin.", "İzlemeye devam et"); return; }
        string sourceKey = CacheKey();
        var items = continueItems.Values.Where(x => x.SourceKey == sourceKey && resumePositions.ContainsKey(x.Key))
            .OrderByDescending(x => x.Watched).ToList();
        if (items.Count == 0)
        { MessageBox.Show(this, "Bu kaynakta devam edilecek film veya bölüm yok.", "İzlemeye devam et"); return; }
        using var window = new Form { Text = "İzlemeye devam et", Width = 650, Height = 410,
            StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var rows = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var remove = new Button { Text = "Seçili öğeyi listeden çıkar", Dock = DockStyle.Bottom, Height = 38 };
        void Render()
        {
            rows.Items.Clear();
            foreach (var item in items)
                rows.Items.Add(item.Item.Name + "  |  " +
                    TimeSpan.FromMilliseconds(resumePositions.GetValueOrDefault(item.Key)).ToString(@"hh\:mm\:ss") +
                    "  |  " + item.Watched.ToLocalTime().ToString("dd.MM HH:mm"));
        }
        remove.Click += (_, _) =>
        {
            if (rows.SelectedIndex < 0 || rows.SelectedIndex >= items.Count) return;
            var chosen = items[rows.SelectedIndex];
            items.RemoveAt(rows.SelectedIndex); resumePositions.Remove(chosen.Key);
            ForgetContinue(chosen.Key); SaveResumePositions(); Render();
        };
        rows.DoubleClick += async (_, _) =>
        {
            if (rows.SelectedIndex < 0 || rows.SelectedIndex >= items.Count) return;
            Channel item = items[rows.SelectedIndex].Item;
            Channel? existing = channels.FirstOrDefault(c => c == item);
            if (existing == null)
            {
                if (item.Kind == "Bölüm" && catalogBeforeEpisodes == null)
                    catalogBeforeEpisodes = channels.ToList();
                channels.Add(item); existing = item;
            }
            kinds.SelectedItem = item.Kind; groups.SelectedIndex = 0;
            favoritesOnly.Checked = false; filter.Clear();
            int index = visible.FindIndex(c => ReferenceEquals(c, existing));
            if (index < 0) return;
            list.SelectedIndex = index;
            window.Close();
            await PlaySelectedAsync();
        };
        window.Controls.Add(rows); window.Controls.Add(remove);
        Render(); window.ShowDialog(this);
    }
}
