using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibVLCSharp.Shared;

namespace JoyIptv;

internal sealed record SubtitleTools(string Ffmpeg, string WhisperCli, string Model);

internal sealed partial class PlayerForm
{
    private SubtitleTools? subtitleTools;
    private CancellationTokenSource? subtitleCts;
    private bool subtitleBusy;
    private string SubtitleToolsFile => Path.Combine(settingsDir, "altyazi_araclari.json");

    private void SetupSubtitles(FlowLayoutPanel toolbar)
    {
        toolbar.Controls.Add(Button("Altyazı dosyası", (_, _) => ImportSubtitle()));
        toolbar.Controls.Add(Button("AI altyazı (10 dk)", async (_, _) => await GenerateSubtitleAsync()));
        toolbar.Controls.Add(Button("AI araçlarını seç", (_, _) => ChooseSubtitleTools()));
        try
        {
            if (File.Exists(SubtitleToolsFile))
                subtitleTools = JsonSerializer.Deserialize<SubtitleTools>(File.ReadAllText(SubtitleToolsFile));
        }
        catch { /* Tools may be chosen again. */ }
    }

    private void ImportSubtitle()
    {
        if (playingChannel == null) { MessageBox.Show(this, "Önce yayını başlatın."); return; }
        using var dialog = new OpenFileDialog { Title = "Altyazı dosyası", Filter = "Altyazı|*.srt;*.vtt;*.ass|Tüm dosyalar|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AttachSubtitle(dialog.FileName);
    }

    private void AttachSubtitle(string path)
    {
        try
        {
            if (!player.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true))
                throw new InvalidOperationException("VLC altyazı dosyasını ekleyemedi.");
            status.Text = "Altyazı yüklendi: " + Path.GetFileName(path);
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Altyazı hatası"); }
    }

    private static string? PickTool(IWin32Window owner, string title, string filter)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    private bool ChooseSubtitleTools()
    {
        string? ffmpeg = PickTool(this, "FFmpeg uygulamasını seçin", "FFmpeg|ffmpeg.exe|EXE|*.exe");
        if (ffmpeg == null) return false;
        string? whisper = PickTool(this, "whisper-cli uygulamasını seçin", "whisper-cli|whisper-cli.exe|EXE|*.exe");
        if (whisper == null) return false;
        string? model = PickTool(this, "Whisper modelini seçin", "Whisper model|*.bin|Tüm dosyalar|*.*");
        if (model == null) return false;
        try
        {
            subtitleTools = new SubtitleTools(ffmpeg, whisper, model);
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(SubtitleToolsFile, JsonSerializer.Serialize(subtitleTools));
            status.Text = "Yerel AI altyazı araçları seçildi.";
            return true;
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Araç ayarları kaydedilemedi"); return false; }
    }

    private async Task GenerateSubtitleAsync()
    {
        if (subtitleBusy) { MessageBox.Show(this, "Altyazı işlemi sürüyor."); return; }
        if (playingChannel?.Kind is not ("Film" or "Bölüm") || currentStreamUrl.Length == 0)
        { MessageBox.Show(this, "AI altyazı, bu sürümde oynayan film ve dizi bölümleri içindir."); return; }
        if (subtitleTools == null || !File.Exists(subtitleTools.Ffmpeg) ||
            !File.Exists(subtitleTools.WhisperCli) || !File.Exists(subtitleTools.Model))
        { if (!ChooseSubtitleTools()) return; }
        subtitleBusy = true;
        subtitleCts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        long segmentStart = Math.Max(0, player.Time / 1000);
        string dir = Path.Combine(settingsDir, "altyazi");
        string prefix = Path.Combine(dir, (playingKey.Length > 0 ? playingKey : Guid.NewGuid().ToString("N")) + "_" + segmentStart);
        string wave = prefix + ".wav", generated = prefix + ".srt";
        try
        {
            Directory.CreateDirectory(dir);
            status.Text = "Ses hazırlanıyor (10 dakika)...";
            var ffmpegArgs = new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-ss",
                segmentStart.ToString(CultureInfo.InvariantCulture), "-i", currentStreamUrl,
                "-t", "600", "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wave };
            await RunLocalToolAsync(subtitleTools!.Ffmpeg, ffmpegArgs, subtitleCts.Token);
            if (!File.Exists(wave) || new FileInfo(wave).Length < 1024)
                throw new InvalidOperationException("Yayından ses çıkarılamadı.");
            status.Text = "Yerel konuşma modeli altyazı oluşturuyor...";
            await RunLocalToolAsync(subtitleTools.WhisperCli, new[] { "-m", subtitleTools.Model, "-f", wave,
                "-l", "auto", "-osrt", "-of", prefix }, subtitleCts.Token);
            if (!File.Exists(generated)) throw new InvalidOperationException("Whisper SRT dosyası üretmedi.");
            if (segmentStart > 0) ShiftSubtitleTimes(generated, TimeSpan.FromSeconds(segmentStart));
            if (!IsDisposed) AttachSubtitle(generated);
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Altyazı işlemi zaman aşımına uğradı veya iptal edildi."; }
        catch (Exception e) { if (!IsDisposed) MessageBox.Show(this, e.Message, "AI altyazı hatası"); }
        finally
        {
            try { if (File.Exists(wave)) File.Delete(wave); } catch { }
            subtitleCts?.Dispose(); subtitleCts = null; subtitleBusy = false;
        }
    }

    private static async Task RunLocalToolAsync(string executable, IEnumerable<string> args, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Yerel araç başlatılamadı.");
        Task error = process.StandardError.ReadToEndAsync();
        Task output = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); await Task.WhenAll(error, output); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException(Path.GetFileName(executable) + " hata kodu: " + process.ExitCode +
                ". Araç ve model dosyalarının birbiriyle uyumunu kontrol edin.");
    }

    private static void ShiftSubtitleTimes(string path, TimeSpan offset)
    {
        string text = File.ReadAllText(path);
        string shifted = Regex.Replace(text,
            @"(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})",
            m => FormatSrt(ParseSrt(m.Groups[1].Value) + offset) +
                " --> " + FormatSrt(ParseSrt(m.Groups[2].Value) + offset));
        File.WriteAllText(path, shifted);
    }

    private static TimeSpan ParseSrt(string time) => new(0, Int32.Parse(time[..2]), Int32.Parse(time[3..5]),
        Int32.Parse(time[6..8]), Int32.Parse(time[9..12]));

    private static string FormatSrt(TimeSpan time) => $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";

    private void CancelSubtitleJob() => subtitleCts?.Cancel();
}
