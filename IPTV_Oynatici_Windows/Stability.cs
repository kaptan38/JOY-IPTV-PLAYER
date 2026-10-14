namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private int recoveryGeneration;
    private readonly RecoveryPolicy recoveryPolicy = new();
    private CancellationTokenSource? recoveryCancellation;
    private DateTime stableSince = DateTime.MinValue;
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
        3 => recoveryPolicy.AutomaticCacheMs,
        _ => 3000
    };

    private void SetupLiveBuffer(FlowLayoutPanel settingsPanel)
    {
        liveBuffer.Items.AddRange(new object[] { "Canlı yayın: Hızlı (1,5 sn)",
            "Canlı yayın: Dengeli (3 sn)", "Canlı yayın: Kararlı (6 sn)", "Canlı yayın: Otomatik (1,5–9 sn)" });
        int choice = 3;
        try { if (File.Exists(LiveBufferFile)) choice = Math.Clamp(int.Parse(File.ReadAllText(LiveBufferFile)), 0, 3); }
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
        if (IsDisposed || recoveryPending || playbackOperations > 0 || loadingSource) return;
        var item = playingChannel;
        if (item == null) return;
        if (item.Kind != "Canlı TV") return;
        int generation = recoveryGeneration;
        var attempt = recoveryPolicy.TrySchedule(generation);
        if (attempt == null)
        { status.Text = "Yayın bağlantısı üç kez yenilendi; sağlayıcı erişimini kontrol edin."; return; }
        int delay = attempt.DelaySeconds; recoveryAttempts = attempt.Number;
        recoveryCancellation ??= new CancellationTokenSource();
        _ = LogDiagnosticAsync("recovery", $"attempt={attempt.Number}; cache={SelectedLiveCacheMs}");
        recoveryPending = true;
        status.Text = $"Bağlantı kesildi; {delay} saniye içinde tekrar bağlanılıyor ({recoveryAttempts}/3).";
        _ = RecoverLiveAsync(item, generation, delay);
    }

    private void QueueLiveRecovery()
    {
        int generation = recoveryGeneration; var item = playingChannel;
        if (!IsHandleCreated || IsDisposed || item == null || playbackOperations > 0 || loadingSource) return;
        try
        {
            BeginInvoke(() =>
            { if (!IsDisposed && generation == recoveryGeneration && ReferenceEquals(item, playingChannel)) ScheduleLiveRecovery(); });
        }
        catch (InvalidOperationException) { /* Window closed while VLC delivered an event. */ }
    }

    private async Task RecoverLiveAsync(Channel item, int generation, int delaySeconds)
    {
        var token = recoveryCancellation?.Token ?? CancellationToken.None;
        try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token); }
        catch (OperationCanceledException) { return; }
        if (IsDisposed || generation != recoveryGeneration || !ReferenceEquals(item, playingChannel)) return;
        recoveryPlayFailed = false;
        await PlaySelectedAsync(item);
        if (generation != recoveryGeneration) return;
        recoveryPending = false;
        if (recoveryPlayFailed && generation == recoveryGeneration) ScheduleLiveRecovery();
    }

    private void StopLiveRecovery()
    {
        recoveryCancellation?.Cancel(); recoveryCancellation?.Dispose(); recoveryCancellation = null;
        recoveryGeneration++; recoveryPolicy.Reset(recoveryGeneration); stableSince = DateTime.MinValue;
        recoveryAttempts = 0;
        recoveryPending = false;
        bufferingSince = DateTime.MinValue;
        playingChannel = null;
    }

    private void MonitorLivePlayback()
    {
        if (playingChannel?.Kind != "Canlı TV" || recoveryPending || playbackOperations > 0 || loadingSource)
        { bufferingSince = DateTime.MinValue; return; }
        string state = player.State.ToString();
        if (state is not ("Buffering" or "Opening"))
        {
            if (bufferingSince != DateTime.MinValue)
                recoveryPolicy.ObserveBuffering(DateTime.UtcNow - bufferingSince);
            bufferingSince = DateTime.MinValue;
            if (state == "Playing")
            { if (stableSince == DateTime.MinValue) stableSince = DateTime.UtcNow;
              recoveryPolicy.ObserveStable(DateTime.UtcNow - stableSince); }
            else stableSince = DateTime.MinValue;
            return;
        }
        stableSince = DateTime.MinValue;
        if (bufferingSince == DateTime.MinValue) bufferingSince = DateTime.UtcNow;
        else if (DateTime.UtcNow - bufferingSince > TimeSpan.FromSeconds(25))
        { recoveryPolicy.ObserveBuffering(DateTime.UtcNow - bufferingSince); bufferingSince = DateTime.MinValue; ScheduleLiveRecovery(); }
    }
}
