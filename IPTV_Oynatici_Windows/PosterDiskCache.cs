using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace JoyIptv;

internal sealed record PosterCacheEntry(byte[] Bytes, DateTime SavedAt, bool IsStale);
internal sealed class PosterDiskCache
{
    private readonly string directory;
    private static readonly SemaphoreSlim writes = new(1, 1);
    internal const int ImageLimit = 3 * 1024 * 1024;
    private const long Budget = 256L * 1024 * 1024;
    internal PosterDiskCache(string directory) { this.directory = directory; }
    private string PathFor(string url) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".png");
    internal static Bitmap Decode(byte[] bytes)
    {
        if (bytes.Length > ImageLimit) throw new InvalidDataException("Afiş boyutu sınırı aşıldı.");
        using var stream = new MemoryStream(bytes, false);
        using var original = Image.FromStream(stream, false, true);
        if ((long)original.Width * original.Height > 20_000_000) throw new InvalidDataException("Afiş piksel sınırı aşıldı.");
        float scale = Math.Min(1f, Math.Min(440f / original.Width, 440f / original.Height));
        return new Bitmap(original, Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
    }
    internal Task<PosterCacheEntry?> ReadAsync(string url, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); string path = PathFor(url);
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > ImageLimit) throw new InvalidDataException();
            byte[] bytes = File.ReadAllBytes(path); token.ThrowIfCancellationRequested();
            using var validated = Decode(bytes);
            DateTime saved = File.GetLastWriteTimeUtc(path);
            return new PosterCacheEntry(bytes, saved, DateTime.UtcNow - saved > TimeSpan.FromDays(7));
        }
        catch (OperationCanceledException) { throw; }
        catch { try { File.Delete(path); } catch { } return null; }
    }, token);
    internal async Task WriteAsync(string url, byte[] scaledImage, CancellationToken token)
    {
        await writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); using var valid = Decode(scaledImage);
                Directory.CreateDirectory(directory); string path = PathFor(url), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temp, scaledImage); token.ThrowIfCancellationRequested(); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                Trim(token);
            }, token).ConfigureAwait(false);
        }
        finally { writes.Release(); }
    }
    internal async Task TrimAsync(CancellationToken token)
    {
        await writes.WaitAsync(token).ConfigureAwait(false);
        try { await Task.Run(() => Trim(token), token).ConfigureAwait(false); }
        finally { writes.Release(); }
    }
    private void Trim(CancellationToken token)
    {
        if (!Directory.Exists(directory)) return;
        var files = new DirectoryInfo(directory).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc).ToArray();
        long bytes = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested(); if (bytes <= Budget) break;
            try { long size = file.Length; file.Delete(); bytes -= size; } catch (IOException) { }
        }
    }
}
