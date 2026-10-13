using System.Text.RegularExpressions;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private static string KindFromGroup(string group)
    {
        string value = group.Replace('İ', 'I').Replace('ı', 'i').ToLowerInvariant();
        if (Regex.IsMatch(value, @"dizi|series|serial|sezon|season|episodes?|\btv[ ._-]*shows?\b|\bshows\b",
            RegexOptions.CultureInvariant)) return "Dizi";
        if (Regex.IsMatch(value, "film|movie|vod|sinema|cinema", RegexOptions.CultureInvariant)) return "Film";
        return "Canlı TV";
    }

    private static string KindFromM3uUri(Uri uri, string groupKind)
    {
        // Interpret the first catalog route, never a later username/password
        // segment. Bouquet names are retained exactly as the provider sent them.
        string path = uri.AbsolutePath;
        int start = 0;
        while (start < path.Length)
        {
            if (path[start] == '/') { start++; continue; }
            int end = path.IndexOf('/', start);
            if (end < 0) end = path.Length;
            var part = path.AsSpan(start, end - start);
            if (part.Equals("live".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "Canlı TV";
            if (part.Equals("movie".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                part.Equals("movies".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                part.Equals("vod".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "Film";
            if (part.Equals("series".AsSpan(), StringComparison.OrdinalIgnoreCase)) return "Dizi";
            start = end;
        }
        return groupKind;
    }

    private static List<Channel> NormalizeM3uKinds(List<Channel> items)
    {
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Channel>(items.Count);
        foreach (var item in items)
        {
            if (!groups.TryGetValue(item.Group, out var groupKind)) groups[item.Group] = groupKind = KindFromGroup(item.Group);
            string kind = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? KindFromM3uUri(uri, groupKind) : groupKind;
            result.Add(item.Kind == kind ? item : item with { Kind = kind });
        }
        return result;
    }
}
