using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

internal static class ReviewTests
{
    const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object f, string n) => f.GetType().GetField(n, P)!.GetValue(f)!;
    static void Set(object f, string n, object? v) => f.GetType().GetField(n, P)!.SetValue(f, v);
    static object? Call(object f, string n, params object[] args) => f.GetType().GetMethod(n, P)!.Invoke(f, args);
    static Form Form(Assembly a) => (Form)Activator.CreateInstance(a.GetType("JoyIptv.PlayerForm")!, true)!;
    static object Channel(Assembly a, string name, string kind, string group = "Ulusal", string? command = null) =>
        a.GetType("JoyIptv.Channel")!.GetConstructors()[0].Invoke(new object?[] { name, group, "http://127.0.0.1/shared", kind, command, 0, 0, "", "", "" });
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
    static void Pump(int ms = 100)
    { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(2); } }
    static void Wait(Task t)
    { var end = DateTime.UtcNow.AddSeconds(20); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(5); Check(t.IsCompleted, "Review test timed out."); t.GetAwaiter().GetResult(); }
    public static void ReadOnlyRollback(Assembly a)
    {
        var t = a.GetType("JoyIptv.SettingsBackup")!; var flags = BindingFlags.Static | BindingFlags.NonPublic;
        string root = Path.Combine(Path.GetTempPath(), "joy-review-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string keep = Path.Combine(root, "a.txt"), changed = Path.Combine(root, "z.txt"), added = Path.Combine(root, "n.txt"), archive = Path.Combine(root, "test.joybackup");
        try
        {
            File.WriteAllText(changed, "archive"); File.WriteAllText(added, "new-file");
            ((Task)t.GetMethod("ExportAsync", flags)!.Invoke(null, new object[] { root, archive, CancellationToken.None })!).GetAwaiter().GetResult();
            File.Delete(added); File.WriteAllText(changed, "keep-original"); File.WriteAllText(keep, "readonly"); File.SetAttributes(keep, FileAttributes.ReadOnly);
            try { ((Task)t.GetMethod("RestoreAsync", flags)!.Invoke(null, new object[] { archive, root, CancellationToken.None })!).GetAwaiter().GetResult(); throw new Exception("FAIL: read-only restore unexpectedly succeeded."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Check(File.ReadAllText(changed) == "keep-original" && File.ReadAllText(keep) == "readonly" && !File.Exists(added),
                "Failed restore left changed or newly created settings behind.");
            Console.WriteLine("PASS: read-only deletion failure rolls back every successful mutation.");
        }
        finally { if (File.Exists(keep)) File.SetAttributes(keep, FileAttributes.Normal); Directory.Delete(root, true); }
    }
    public static void Profiles(Assembly a)
    {
        using var f = Form(a); string root = Path.Combine(Path.GetTempPath(), "joy-profiles-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Set(f, "settingsDir", root); ((IDictionary)Field(f, "recentItems")).Clear(); ((IDictionary)Field(f, "continueItems")).Clear(); ((IDictionary)Field(f, "resumePositions")).Clear();
        var mode = (ComboBox)Field(f, "source"); var address = (TextBox)Field(f, "address"); var user = (TextBox)Field(f, "username"); var pass = (TextBox)Field(f, "password");
        try
        {
            mode.SelectedIndex = 2; Set(f, "activeSource", 2); address.Text = "http://127.0.0.1";
            var live = Channel(a, "Shared live", "Canlı TV", command: "101");
            user.Text = "account-a"; pass.Text = "test-a"; Call(f, "PrepareResume", live);
            string favorite = (string)Call(f, "FavoriteKey", live)!; Call(f, "RememberRecent", live); Wait((Task)Field(f, "recentSaveTask"));
            user.Text = "account-b"; pass.Text = "test-b"; Call(f, "PrepareResume", live); Call(f, "RememberRecent", live); Wait((Task)Field(f, "recentSaveTask"));
            Check((string)Call(f, "FavoriteKey", live)! == favorite, "Legacy favorite identity changed.");
            ((IDictionary)Field(f, "recentItems")).Clear(); Call(f, "LoadRecentItems");
            Check(((IDictionary)Field(f, "recentItems")).Count == 2, "Same-server accounts overwrite persisted recent history.");
            mode.SelectedIndex = 3; Set(f, "activeSource", 3); address.Text = "http://127.0.0.1/portal"; Set(f, "stalkerBase", "http://127.0.0.1/portal.php"); pass.Clear();
            var movie = Channel(a, "Shared Stalker movie", "Film", command: "ffmpeg http://localhost/ch/1");
            user.Text = "00:1A:79:00:00:01"; Call(f, "PrepareResume", movie); string first = (string)Field(f, "playingKey");
            ((IDictionary)Field(f, "resumePositions"))[first] = 45000L; Call(f, "RememberContinue", first, movie);
            user.Text = "00:1A:79:00:00:02"; Call(f, "PrepareResume", movie);
            Check((long)Field(f, "pendingResume") == 0, "Stalker account inherited another account's resume position.");
            string second = (string)Field(f, "playingKey"); ((IDictionary)Field(f, "resumePositions"))[second] = 90000L; Call(f, "RememberContinue", second, movie); Call(f, "SaveResumePositions");
            ((IDictionary)Field(f, "continueItems")).Clear(); ((IDictionary)Field(f, "resumePositions")).Clear(); Call(f, "LoadResumePositions"); Call(f, "LoadContinueItems");
            user.Text = "00:1A:79:00:00:01"; Call(f, "PrepareResume", movie); Check((long)Field(f, "pendingResume") == 45000L, "First resume lost on reload.");
            user.Text = "00:1A:79:00:00:02"; Call(f, "PrepareResume", movie); Check((long)Field(f, "pendingResume") == 90000L, "Second resume lost on reload.");
            var entries = (IDictionary)Field(f, "continueItems"); var positions = (IDictionary)Field(f, "resumePositions");
            entries.Clear(); positions.Clear();
            string legacy = (string)Call(f, "FavoriteKey", movie)!;
            string sourceKey = (string)Field(f, "playingSourceKey");
            entries[legacy] = a.GetType("JoyIptv.ContinueEntry")!.GetConstructors()[0].Invoke(new object[] { legacy, sourceKey, movie, DateTime.UtcNow });
            positions[legacy] = 123000L; Call(f, "SaveContinueItems"); Call(f, "SaveResumePositions");
            entries.Clear(); positions.Clear(); Call(f, "LoadResumePositions"); Call(f, "LoadContinueItems"); Call(f, "PrepareResume", movie);
            Check((long)Field(f, "pendingResume") == 123000L, "Legacy continue position was not migrated.");
            entries.Clear(); positions.Clear(); Call(f, "LoadResumePositions"); Call(f, "LoadContinueItems"); Call(f, "PrepareResume", movie);
            Check((long)Field(f, "pendingResume") == 123000L, "Migrated position changed on second reload.");
            Console.WriteLine("PASS: source-qualified history and resume persist independently; favorites remain compatible.");
        }
        finally { Wait((Task)Field(f, "recentSaveTask")); Directory.Delete(root, true); }
    }
    public static void PendingRecovery(Assembly a)
    {
        using var f = Form(a); _ = f.Handle;
        var old = Channel(a, "Old", "Canlı TV"); var next = Channel(a, "New", "Canlı TV");
        Set(f, "playingChannel", old); Set(f, "recoveryGeneration", 10);
        var policy = Field(f, "recoveryPolicy"); policy.GetType().GetMethod("Reset", P)!.Invoke(policy, new object[] { 10 }); Set(f, "playbackOperations", 1);
        try
        {
            Call(f, "QueueLiveRecovery"); Pump();
            Check(!(bool)Field(f, "recoveryPending") && (int)Field(f, "recoveryAttempts") == 0,
                "Old media event consumes new selection recovery budget during URL resolution.");
            Set(f, "playbackOperations", 0); Set(f, "playingChannel", next); Call(f, "QueueLiveRecovery"); Pump();
            Check((bool)Field(f, "recoveryPending") && (int)Field(f, "recoveryAttempts") == 1, "Current media error no longer recovers.");
            Console.WriteLine("PASS: pending channel switch rejects old errors while current media still recovers.");
        }
        finally { Set(f, "playbackOperations", 0); Call(f, "StopLiveRecovery"); }
    }
    public static void SourcePlayback(Assembly a)
    {
        using var f = Form(a); using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
        var serving = Task.Run(async () => { var c = await server.GetContextAsync(); c.Response.ContentLength64 = 100000; await c.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n")); await c.Response.OutputStream.FlushAsync(); return c; });
        ((ComboBox)Field(f, "source")).SelectedIndex = 1; Set(f, "activeSource", 1);
        var movie = Channel(a, "Tracked movie", "Film"); Call(f, "PrepareResume", movie); Set(f, "playingChannel", movie); Set(f, "currentStreamUrl", "http://127.0.0.1/old"); Set(f, "cinemaPlayingView", true); Set(f, "pendingResume", 75000L);
        string key = (string)Field(f, "playingKey"); ((IDictionary)Field(f, "resumePositions"))[key] = 75000L;
        ((TextBox)Field(f, "address")).Text = $"http://127.0.0.1:{port}/slow.m3u";
        var loading = (Task)Call(f, "LoadAsync")!;
        try
        { Wait(serving); Check(!(bool)Field(f, "cinemaPlayingView") && (string)Field(f, "currentStreamUrl") == "", "Source load detaches tracked media without stopping playback view.");
          Check((long)((IDictionary)Field(f, "resumePositions"))[key]! == 75000L, "Source change lost saved VOD position."); Console.WriteLine("PASS: source change explicitly stops tracked playback and retains resume."); }
        finally { ((CancellationTokenSource)Field(f, "sourceLoadCancellation")).Cancel(); Wait(loading); if (serving.IsCompletedSuccessfully) serving.Result.Response.Abort(); }
    }
    public static void KindReturn(Assembly a)
    {
        using var f = Form(a); var catalog = (IList)Field(f, "channels"); catalog.Clear();
        for (int i = 0; i < 150; i++) catalog.Add(Channel(a, "Ulusal " + i, "Canlı TV"));
        catalog.Add(Channel(a, "Diğer", "Canlı TV", "Diğer")); catalog.Add(Channel(a, "Film", "Film", "Sinema"));
        var kind = (ComboBox)Field(f, "kinds"); var groups = (ComboBox)Field(f, "groups");
        kind.SelectedItem = "Canlı TV"; Call(f, "RefreshGroups"); Wait((Task)Field(f, "libraryFilterTask")); groups.SelectedItem = "Canlı TV / Ulusal"; Wait((Task)Field(f, "libraryFilterTask"));
        var list = (Control)Field(f, "list"); var selected = list.GetType().GetProperty("SelectedIndex")!; var top = list.GetType().GetProperty("TopIndex")!; selected.SetValue(list, 100); int savedTop = (int)top.GetValue(list)!;
        kind.SelectedItem = "Film"; Wait((Task)Field(f, "libraryFilterTask")); kind.SelectedItem = "Canlı TV"; Wait((Task)Field(f, "libraryFilterTask"));
        Check(groups.SelectedItem?.ToString() == "Canlı TV / Ulusal" && (int)selected.GetValue(list)! == 100 && (int)top.GetValue(list)! == savedTop,
            "Content-kind return loses category, selected channel, or scroll position.");
        Console.WriteLine("PASS: kind return restores category and viewport.");
    }
}
