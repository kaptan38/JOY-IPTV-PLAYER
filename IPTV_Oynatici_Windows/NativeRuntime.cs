using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace JoyIptv;

internal static class NativeRuntime
{
    internal static string EnsureReady()
    {
        const string resource = "JoyIptv.LibVLC.zip";
        var assembly = Assembly.GetExecutingAssembly();
        using var embedded = assembly.GetManifestResourceStream(resource);
        if (embedded == null)
        {
            string external = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
            if (Ready(external, false)) return external;
            throw new InvalidOperationException("EXE içinde VLC oynatma paketi bulunamadı. Tam paketi yeniden indirin.");
        }
        string hash = Convert.ToHexString(SHA256.HashData(embedded));
        string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JOY_IPTV_Player", "oynatma_motoru");
        string destination = Path.Combine(parent, hash);
        using var mutex = new Mutex(false, @"Local\JOY_IPTV_VLC_" + hash);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(90)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("Oynatma motoru hazırlanıyor. Biraz sonra yeniden açın.");
            if (Ready(destination, true)) return destination;
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".hazirlaniyor-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var input = assembly.GetManifestResourceStream(resource)!;
                using var archive = new ZipArchive(input, ZipArchiveMode.Read);
                archive.ExtractToDirectory(staging);
                if (!Ready(staging, false)) throw new InvalidDataException("Gömülü VLC paketi eksik.");
                File.WriteAllText(Path.Combine(staging, ".hazir"), hash);
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                Directory.Move(staging, destination);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            return destination;
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static bool Ready(string directory, bool requireStamp) =>
        File.Exists(Path.Combine(directory, "libvlc.dll")) &&
        File.Exists(Path.Combine(directory, "libvlccore.dll")) &&
        Directory.Exists(Path.Combine(directory, "plugins")) &&
        (!requireStamp || File.Exists(Path.Combine(directory, ".hazir")));
}
