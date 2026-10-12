using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;

namespace JoyIptv;

internal sealed record ScheduledRecording(Guid Id, string SourceKey, Channel Channel, DateTime StartUtc, int Minutes);

internal sealed partial class PlayerForm
{
    private readonly List<ScheduledRecording> schedules = new();
    private readonly System.Windows.Forms.Timer scheduleTimer = new() { Interval = 15000 };
    private bool scheduleBusy;
    private string SchedulesFile => Path.Combine(settingsDir, "zamanli_kayit.dat");

    private void SetupSchedules()
    {
        try
        {
            if (File.Exists(SchedulesFile))
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(SchedulesFile), null, DataProtectionScope.CurrentUser);
                schedules.AddRange(JsonSerializer.Deserialize<List<ScheduledRecording>>(plain) ?? new());
            }
        }
        catch { status.Text = "Zamanlanmış kayıtlar okunamadı."; }
        scheduleTimer.Tick += async (_, _) => await CheckSchedulesAsync();
        scheduleTimer.Start();
    }

    private void PersistSchedules()
    {
        Directory.CreateDirectory(settingsDir);
        string temp = SchedulesFile + ".tmp";
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(schedules);
        File.WriteAllBytes(temp, ProtectedData.Protect(content, null, DataProtectionScope.CurrentUser));
        File.Move(temp, SchedulesFile, true);
    }

    private void AddScheduledRecording()
    {
        Channel? channel = list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count
            ? visible[list.SelectedIndex] : playingChannel;
        if (activeSource < 0 || channel?.Kind != "Canlı TV")
        { MessageBox.Show(this, "Önce bir kaynak yükleyip canlı kanal seçin.", "Zamanlanmış kayıt"); return; }
        if (SelectRecorder() == null || SelectRecordingFolder() == null) return;
        using var dialog = new Form { Text = "Zamanlanmış kayıt: " + channel.Name,
            Width = 400, Height = 225, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var whenLabel = new Label { Text = "Başlangıç (bilgisayar saati)", Left = 15, Top = 14, Width = 350 };
        var when = new DateTimePicker { Left = 15, Top = 42, Width = 350,
            Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy HH:mm",
            Value = DateTime.Now.AddMinutes(10) };
        var durationLabel = new Label { Text = "Süre (dakika)", Left = 15, Top = 78, Width = 170 };
        var duration = new NumericUpDown { Left = 200, Top = 76, Width = 120,
            Minimum = 1, Maximum = 480, Value = 60 };
        var save = new Button { Text = "Planla", Left = 260, Top = 125, Width = 105, DialogResult = DialogResult.OK };
        dialog.Controls.AddRange(new Control[] { whenLabel, when, durationLabel, duration, save });
        dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (when.Value < DateTime.Now.AddSeconds(30) || when.Value > DateTime.Now.AddDays(7))
        { MessageBox.Show(this, "30 saniye ile 7 gün arasındaki bir zamanı seçin."); return; }
        var item = new ScheduledRecording(Guid.NewGuid(), CacheKey(), channel,
            when.Value.ToUniversalTime(), (int)duration.Value);
        try { schedules.Add(item); PersistSchedules(); status.Text = "Kayıt planlandı: " + channel.Name; }
        catch (Exception e) { schedules.Remove(item); MessageBox.Show(this, ExplainError(e), "Plan kaydedilemedi"); }
    }

    private void ShowSchedules()
    {
        using var window = new Form { Text = "Zamanlanmış kayıtlar", Width = 660, Height = 370,
            StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var rows = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var cancel = new Button { Text = "Seçili planı iptal et", Dock = DockStyle.Bottom, Height = 38 };
        var shown = new List<ScheduledRecording>();
        void Refresh()
        {
            shown = schedules.OrderBy(x => x.StartUtc).ToList(); rows.Items.Clear();
            foreach (var item in shown)
                rows.Items.Add(item.StartUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm") +
                    "  |  " + item.Channel.Name + "  |  " + item.Minutes + " dk");
        }
        cancel.Click += (_, _) =>
        {
            if (rows.SelectedIndex < 0 || rows.SelectedIndex >= shown.Count) return;
            var item = shown[rows.SelectedIndex];
            try { schedules.Remove(item); PersistSchedules(); Refresh(); }
            catch (Exception e) { schedules.Add(item); MessageBox.Show(this, ExplainError(e), "İptal kaydedilemedi"); }
        };
        window.Controls.Add(rows); window.Controls.Add(cancel);
        Refresh(); window.ShowDialog(this);
    }

    private async Task CheckSchedulesAsync()
    {
        if (scheduleBusy || IsDisposed || Disposing) return;
        scheduleBusy = true;
        try
        {
            foreach (var item in schedules.Where(x => x.StartUtc <= DateTime.UtcNow)
                .OrderBy(x => x.StartUtc).ToArray())
            {
                if (DateTime.UtcNow - item.StartUtc > TimeSpan.FromMinutes(5))
                {
                    schedules.Remove(item); PersistSchedules();
                    status.Text = "Kayıt zamanı kaçırıldı: " + item.Channel.Name;
                    continue;
                }
                if (activeSource < 0 || CacheKey() != item.SourceKey ||
                    recordings.Count + startingRecordings >= 4) continue;
                RecordingJob? job = await StartRecordingAsync(item.Channel, unattended: true);
                if (job == null) continue;
                _ = StopScheduledJobAsync(job, item.Minutes);
                schedules.Remove(item); PersistSchedules();
            }
        }
        catch (Exception e) { if (!IsDisposed && !Disposing) status.Text = "Zamanlanmış kayıt: " + ExplainError(e); }
        finally { scheduleBusy = false; }
    }

    private async Task StopScheduledJobAsync(RecordingJob job, int minutes)
    {
        await Task.Delay(TimeSpan.FromMinutes(minutes));
        if (!IsDisposed && !Disposing && recordings.Contains(job)) await StopRecordingAsync(job);
    }
}
