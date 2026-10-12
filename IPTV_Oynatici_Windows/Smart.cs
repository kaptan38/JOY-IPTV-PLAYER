using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JoyIptv;

internal sealed record CachedChannels(DateTime SavedAt, List<Channel> Items);

internal sealed partial class PlayerForm
{
    private readonly Dictionary<string, int> watchCounts = new(StringComparer.Ordinal);
    private string currentStreamUrl = "";
    private DateTime watchStarted;
    private bool watchRecorded;

    private string CacheKey()
    {
        string sourceIdentity = source.SelectedIndex + "|" + address.Text.Trim() + "|" + username.Text.Trim() + "|" + password.Text;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdentity)));
    }

    private string CacheFile => Path.Combine(settingsDir, "onbellek", CacheKey() + ".dat");
    private string HistoryFile => Path.Combine(settingsDir, "izleme_gecmisi.json");

    private void SaveChannelCache(List<Channel> items)
    {
        try
        {
            string path = CacheFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new CachedChannels(DateTime.Now, items));
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, path, true);
        }
        catch (Exception) { status.Text = "Kanal önbelleği kaydedilemedi."; }
    }

    private bool TryLoadChannelCache(out List<Channel> items, out DateTime savedAt)
    {
        items = new(); savedAt = default;
        try
        {
            byte[] encrypted = File.ReadAllBytes(CacheFile);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var cached = JsonSerializer.Deserialize<CachedChannels>(plain);
            if (cached == null || cached.Items.Count == 0) return false;
            items = cached.Items; savedAt = cached.SavedAt; return true;
        }
        catch { return false; }
    }

    private void LoadWatchHistory()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return;
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(HistoryFile)) ?? new())
                watchCounts[pair.Key] = pair.Value;
        }
        catch { status.Text = "İzleme geçmişi okunamadı."; }
    }

    private void StartWatchSession()
    {
        watchStarted = DateTime.UtcNow; watchRecorded = false;
    }

    private void UpdateWatchSession()
    {
        if (watchRecorded || playingChannel == null || !player.IsPlaying ||
            (DateTime.UtcNow - watchStarted).TotalSeconds < 30) return;
        watchRecorded = true;
        string key = FavoriteKey(playingChannel);
        watchCounts[key] = Math.Min(1000, watchCounts.GetValueOrDefault(key) + 1);
        try
        {
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(watchCounts));
        }
        catch { /* Watching continues if history is unavailable. */ }
    }

    private IEnumerable<Channel> RankRecommendations(IEnumerable<Channel> sourceChannels)
    {
        var all = sourceChannels.ToList();
        var keyed = all.Select(c => (Item: c, Key: FavoriteKey(c))).ToList();
        var preferredGroups = keyed.Where(c => watchCounts.ContainsKey(c.Key) || favorites.Contains(c.Key))
            .GroupBy(c => c.Item.Kind + "|" + c.Item.Group)
            .ToDictionary(g => g.Key, g => g.Sum(c => watchCounts.GetValueOrDefault(c.Key) + (favorites.Contains(c.Key) ? 3 : 0)));
        return keyed.Select(c => new
        {
            Item = c.Item,
            Score = (favorites.Contains(c.Key) ? 20 : 0) +
                watchCounts.GetValueOrDefault(c.Key) * 8 +
                preferredGroups.GetValueOrDefault(c.Item.Kind + "|" + c.Item.Group)
        }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(150).Select(x => x.Item);
    }

    private static string NormalizeSearch(string text)
    {
        text = text.Replace('ı', 'i').Replace('İ', 'I').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString();
    }

    private static int SmartSearchScore(Channel channel, string query)
    {
        string name = NormalizeSearch(channel.Name), group = NormalizeSearch(channel.Group), wanted = NormalizeSearch(query);
        if (name == wanted) return 100;
        if (name.StartsWith(wanted)) return 80;
        if (name.Contains(wanted)) return 60;
        string kind = NormalizeSearch(channel.Kind);
        if (group.Contains(wanted) || kind.Contains(wanted)) return 35;
        var tokens = Regex.Matches(wanted, @"[\p{L}\p{N}]+")
            .Cast<Match>().Select(m => m.Value switch { "filmler" => "film", "diziler" => "dizi", "uhd" => "4k", _ => m.Value }).ToArray();
        if (tokens.Length == 0) return -1;
        string searchable = name + " " + group + " " + kind;
        string[] words = Regex.Matches(searchable, @"[\p{L}\p{N}]+")
            .Cast<Match>().Select(m => m.Value == "uhd" ? "4k" : m.Value).ToArray();
        int score = 0;
        foreach (string token in tokens)
        {
            if (words.Contains(token)) { score += 15; continue; }
            if (token.All(char.IsDigit)) return -1;
            if (searchable.Contains(token)) { score += 10; continue; }
            // Years and short words require an exact match; fuzzy matching
            // must not turn 2024 into 2023 or HD into an unrelated title.
            if (token.Length < 4 || token.Any(char.IsDigit)) return -1;
            int limit = token.Length >= 6 ? 2 : 1;
            if (!words.Any(word => Math.Abs(word.Length - token.Length) <= limit &&
                BoundedDistance(word, token, limit) <= limit)) return -1;
            score += 3;
        }
        return Math.Min(55, score);
    }

    private static int BoundedDistance(string a, string b, int limit)
    {
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray(), current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i; int min = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                min = Math.Min(min, current[j]);
            }
            if (min > limit) return limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    private async Task<string> ProbePlaybackAsync(Uri uri)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(0, 1023);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            int code = (int)result.StatusCode;
            string note = code switch
            {
                401 or 403 => "erişim reddedildi",
                404 => "yayın yolu bulunamadı",
                >= 500 => "sağlayıcı sunucu hatası",
                >= 200 and < 300 => "sunucu yanıt verdi",
                _ => "sunucu yanıtı"
            };
            return $"{code} ({note})";
        }
        catch (TaskCanceledException) { return "8 saniyede yanıt yok"; }
        catch (Exception e) { return "kontrol edilemedi: " + e.GetType().Name; }
    }
}
