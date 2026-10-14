using System.Reflection;
using System.Collections;

internal static class FeatureTests
{
    const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
    static IEnumerable<Control> Descendants(Control root)
    { foreach (Control child in root.Controls) { yield return child; foreach (var c in Descendants(child)) yield return c; } }
    static Form Form(Assembly a) => (Form)Activator.CreateInstance(a.GetType("JoyIptv.PlayerForm")!, true)!;
    public static void Recovery(Assembly a)
    {
        using var f = Form(a);
        var buffer = (ComboBox)f.GetType().GetField("liveBuffer", P)!.GetValue(f)!;
        Check(buffer.Items.Cast<object>().Any(x => x.ToString()!.Contains("Otomatik")), "Automatic adaptive playback buffer unavailable.");
        var t = a.GetType("JoyIptv.RecoveryPolicy")!; var policy = Activator.CreateInstance(t, true)!;
        t.GetMethod("Reset", P)!.Invoke(policy, new object[] { 1 });
        for (int i = 0; i < 3; i++)
        {
            var attempt = t.GetMethod("TrySchedule", P)!.Invoke(policy, new object[] { 1 });
            Check(attempt != null && (int)attempt.GetType().GetProperty("DelaySeconds")!.GetValue(attempt)! == (1 << (i + 1)), "Retry delay changed.");
        }
        Check(t.GetMethod("TrySchedule", P)!.Invoke(policy, new object[] { 1 }) == null, "Recovery exceeds three attempts.");
        t.GetMethod("Reset", P)!.Invoke(policy, new object[] { 2 });
        Check(t.GetMethod("TrySchedule", P)!.Invoke(policy, new object[] { 1 }) == null, "Stale recovery generation accepted.");
        foreach (int expected in new[] { 3000, 5000, 9000, 9000 })
        { t.GetMethod("ObserveBuffering", P)!.Invoke(policy, new object[] { TimeSpan.FromSeconds(10) });
          Check((int)t.GetProperty("AutomaticCacheMs", P)!.GetValue(policy)! == expected, "Adaptive buffer tier incorrect."); }
        t.GetMethod("ObserveStable", P)!.Invoke(policy, new object[] { TimeSpan.FromSeconds(60) });
        Check((int)t.GetProperty("AutomaticCacheMs", P)!.GetValue(policy)! == 1500, "Stable playback did not reset buffer.");
        Console.WriteLine("PASS: recovery is bounded, generation-safe, and adaptive.");
    }
    public static void Menu(Assembly a)
    {
        using var f = Form(a);
        Check(Descendants(f).Any(c => c.AccessibleName == "Menüyü daralt"), "Rail collapse control unavailable.");
        var t = a.GetType("JoyIptv.CinemaPreferences")!;
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var size = a.GetType("JoyIptv.PosterSize")!;
            var prefs = Activator.CreateInstance(t, new object[] { true, Enum.Parse(size, "Small"), false })!;
            ((Task)t.GetMethod("SaveAsync", P)!.Invoke(prefs, new object[] { dir, CancellationToken.None })!).GetAwaiter().GetResult();
            var restored = t.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, new object[] { dir })!;
            Check((bool)t.GetProperty("CompactRail")!.GetValue(restored)! && t.GetProperty("PosterSize")!.GetValue(restored)!.ToString() == "Small", "Appearance preferences not persisted.");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        var root = (TableLayoutPanel)f.GetType().GetField("cinemaRoot", P)!.GetValue(f)!;
        var collapse = Descendants(f).OfType<Button>().Single(c => c.AccessibleName == "Menüyü daralt");
        float original = root.ColumnStyles[0].Width;
        collapse.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(collapse, new object[] { EventArgs.Empty });
        var until = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(2); }
        Check(root.ColumnStyles[0].Width != original, "Collapse did not change menu width.");
        var gallery = (Control)f.GetType().GetField("cinemaGallery", P)!.GetValue(f)!;
        var posterSize = gallery.GetType().GetProperty("PosterSize", P)!;
        var width = gallery.GetType().GetProperty("CardWidth", P)!;
        int small = 0, large = 0;
        foreach (string value in new[] { "Small", "Medium", "Large" })
        {
            posterSize.SetValue(gallery, Enum.Parse(a.GetType("JoyIptv.PosterSize")!, value));
            if (value == "Small") small = (int)width.GetValue(gallery)!;
            if (value == "Large") large = (int)width.GetValue(gallery)!;
        }
        Check(small < large, "Poster size does not affect visible card dimensions.");
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            using var scaled = Form(a);
            scaled.AutoScaleMode = AutoScaleMode.None;
            scaled.Scale(new SizeF(scale, scale));
            var layout = (TableLayoutPanel)scaled.GetType().GetField("cinemaRoot", P)!.GetValue(scaled)!;
            layout.Dock = DockStyle.None; layout.Size = new Size((int)(1060 * scale), (int)(660 * scale)); layout.PerformLayout();
            var main = (Control)scaled.GetType().GetField("cinemaMainHost", P)!.GetValue(scaled)!;
            var filter = (TextBox)scaled.GetType().GetField("filter", P)!.GetValue(scaled)!;
            Check(main.Width >= 200 * scale && main.Height >= 150 * scale && filter.Width >= 100 * scale,
                "Minimum viewport clips menu/content at " + scale);
        }
        Console.WriteLine("PASS: menu collapse and poster preferences are available and persisted.");
    }
    public static void Backup(Assembly a)
    {
        using var f = Form(a);
        var show = f.GetType().GetField("showSettings", P)!.GetValue(f);
        // Settings controls live in the captured settings dialog, so verify the advertised backup surface via menu.
        var menu = (ContextMenuStrip)f.GetType().GetField("cinemaToolsMenu", P)!.GetValue(f)!;
        IEnumerable<ToolStripItem> Items(ToolStripItemCollection rows)
        { foreach (ToolStripItem item in rows) { yield return item; if (item is ToolStripMenuItem m) foreach (var x in Items(m.DropDownItems)) yield return x; } }
        Check(Items(menu.Items).Any(i => (i.Text ?? "").Contains("yedek", StringComparison.OrdinalIgnoreCase)), "Settings backup action unavailable.");
        var type = a.GetType("JoyIptv.SettingsBackup")!;
        var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        string dir = Path.Combine(Path.GetTempPath(), "joy-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "theme.txt"), "original");
            Directory.CreateDirectory(Path.Combine(dir, "onbellek")); File.WriteAllText(Path.Combine(dir, "onbellek", "huge.dat"), "excluded");
            string archive = Path.Combine(dir, "export.joybackup");
            ((Task)type.GetMethod("ExportAsync", flags)!.Invoke(null, new object[] { dir, archive, CancellationToken.None })!).GetAwaiter().GetResult();
            Check(!File.ReadAllText(archive).Contains("original"), "Backup contains plaintext settings.");
            File.WriteAllText(Path.Combine(dir, "theme.txt"), "changed");
            var restore = (Task)type.GetMethod("RestoreAsync", flags)!.Invoke(null, new object[] { archive, dir, CancellationToken.None })!;
            restore.GetAwaiter().GetResult(); Check(File.ReadAllText(Path.Combine(dir, "theme.txt")) == "original", "Settings round trip failed.");
            File.WriteAllBytes(archive, new byte[] { 1, 2, 3 });
            try { ((Task)type.GetMethod("RestoreAsync", flags)!.Invoke(null, new object[] { archive, dir, CancellationToken.None })!).GetAwaiter().GetResult(); throw new Exception("FAIL: corrupt backup accepted."); }
            catch (System.Security.Cryptography.CryptographicException) { }
            Check(File.ReadAllText(Path.Combine(dir, "theme.txt")) == "original", "Corrupt restore modified settings.");
            File.WriteAllBytes(archive, System.Security.Cryptography.ProtectedData.Protect(new byte[] { 1, 2, 3 }, new byte[] { 99 },
                System.Security.Cryptography.DataProtectionScope.CurrentUser));
            try { ((Task)type.GetMethod("RestoreAsync", flags)!.Invoke(null, new object[] { archive, dir, CancellationToken.None })!).GetAwaiter().GetResult(); throw new Exception("FAIL: wrong-key backup accepted."); }
            catch (System.Security.Cryptography.CryptographicException) { }
            Check(File.ReadAllText(Path.Combine(dir, "theme.txt")) == "original", "Wrong-key restore modified settings.");
            foreach (string name in new[] { "../escape.txt", "C:/escape.txt", "theme.txt:stream" })
            {
                using var memory = new MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("malicious");
                    using var manifest = new StreamWriter(zip.CreateEntry("manifest.json").Open());
                    manifest.Write(System.Text.Json.JsonSerializer.Serialize(new { Schema = 1, Files = new[] {
                        new { Name = name, Size = 9, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("malicious"))) } } }));
                }
                File.WriteAllBytes(archive, System.Security.Cryptography.ProtectedData.Protect(memory.ToArray(), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser));
                try { ((Task)type.GetMethod("RestoreAsync", flags)!.Invoke(null, new object[] { archive, dir, CancellationToken.None })!).GetAwaiter().GetResult(); throw new Exception("FAIL: unsafe backup accepted."); }
                catch (InvalidDataException) { }
                Check(File.ReadAllText(Path.Combine(dir, "theme.txt")) == "original", "Unsafe restore changed existing settings.");
            }
            string rollback = Path.Combine(dir, "rollback"); Directory.CreateDirectory(rollback);
            File.WriteAllText(Path.Combine(rollback, "theme.txt"), "before");
            File.WriteAllText(Path.Combine(rollback, "zblocked.json"), "{}");
            string rollbackArchive = Path.Combine(dir, "rollback.joybackup");
            ((Task)type.GetMethod("ExportAsync", flags)!.Invoke(null, new object[] { rollback, rollbackArchive, CancellationToken.None })!).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(rollback, "theme.txt"), "keep-me");
            File.Delete(Path.Combine(rollback, "zblocked.json")); Directory.CreateDirectory(Path.Combine(rollback, "zblocked.json"));
            try { ((Task)type.GetMethod("RestoreAsync", flags)!.Invoke(null, new object[] { rollbackArchive, rollback, CancellationToken.None })!).GetAwaiter().GetResult(); throw new Exception("FAIL: blocked restore accepted."); }
            catch (IOException) { }
            Check(File.ReadAllText(Path.Combine(rollback, "theme.txt")) == "keep-me", "Failed restore did not roll back applied settings.");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("PASS: encrypted backup round trip and corrupt archive protection.");
    }
}
