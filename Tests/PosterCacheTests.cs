using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Drawing.Imaging;

internal static class PosterCacheTests
{
    public static void Run(Assembly assembly)
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
        byte[] bytes;
        using (var bitmap = new Bitmap(30, 40)) { using var data = new MemoryStream(); bitmap.Save(data, ImageFormat.Png); bytes = data.ToArray(); }
        string url = $"http://127.0.0.1:{port}/{Guid.NewGuid():N}.png";
        int requests = 0;
        var serving = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                try { var ctx = await server.GetContextAsync(); Interlocked.Increment(ref requests);
                    ctx.Response.ContentLength64 = bytes.Length; await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close(); }
                catch (HttpListenerException) { break; } catch (ObjectDisposedException) { break; }
            }
        });
        var type = assembly.GetType("JoyIptv.PosterGallery")!;
        var get = type.GetMethod("GetPoster", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Image? Read(Control gallery) => get.Invoke(gallery, new object[] { url }) as Image;
        void Wait(Control gallery)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (Read(gallery) == null && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(5); }
            if (Read(gallery) == null) throw new Exception("FAIL: poster unavailable.");
        }
        using (var first = (Control)Activator.CreateInstance(type, true)!) { Wait(first); Read(first); }
        if (requests != 1) throw new Exception("FAIL: duplicate requests for identical poster URL.");
        server.Stop(); serving.GetAwaiter().GetResult();
        using (var second = (Control)Activator.CreateInstance(type, true)!) { Wait(second); }
        if (requests != 1) throw new Exception("FAIL: next gallery downloaded a disk-cached image again.");
        Console.WriteLine("PASS: poster persists across galleries and works without network.");
        string directory = Path.Combine(Path.GetTempPath(), "joy-posters-" + Guid.NewGuid().ToString("N"));
        var cacheType = assembly.GetType("JoyIptv.PosterDiskCache")!;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var cache = Activator.CreateInstance(cacheType, flags, null, new object[] { directory }, null)!;
        try
        {
            ((Task)cacheType.GetMethod("WriteAsync", flags)!.Invoke(cache, new object[] { url, bytes, CancellationToken.None })!).GetAwaiter().GetResult();
            object? Entry()
            { var t = (Task)cacheType.GetMethod("ReadAsync", flags)!.Invoke(cache, new object[] { url, CancellationToken.None })!;
              t.GetAwaiter().GetResult(); return t.GetType().GetProperty("Result")!.GetValue(t); }
            var entry = Entry(); if (entry == null) throw new Exception("FAIL: disk poster not readable.");
            string path = Directory.GetFiles(directory, "*.png").Single();
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
            entry = Entry();
            if (entry == null || !(bool)entry.GetType().GetProperty("IsStale")!.GetValue(entry)!) throw new Exception("FAIL: seven-day poster TTL ignored.");
            File.WriteAllText(path, "not an image");
            if (Entry() != null) throw new Exception("FAIL: corrupt disk poster accepted.");
            for (int i = 0; i < 90; i++) File.WriteAllBytes(Path.Combine(directory, i.ToString("D64") + ".png"), new byte[3 * 1024 * 1024]);
            ((Task)cacheType.GetMethod("TrimAsync", flags)!.Invoke(cache, new object[] { CancellationToken.None })!).GetAwaiter().GetResult();
            if (Directory.GetFiles(directory).Sum(p => new FileInfo(p).Length) > 256L * 1024 * 1024) throw new Exception("FAIL: disk cache budget exceeded.");
            Console.WriteLine("PASS: poster TTL, corrupt entry, and 256 MB budget enforced.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
