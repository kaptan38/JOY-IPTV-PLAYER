using System.Text.Json;
using System.Diagnostics;

namespace JoyIptv;

internal enum PosterSize { Small, Medium, Large }
internal sealed record CinemaPreferences(bool CompactRail, PosterSize PosterSize, bool Animations)
{
    private static readonly SemaphoreSlim writes = new(1, 1);
    internal static CinemaPreferences Load(string directory)
    {
        try
        { var value = JsonSerializer.Deserialize<CinemaPreferences>(File.ReadAllText(Path.Combine(directory, "gorunum.json")));
          if (value != null && Enum.IsDefined(value.PosterSize)) return value; }
        catch (IOException) { } catch (JsonException) { } catch (UnauthorizedAccessException) { }
        return new CinemaPreferences(false, JoyIptv.PosterSize.Medium, false);
    }
    internal async Task SaveAsync(string directory, CancellationToken token)
    {
        await writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "gorunum.json"), temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, path, true);
            }, token).ConfigureAwait(false);
        }
        finally { writes.Release(); }
    }
}

internal sealed partial class PlayerForm
{
    private CinemaPreferences cinemaPreferences = CinemaPreferences.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JOY_IPTV_Player"));
    private System.Windows.Forms.Timer? railAnimation;
    private Button? cinemaCollapse;
    private CheckBox? compactRailPreference;
    private bool syncingCinemaPreferences;
    private Task preferenceSaveTask = Task.CompletedTask;
    private void SetupCinemaPreferences(FlowLayoutPanel panel)
    {
        var size = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
        size.Items.AddRange(new object[] { "Afişler: Küçük", "Afişler: Orta", "Afişler: Büyük" });
        size.SelectedIndex = (int)cinemaPreferences.PosterSize;
        var compact = new CheckBox { Text = "Sol menüyü daralt", AutoSize = true, Checked = cinemaPreferences.CompactRail };
        compactRailPreference = compact;
        var animations = new CheckBox { Text = "Kısa menü geçişi", AutoSize = true, Checked = cinemaPreferences.Animations };
        size.SelectedIndexChanged += (_, _) => UpdateCinemaPreferences(cinemaPreferences with { PosterSize = (PosterSize)size.SelectedIndex });
        compact.CheckedChanged += (_, _) => { if (!syncingCinemaPreferences) UpdateCinemaPreferences(cinemaPreferences with { CompactRail = compact.Checked }); };
        animations.CheckedChanged += (_, _) => UpdateCinemaPreferences(cinemaPreferences with { Animations = animations.Checked });
        panel.Controls.Add(size); panel.Controls.Add(compact); panel.Controls.Add(animations);
        panel.Controls.Add(new Label { Text = "Afişler 7 gün önbellekte tutulur. Disk bütçesi: 256 MB.", AutoSize = true, MaximumSize = new Size(440, 0) });
        FormClosed += (_, _) => { railAnimation?.Stop(); railAnimation?.Dispose(); };
    }
    private void UpdateCinemaPreferences(CinemaPreferences value)
    {
        cinemaPreferences = value; ApplyCinemaPreferences(value.Animations);
        preferenceSaveTask = SaveCinemaPreferencesAsync(value);
    }
    private async Task SaveCinemaPreferencesAsync(CinemaPreferences value)
    {
        try { await value.SaveAsync(settingsDir, CancellationToken.None); }
        catch (IOException) { status.Text = "Görünüm tercihi kaydedilemedi."; }
        catch (UnauthorizedAccessException) { status.Text = "Görünüm tercihi kaydedilemedi."; }
    }
    private void ApplyCinemaPreferences(bool animate = false)
    {
        if (cinemaRoot == null) return;
        syncingCinemaPreferences = true;
        if (compactRailPreference != null) compactRailPreference.Checked = cinemaPreferences.CompactRail;
        syncingCinemaPreferences = false;
        railAnimation?.Stop(); railAnimation?.Dispose(); railAnimation = null;
        float scale = DeviceDpi / 96f;
        float target = (cinemaPreferences.CompactRail ? 76 : 180) * scale;
        foreach (var button in cinemaAllRail)
        {
            string original = button.Tag as string ?? button.Text;
            button.Width = (int)((cinemaPreferences.CompactRail ? 46 : 145) * scale);
            button.Text = cinemaPreferences.CompactRail ? original.Split(' ')[0] : original;
            button.TextAlign = cinemaPreferences.CompactRail ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
        }
        if (cinemaCollapse != null) cinemaCollapse.Text = cinemaPreferences.CompactRail ? "☰" : "☰  JOY IPTV";
        if (cinemaGallery != null) cinemaGallery.PosterSize = cinemaPreferences.PosterSize;
        if (!animate || !IsHandleCreated)
        { cinemaRoot.ColumnStyles[0].Width = target; return; }
        float start = cinemaRoot.ColumnStyles[0].Width; var watch = Stopwatch.StartNew();
        railAnimation = new System.Windows.Forms.Timer { Interval = 15 };
        var timer = railAnimation;
        timer.Tick += (_, _) =>
        {
            float progress = Math.Min(1f, (float)watch.Elapsed.TotalMilliseconds / 100f);
            cinemaRoot.ColumnStyles[0].Width = start + (target - start) * progress;
            if (progress >= 1f) { timer.Stop(); timer.Dispose(); if (ReferenceEquals(railAnimation, timer)) railAnimation = null; }
        };
        timer.Start();
    }
}
