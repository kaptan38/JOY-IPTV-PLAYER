using System.Reflection;

internal static class LogTests
{
    public static void Run(Assembly a)
    {
        var t = a.GetType("JoyIptv.DiagnosticLog");
        if (t == null) throw new Exception("FAIL: diagnostic events cannot be persisted with credential sanitization.");
        string dir = Path.Combine(Path.GetTempPath(), "joy-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = Activator.CreateInstance(t, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { dir }, null)!;
            var write = t.GetMethod("WriteAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            string secret = "PrivatePassword123";
            for (int i = 0; i < 7; i++)
            {
                string text = "https://server.invalid/live/user/PrivatePassword123/123.ts?token=HiddenToken 00:1A:79:11:22:33 " + secret + " " + new string('x', 3 * 1024 * 1024);
                ((Task)write.Invoke(log, new object[] { "playback", text, new[] { secret, "HiddenToken" }, CancellationToken.None })!).GetAwaiter().GetResult();
            }
            var files = Directory.GetFiles(dir, "*.log");
            if (files.Length > 5 || files.Any(p => new FileInfo(p).Length > 2 * 1024 * 1024)) throw new Exception("FAIL: log rotation budget exceeded.");
            foreach (string p in files)
            { string text = File.ReadAllText(p); if (text.Contains(secret) || text.Contains("HiddenToken") || text.Contains("server.invalid") || text.Contains("00:1A")) throw new Exception("FAIL: credentials leaked into log."); }
            Console.WriteLine("PASS: rotated diagnostics redact URLs, MACs, and secrets.");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
