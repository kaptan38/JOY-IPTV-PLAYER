using System.Globalization;
using System.Text.Json;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private static string FirstDate(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            string value = Str(obj, key);
            if (NormalizeSourceDate(value) is string date) return date;
        }
        return "";
    }

    private static string? NormalizeSourceDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "0" or "null") return null;
        if (long.TryParse(value, out long epoch) && epoch > 1_000_000_000)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(epoch > 99_999_999_999 ? epoch / 1000 : epoch)
                .ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
            out var parsed) && parsed.Year is >= 2000 and <= 2100)
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (DateTimeOffset.TryParse(value, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.AllowWhiteSpaces,
            out parsed) && parsed.Year is >= 2000 and <= 2100)
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return null;
    }

    private static string? SourceRemaining(string date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var expires)) return null;
        int days = expires.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        return days < 0 ? "süresi doldu" : days + " gün";
    }

    private void UpdateCurrentSourceDates(string started, string expires)
    {
        currentStarted = NormalizeSourceDate(started) ?? "";
        currentExpires = NormalizeSourceDate(expires) ?? "";
        int i = savedSources.FindIndex(s => s.Type == source.SelectedIndex &&
            s.Address == address.Text.Trim() && s.Username == username.Text.Trim() && s.Password == password.Text);
        if (i < 0) return;
        var entry = savedSources[i] with { Started = currentStarted, Expires = currentExpires };
        savedSources[i] = entry;
        try { PersistSources(); } catch { /* Playback does not depend on saved metadata. */ }
        RefreshSources(); savedSourceBox.SelectedIndex = i;
    }

    private void ShowSourceDates()
    {
        SavedSource? entry = null;
        int index = savedSourceBox.SelectedIndex;
        if (index >= 0 && index < savedSources.Count)
        {
            var selected = savedSources[index];
            if (selected.Type == source.SelectedIndex && selected.Address == address.Text.Trim() &&
                selected.Username == username.Text.Trim() && selected.Password == password.Text) entry = selected;
        }
        string began = entry?.Started ?? currentStarted ?? "", ended = entry?.Expires ?? currentExpires ?? "";
        string name = entry?.Name ?? "Geçerli kaynak";
        MessageBox.Show(this, $"{name}\nBaşlangıç: {(began.Length == 0 ? "Sağlayıcı bildirmedi" : began)}" +
            $"\nBitiş: {(ended.Length == 0 ? "Sağlayıcı bildirmedi" : ended)}" +
            $"\nKalan: {SourceRemaining(ended) ?? "Bilinmiyor"}", "Kaynak süresi");
    }

    private async Task TryLoadM3uAccountDatesAsync(string url, int generation)
    {
        try
        {
            var uri = new Uri(url);
            if (!uri.AbsolutePath.EndsWith("/get.php", StringComparison.OrdinalIgnoreCase)) return;
            var args = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
                .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]), StringComparer.OrdinalIgnoreCase);
            if (!args.TryGetValue("username", out var user) || !args.TryGetValue("password", out var pass)) return;
            string api = uri.GetLeftPart(UriPartial.Authority) + "/player_api.php?username=" +
                Uri.EscapeDataString(user) + "&password=" + Uri.EscapeDataString(pass);
            using var doc = JsonDocument.Parse(await http.GetStringAsync(api));
            if (generation == stalkerCatalogGeneration && source.SelectedIndex == 1 &&
                address.Text.Trim() == url && doc.RootElement.TryGetProperty("user_info", out var info))
                UpdateCurrentSourceDates(Str(info, "created_at"), Str(info, "exp_date"));
        }
        catch (Exception) { /* M3U URLs need not have an account API. */ }
    }
}
