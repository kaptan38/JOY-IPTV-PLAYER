using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using LibVLCSharp.Shared;

internal static class SourceRegression
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Field(object form, string name) => form.GetType().GetField(name, Private)!.GetValue(form)!;
    private static object? Call(object form, string name, params object[] args) =>
        form.GetType().GetMethod(name, Private)!.Invoke(form, args);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Core.Initialize(args[0]);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var assembly = Assembly.Load("JOY_IPTV_Player");
            using var form = (Form)Activator.CreateInstance(assembly.GetType("JoyIptv.PlayerForm")!, true)!;
            var address = (TextBox)Field(form, "address");
            var mode = (ComboBox)Field(form, "source");
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            string url = $"http://127.0.0.1:{port}/playlist.m3u";
            using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
            var serving = Task.Run(async () =>
            {
                var context = await server.GetContextAsync();
                byte[] data = Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:-1 group-title=\"Ulusal\",Test TV\nhttp://127.0.0.1/live.ts\n");
                context.Response.ContentType = "audio/x-mpegurl";
                context.Response.ContentLength64 = data.Length;
                await context.Response.OutputStream.WriteAsync(data); context.Response.Close();
            });
            mode.SelectedIndex = 0; address.Text = "  " + url + "  ";
            var task = (Task)Call(form, "ParseM3uFileAsync")!;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            Check(task.IsCompleted, "FAIL: URL in file mode did not finish.");
            try { task.GetAwaiter().GetResult(); }
            catch (Exception ex) { throw new InvalidOperationException("FAIL: HTTP playlist in M3U file mode was treated as a file path.", ex); }
            var items = (IList)task.GetType().GetProperty("Result")!.GetValue(task)!;
            Check(items.Count == 1, "FAIL: HTTP playlist was not parsed.");
            Check(mode.SelectedIndex == 1, "FAIL: URL input did not switch to M3U URL.");
            serving.GetAwaiter().GetResult();
            Console.WriteLine("PASS: HTTP URL entered in M3U file mode loads the playlist and selects URL mode.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
