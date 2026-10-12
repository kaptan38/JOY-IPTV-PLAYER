using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;

namespace JoyIptv;

internal sealed class RecordingJob
{
    public required string Name { get; init; }
    public required string File { get; init; }
    public required Process Process { get; init; }
    public DateTime Started { get; init; } = DateTime.Now;
    public bool Stopping { get; set; }
}

internal sealed partial class PlayerForm
{
    private readonly List<RecordingJob> recordings = new();
    private string? recordingFolder;
    private string RecordingFolderFile => Path.Combine(settingsDir, "kayit_klasoru.txt");
    private string RecorderExeFile => Path.Combine(settingsDir, "kayit_ffmpeg.txt");
    private string? recorderExe;
    private int startingRecordings;

    private void SetupRecordings(FlowLayoutPanel toolbar)
    {
        toolbar.Controls.Add(Button("Kayda al", async (_, _) => { await StartRecordingAsync(); }));
        toolbar.Controls.Add(Button("Kayıtlar", (_, _) => ShowRecordingManager()));
        try
        {
            if (File.Exists(RecordingFolderFile)) recordingFolder = File.ReadAllText(RecordingFolderFile);
            if (File.Exists(RecorderExeFile)) recorderExe = File.ReadAllText(RecorderExeFile);
        }
        catch { /* Paths can be chosen again. */ }
    }

