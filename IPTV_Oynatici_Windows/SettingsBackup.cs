using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace JoyIptv;

internal sealed record BackupFile(string Name, long Size, string Sha256);
internal sealed record BackupManifest(int Schema, BackupFile[] Files);
internal static class SettingsBackup
{
    private const int Limit = 16 * 1024 * 1024;
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static bool SafeName(string name) => !String.IsNullOrEmpty(name) && name.Length <= 128 &&
        name == Path.GetFileName(name) && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name.Split('.')[0].TrimEnd().ToUpperInvariant()) &&
        !name.EndsWith(' ') && !name.EndsWith('.') && name is not ("." or "..");
    private static bool Eligible(string name) => SafeName(name) && !String.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase) &&
        new[] { ".dat", ".json", ".txt" }.Contains(Path.GetExtension(name).ToLowerInvariant());
    private static Dictionary<string, byte[]> Snapshot(string directory, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ayar klasörü bağlantı olamaz.");
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (string path in Directory.GetFiles(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); string name = Path.GetFileName(path);
            if (!Eligible(name)) continue;
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ayar dosyası bağlantı olamaz.");
            total += info.Length; if (total > Limit || result.Count >= 512) throw new InvalidDataException("Ayar yedeği 16 MB sınırını aşıyor.");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var memory = new MemoryStream(); byte[] buffer = new byte[16384]; int read;
            while ((read = input.Read(buffer)) > 0)
            { token.ThrowIfCancellationRequested(); if (memory.Length + read > Limit - result.Values.Sum(x => (long)x.Length)) throw new InvalidDataException("Ayar yedeği sınırı aşıldı."); memory.Write(buffer, 0, read); }
            byte[] bytes = memory.ToArray(); result.Add(name, bytes);
        }
        return result;
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Export(Dictionary<string, byte[]> data, string outputPath, CancellationToken token)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            var manifest = new BackupManifest(1, data.Select(x => new BackupFile(x.Key, x.Value.Length,
                Convert.ToHexString(SHA256.HashData(x.Value)))).ToArray());
            using (var output = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(output, manifest);
            foreach (var file in data)
            { token.ThrowIfCancellationRequested(); using var output = zip.CreateEntry(file.Key, CompressionLevel.Fastest).Open(); output.Write(file.Value); }
        }
        token.ThrowIfCancellationRequested();
        byte[] encoded = ProtectedData.Protect(memory.ToArray(), null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!); AtomicWrite(outputPath, encoded);
    }
    internal static async Task ExportAsync(string settingsDirectory, string outputPath, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await Task.Run(() => Export(Snapshot(settingsDirectory, token), outputPath, token), token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit, CancellationToken token)
    {
        if (entry.Length > limit || entry.Length < 0) throw new InvalidDataException("Yedek boyutu geçersiz.");
        using var input = entry.Open(); using var data = new MemoryStream(); byte[] buffer = new byte[16384]; int read;
        while ((read = input.Read(buffer)) > 0)
        { token.ThrowIfCancellationRequested(); if (data.Length + read > limit) throw new InvalidDataException("Yedek sınırı aşıldı."); data.Write(buffer, 0, read); }
        if (data.Length != entry.Length) throw new InvalidDataException("Yedek girdisi eksik.");
        return data.ToArray();
    }
    private static Dictionary<string, byte[]> Validate(string archivePath, CancellationToken token)
    {
        if (new FileInfo(archivePath).Length > Limit + 1024 * 1024) throw new InvalidDataException("Yedek çok büyük.");
        byte[] decoded = ProtectedData.Unprotect(File.ReadAllBytes(archivePath), null, DataProtectionScope.CurrentUser);
        using var memory = new MemoryStream(decoded); using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        if (zip.Entries.Count > 513) throw new InvalidDataException("Yedekte çok fazla girdi var.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!SafeName(entry.FullName) || (entry.FullName != "manifest.json" && !Eligible(entry.FullName)) ||
                !entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("Yedek dosya yolu geçersiz.");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry)) throw new InvalidDataException("Yedek manifesti yok.");
        BackupManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<BackupManifest>(ReadEntry(manifestEntry, 256 * 1024, token)); }
        catch (JsonException ex) { throw new InvalidDataException("Yedek manifesti bozuk.", ex); }
        if (manifest?.Schema != 1 || manifest.Files == null || manifest.Files.Length != entries.Count - 1)
            throw new InvalidDataException("Yedek sürümü veya manifesti geçersiz.");
        var data = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); int total = 0;
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            if (file == null || !Eligible(file.Name) || file.Size < 0 || file.Size > Limit - total || !entries.TryGetValue(file.Name, out var entry))
                throw new InvalidDataException("Yedek girdisi geçersiz.");
            byte[] bytes = ReadEntry(entry, Limit - total, token); total += bytes.Length;
            if (bytes.LongLength != file.Size || !String.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.Sha256, StringComparison.OrdinalIgnoreCase) || !data.TryAdd(file.Name, bytes))
                throw new InvalidDataException("Yedek bütünlük denetimi başarısız.");
        }
        return data;
    }
    internal static async Task<string> RestoreAsync(string archivePath, string settingsDirectory, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var data = Validate(archivePath, token); var before = Snapshot(settingsDirectory, token);
                string previous = Path.Combine(settingsDirectory, "yedekler", "restore_oncesi_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8] + ".joybackup");
                Export(before, previous, token);
                var changed = new List<string>();
                try
                {
                    foreach (var file in data)
                    { token.ThrowIfCancellationRequested(); AtomicWrite(Path.Combine(settingsDirectory, file.Key), file.Value); changed.Add(file.Key); }
                    foreach (string name in before.Keys.Except(data.Keys, StringComparer.OrdinalIgnoreCase))
                    { token.ThrowIfCancellationRequested(); File.Delete(Path.Combine(settingsDirectory, name)); changed.Add(name); }
                }
                catch (Exception failure)
                {
                    var errors = new List<Exception> { failure };
                    foreach (string name in changed.AsEnumerable().Reverse())
                    {
                        try
                        {
                            string path = Path.Combine(settingsDirectory, name);
                            if (before.TryGetValue(name, out var bytes)) AtomicWrite(path, bytes);
                            else if (File.Exists(path)) File.Delete(path);
                        }
                        catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
                    }
                    if (errors.Count > 1) throw new IOException("Geri alma tamamlanamadı. Önceki ayar yedeği: " + previous, new AggregateException(errors));
                    throw;
                }
                return previous;
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
