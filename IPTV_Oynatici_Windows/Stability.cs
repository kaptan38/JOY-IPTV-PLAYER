namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private int recoveryGeneration;
    private int recoveryAttempts;
    private bool recoveryPending;
    private bool recoveryPlayFailed;
    private DateTime bufferingSince = DateTime.MinValue;
    private readonly ComboBox liveBuffer = new() { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
    private string LiveBufferFile => Path.Combine(settingsDir, "yayin_tamponu.txt");

    private int SelectedLiveCacheMs => liveBuffer.SelectedIndex switch
    {
        0 => 1500,
        2 => 6000,
        _ => 3000
    };

    private void SetupLiveBuffer(FlowLayoutPanel settingsPanel)
    {
        liveBuffer.Items.AddRange(new object[] { "Canlı yayın: Hızlı (1,5 sn)",
            "Canlı yayın: Dengeli (3 sn)", "Canlı yayın: Kararlı (6 sn)" });
        int choice = 1;
        try { if (File.Exists(LiveBufferFile)) choice = Math.Clamp(int.Parse(File.ReadAllText(LiveBufferFile)), 0, 2); }
        catch { }
        liveBuffer.SelectedIndex = choice;
        liveBuffer.SelectedIndexChanged += (_, _) =>
        {
            try { Directory.CreateDirectory(settingsDir); File.WriteAllText(LiveBufferFile, liveBuffer.SelectedIndex.ToString()); }
            catch { }
        };
        settingsPanel.Controls.Add(liveBuffer);
    }

    private void ScheduleLiveRecovery()
    {
        if (IsDisposed || recoveryPending) return;
        var item = playingChannel;
        if (item == null) return;
        if (item.Kind != "Canlı TV") return;
        if (recoveryAttempts >= 3)
        {
            status.Text = "Yayın bağlantısı üç kez yenilendi; sağlayıcı erişimini kontrol edin.";
            return;
        }
        int generation = recoveryGeneration;
        int delay = (int)Math.Pow(2, recoveryAttempts + 1);
        recoveryAttempts++;
        recoveryPending = true;
        status.Text = $"Bağlantı kesildi; {delay} saniye içinde tekrar bağlanılıyor ({recoveryAttempts}/3).";
        _ = RecoverLiveAsync(item, generation, delay);
    }

    private async Task RecoverLiveAsync(Channel item, int generation, int delaySeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        if (IsDisposed || generation != recoveryGeneration || !ReferenceEquals(item, playingChannel)) return;
        recoveryPlayFailed = false;
        await PlaySelectedAsync(item);
        if (generation != recoveryGeneration) return;
        recoveryPending = false;
        if (recoveryPlayFailed && generation == recoveryGeneration) ScheduleLiveRecovery();
    }

    private void StopLiveRecovery()
    {
        recoveryGeneration++;
        recoveryAttempts = 0;
        recoveryPending = false;
        bufferingSince = DateTime.MinValue;
        playingChannel = null;
    }

    private void MonitorLivePlayback()
    {
        if (playingChannel?.Kind != "Canlı TV" || recoveryPending)
        { bufferingSince = DateTime.MinValue; return; }
        string state = player.State.ToString();
        if (state is not ("Buffering" or "Opening"))
        { bufferingSince = DateTime.MinValue; return; }
        if (bufferingSince == DateTime.MinValue) bufferingSince = DateTime.UtcNow;
        else if (DateTime.UtcNow - bufferingSince > TimeSpan.FromSeconds(25))
        { bufferingSince = DateTime.MinValue; ScheduleLiveRecovery(); }
    }
}
