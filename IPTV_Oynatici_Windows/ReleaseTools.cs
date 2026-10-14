using System.Diagnostics;

namespace JoyIptv;

internal static class ReleaseTools
{
    internal static async Task<string> BackupCurrentExecutableAsync(string directory, CancellationToken token)
    {
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("EXE yolu alınamadı.");
        if (!String.Equals(Path.GetFileName(source), "JOY_IPTV_Player.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Bu işlem kurulumsuz JOY IPTV EXE'sinde kullanılabilir.");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "JOY_IPTV_Player_" + ReleaseInfo.Version + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6] + ".exe");
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            await input.CopyToAsync(output, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false);
            return target;
        }
        catch { if (File.Exists(target)) File.Delete(target); throw; }
    }
}

internal sealed partial class PlayerForm
{
    private bool settingsOperation;
    private bool CanRestoreSettings => !settingsOperation && !loadingSource && stalkerCatalogTask.IsCompleted &&
        playbackOperations == 0 && playingChannel == null && !player.IsPlaying && recordings.Count == 0 && startingRecordings == 0;
    private void SetupReleaseSettings(FlowLayoutPanel panel)
    {
        panel.Controls.Add(new Label { Text = "JOY IPTV " + ReleaseInfo.Version + "\n" + ReleaseInfo.Changes,
            AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(4, 15, 4, 10) });
        panel.Controls.Add(Button("Ayar yedeğini dışa aktar", async (_, _) => await ExportSettingsAsync()));
        panel.Controls.Add(Button("Ayar yedeğini geri yükle", async (_, _) => await RestoreSettingsAsync()));
        panel.Controls.Add(Button("Güncelleme ve önceki EXE", async (_, _) => await PrepareManualUpdateAsync()));
        panel.Controls.Add(new Label { Text = "Ayar yedeği bu Windows hesabına bağlıdır. Eski EXE'ye dönmek ve ayar yedeğini geri yüklemek ayrı işlemlerdir.",
            AutoSize = true, MaximumSize = new Size(440, 0) });
    }
    private async Task ExportSettingsAsync()
    {
        if (settingsOperation) return;
        using var dialog = new SaveFileDialog { Filter = "JOY ayar yedeği|*.joybackup", FileName = "JOY_IPTV_ayarlar_" + DateTime.Now.ToString("yyyyMMdd") + ".joybackup" };
        var owner = Form.ActiveForm ?? this; if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        bool enabled = owner.Enabled, timer = playbackTimer.Enabled;
        settingsOperation = true; owner.Enabled = false; playbackTimer.Stop();
        try
        {
            SavePlaybackPosition(); await preferenceSaveTask; await recentSaveTask;
            await SettingsBackup.ExportAsync(settingsDir, dialog.FileName, CancellationToken.None);
            MessageBox.Show(owner, "Şifreli ayar yedeği kaydedildi.", "Ayar yedeği");
        }
        catch (Exception ex) { MessageBox.Show(owner, "Yedek alınamadı: " + DiagnosticLog.Sanitize(ex.Message, new[] { username.Text, password.Text }), "Ayar yedeği"); }
        finally { settingsOperation = false; if (!owner.IsDisposed) owner.Enabled = enabled; if (timer && !IsDisposed) playbackTimer.Start(); }
    }
    private async Task RestoreSettingsAsync()
    {
        var owner = Form.ActiveForm ?? this;
        if (!CanRestoreSettings)
        { MessageBox.Show(owner, "Önce yayını ve kayıtları durdurun; kaynak yüklemesinin bitmesini bekleyin.", "Geri yükleme"); return; }
        using var dialog = new OpenFileDialog { Filter = "JOY ayar yedeği|*.joybackup" };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        if (MessageBox.Show(owner, "Mevcut ayarların otomatik yedeği alınacak. Geri yükleme başarılı olursa JOY IPTV yeniden açılacak. Devam edilsin mi?",
            "Geri yükleme", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        if (!CanRestoreSettings) return;
        bool enabled = owner.Enabled, schedule = scheduleTimer.Enabled;
        settingsOperation = true; owner.Enabled = false; scheduleTimer.Stop();
        try
        {
            await preferenceSaveTask; await recentSaveTask;
            string previous = await SettingsBackup.RestoreAsync(dialog.FileName, settingsDir, CancellationToken.None);
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
            MessageBox.Show(owner, "Ayarlar geri yüklendi. Önceki ayar yedeği:\n" + previous, "Geri yükleme");
            Close(); Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(owner, "Geri yükleme tamamlanamadı: " + DiagnosticLog.Sanitize(ex.Message, new[] { username.Text, password.Text }), "Geri yükleme"); }
        finally { settingsOperation = false; if (!owner.IsDisposed) owner.Enabled = enabled; if (schedule && !IsDisposed) scheduleTimer.Start(); }
    }
    private async Task PrepareManualUpdateAsync()
    {
        if (settingsOperation) return;
        var owner = Form.ActiveForm ?? this; bool enabled = owner.Enabled;
        settingsOperation = true; owner.Enabled = false;
        try
        {
            string backup = await ReleaseTools.BackupCurrentExecutableAsync(Path.Combine(settingsDir, "yedekler", "uygulama"), CancellationToken.None);
            MessageBox.Show(owner, "Önceki EXE saklandı:\n" + backup + "\n\nYeni EXE'yi resmî GitHub sayfasından indirin. Programı kapattıktan sonra dosyayı elle değiştirin. Ayarlar korunur.", "Güncelleme");
            Process.Start(new ProcessStartInfo("https://github.com/kaptan38/JOY-IPTV-PLAYER") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(owner, "EXE yedeklenemedi: " + ex.Message, "Güncelleme"); }
        finally { settingsOperation = false; if (!owner.IsDisposed) owner.Enabled = enabled; }
    }
}