    private string? SelectRecorder()
    {
        if (File.Exists(recorderExe)) return recorderExe;
        if (File.Exists(subtitleTools?.Ffmpeg)) return subtitleTools!.Ffmpeg;
        using var dialog = new OpenFileDialog { Title = "FFmpeg uygulamasını seçin", Filter = "FFmpeg|ffmpeg.exe|EXE|*.exe" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        recorderExe = dialog.FileName;
        try { Directory.CreateDirectory(settingsDir); File.WriteAllText(RecorderExeFile, recorderExe); } catch { }
        return recorderExe;
    }

    private string? SelectRecordingFolder()
    {
        if (Directory.Exists(recordingFolder)) return recordingFolder;
        using var dialog = new FolderBrowserDialog { Description = "Yayın kayıtlarının saklanacağı klasör" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        recordingFolder = dialog.SelectedPath;
        try { Directory.CreateDirectory(settingsDir); File.WriteAllText(RecordingFolderFile, recordingFolder); } catch { }
        return recordingFolder;
    }

    private async Task<RecordingJob?> StartRecordingAsync(Channel? scheduledChannel = null, bool unattended = false)
    {
        var channel = scheduledChannel ?? (list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count
            ? visible[list.SelectedIndex] : playingChannel);
        if (channel == null || channel.Kind != "Canlı TV")
        { if (!unattended) MessageBox.Show(this, "Kaydetmek için canlı TV kanalını seçin."); return null; }
        if (recordings.Count + startingRecordings >= 4)
        { if (!unattended) MessageBox.Show(this, "Aynı anda en fazla dört kayıt başlatılabilir."); return null; }
        string? exe = unattended ? (File.Exists(recorderExe) ? recorderExe : subtitleTools?.Ffmpeg) : SelectRecorder();
        string? folder = unattended ? recordingFolder : SelectRecordingFolder();
        if (exe == null || !File.Exists(exe) || folder == null || !Directory.Exists(folder)) return null;
        startingRecordings++;
        try
        {
            string url = await RecordingUrlAsync(channel);
            if (IsDisposed || Disposing) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Kaydedilebilir yayın adresi alınamadı.");
            string safeName = Regex.Replace(channel.Name, @"[^\p{L}\p{N} _-]", "_").Trim();
            if (safeName.Length == 0) safeName = "Yayin";
            if (safeName.Length > 60) safeName = safeName[..60];
            string output = Path.Combine(folder, safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                "_" + Guid.NewGuid().ToString("N")[..6] + ".mkv");
            var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-i", url,
                "-map", "0:v?", "-map", "0:a?", "-c", "copy", "-f", "matroska", output })
                start.ArgumentList.Add(arg);
            var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg başlatılamadı.");
            _ = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            var job = new RecordingJob { Name = channel.Name, File = output, Process = process };
            recordings.Add(job);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(() =>
                {
                    if (job.Stopping) return;
                    if (IsDisposed || !recordings.Remove(job)) return;
                    status.Text = process.ExitCode == 0 ? "Kayıt bitti: " + Path.GetFileName(output) :
                        "Kayıt hata koduyla durdu (" + process.ExitCode + "): " + channel.Name;
                    if (!job.Stopping) process.Dispose();
                }); } catch (InvalidOperationException) { }
            };
            status.Text = $"Kayıt başladı: {channel.Name} ({recordings.Count}/4)";
            return job;
        }
        catch (Exception e)
        {
            if (unattended) status.Text = "Zamanlanmış kayıt başlatılamadı: " + ExplainError(e);
            else MessageBox.Show(this, ExplainError(e), "Kayıt başlatılamadı");
            return null;
        }
        finally { startingRecordings--; }
    }

    private async Task<string> RecordingUrlAsync(Channel item)
    {
        if (activeSource != 3 || item.Command == null) return item.Url;
        using var doc = await StalkerRequestAsync("type=itv&action=create_link&cmd=" +
            Uri.EscapeDataString(item.Command) + "&JsHttpRequest=1-xml", true);
        string url = Str(doc.RootElement.GetProperty("js"), "cmd").Trim();
        return url.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase) ? url[7..].Trim() : url;
    }

    private void ShowRecordingManager()
    {
        using var window = new Form { Text = "Aktif yayın kayıtları", Width = 670, Height = 360,
            StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var rows = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var stop = new Button { Text = "Seçili kaydı durdur", Dock = DockStyle.Bottom, Height = 40 };
        var folder = new Button { Text = "Kayıt klasörünü aç", Dock = DockStyle.Bottom, Height = 36 };
        void Refresh()
        {
            int selected = rows.SelectedIndex;
            rows.Items.Clear();
            foreach (var job in recordings)
                rows.Items.Add(job.Name + "  |  " + (DateTime.Now - job.Started).ToString(@"hh\:mm\:ss") +
                    "  |  " + Path.GetFileName(job.File));
            if (selected >= 0 && selected < rows.Items.Count) rows.SelectedIndex = selected;
        }
        stop.Click += async (_, _) =>
        {
            if (rows.SelectedIndex < 0 || rows.SelectedIndex >= recordings.Count) return;
            await StopRecordingAsync(recordings[rows.SelectedIndex]); Refresh();
        };
        folder.Click += (_, _) =>
        {
            if (Directory.Exists(recordingFolder))
                Process.Start(new ProcessStartInfo(recordingFolder!) { UseShellExecute = true });
        };
        var refresh = new System.Windows.Forms.Timer { Interval = 1000 };
        refresh.Tick += (_, _) => Refresh(); refresh.Start();
        window.FormClosed += (_, _) => refresh.Dispose();
        window.Controls.Add(rows); window.Controls.Add(stop); window.Controls.Add(folder);
        Refresh(); window.ShowDialog(this);
    }

    private async Task StopRecordingAsync(RecordingJob job)
    {
        job.Stopping = true;
        try
        {
            if (!job.Process.HasExited)
            {
                await job.Process.StandardInput.WriteLineAsync("q");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await job.Process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { if (!job.Process.HasExited) job.Process.Kill(entireProcessTree: true); }
            }
        }
        catch (Exception e) { if (!IsDisposed && !Disposing) MessageBox.Show(this, e.Message, "Kayıt durdurulamadı"); }
        finally
        {
            recordings.Remove(job);
            job.Process.Dispose();
            if (!IsDisposed) status.Text = "Kayıt durduruldu: " + job.Name;
        }
    }

    private void StopAllRecordings()
    {
        foreach (var job in recordings.ToArray())
        {
            job.Stopping = true;
            try
            {
                if (!job.Process.HasExited)
                {
                    job.Process.StandardInput.WriteLine("q");
                    if (!job.Process.WaitForExit(2000)) job.Process.Kill(entireProcessTree: true);
                }
                job.Process.Dispose();
            }
            catch { }
        }
        recordings.Clear();
    }
}
