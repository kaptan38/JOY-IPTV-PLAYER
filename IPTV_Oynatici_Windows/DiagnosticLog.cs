using System.Text;
using System.Text.RegularExpressions;

namespace JoyIptv;

internal sealed class DiagnosticLog
{
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private const int Limit = 2 * 1024 * 1024;
    internal DiagnosticLog(string directory) { this.directory = directory; }
    internal static string Sanitize(string value, IReadOnlyList<string> secrets)
    {
        value = Regex.Replace(value, @"\b(?:[a-z][a-z0-9+.-]*://|www\.)[^\s<>]+", "[adres]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
        value = Regex.Replace(value, @"\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b", "[gizli]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
        value = Regex.Replace(value, @"\b(?:password|passwd|token|username|mac)\s*[=:]\s*[^\s&,;]+", "[gizli]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
        foreach (string secret in secrets.Where(s => !String.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
            value = value.Replace(secret, "[gizli]", StringComparison.Ordinal);
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
    internal async Task WriteAsync(string eventName, string message, IReadOnlyList<string> secrets, CancellationToken token)
    {
        var known = secrets.ToArray();
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
                string text = Sanitize(eventName + ": " + message, known);
                if (text.Length > Limit / 4 - 100) text = text[..(Limit / 4 - 100)] + " [kısaltıldı]";
                byte[] bytes = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
                string path = Path.Combine(directory, "current.log");
                if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > Limit)
                {
                    File.Delete(Path.Combine(directory, "4.log"));
                    for (int i = 3; i >= 1; i--)
                    { string old = Path.Combine(directory, i + ".log"); if (File.Exists(old)) File.Move(old, Path.Combine(directory, (i + 1) + ".log"), true); }
                    File.Move(path, Path.Combine(directory, "1.log"), true);
                }
                using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                output.Write(bytes);
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}

internal sealed partial class PlayerForm
{
    private readonly DiagnosticLog diagnostics = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JOY_IPTV_Player", "tani"));
    private async Task LogDiagnosticAsync(string eventName, string message)
    {
        string[] secrets = { username.Text, password.Text, stalkerToken };
        try { await diagnostics.WriteAsync(eventName, message, secrets, CancellationToken.None); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
