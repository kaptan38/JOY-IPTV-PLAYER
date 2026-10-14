using System.Security.Cryptography;
using System.Text;

namespace JoyIptv;

internal sealed record LibrarySnapshot(Channel[] Items, string FavoritePrefix, HashSet<string> FavoriteIds,
    Dictionary<string, int> WatchCounts, int CatalogVersion);
internal sealed record LibraryQuery(string Text, string Kind, string Group, bool FavoritesOnly);
internal static class LibrarySearch
{
    internal static string FavoriteId(string prefix, Channel c) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(prefix + c.Kind + "|" + c.Name + "|" + (c.Command ?? c.Url))));
    internal static List<Channel> Filter(LibrarySnapshot snapshot, LibraryQuery query, CancellationToken token)
    {
        var candidates = new List<(Channel Item, string Key)>();
        foreach (var item in snapshot.Items)
        {
            token.ThrowIfCancellationRequested();
            if (query.Kind is not ("Tümü" or "Önerilen") && item.Kind != query.Kind) continue;
            if (query.Group != "Tümü" && query.Group != item.Kind + " / " + item.Group) continue;
            string key = query.FavoritesOnly || query.Kind == "Önerilen" ? FavoriteId(snapshot.FavoritePrefix, item) : "";
            if (query.FavoritesOnly && !snapshot.FavoriteIds.Contains(key)) continue;
            candidates.Add((item, key));
        }
        IEnumerable<Channel> result;
        if (query.Kind == "Önerilen")
        {
            var preferred = candidates.Where(c => snapshot.WatchCounts.ContainsKey(c.Key) || snapshot.FavoriteIds.Contains(c.Key))
                .GroupBy(c => c.Item.Kind + "|" + c.Item.Group).ToDictionary(g => g.Key,
                    g => g.Sum(c => snapshot.WatchCounts.GetValueOrDefault(c.Key) + (snapshot.FavoriteIds.Contains(c.Key) ? 3 : 0)));
            result = candidates.Select(c => (c.Item, Score: (snapshot.FavoriteIds.Contains(c.Key) ? 20 : 0) +
                snapshot.WatchCounts.GetValueOrDefault(c.Key) * 8 + preferred.GetValueOrDefault(c.Item.Kind + "|" + c.Item.Group)))
                .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(150).Select(x => x.Item);
        }
        else result = candidates.Select(x => x.Item);
        if (query.Text.Length == 0) return result.ToList();
        var ranked = new List<(Channel Item, int Score)>();
        foreach (var item in result)
        { token.ThrowIfCancellationRequested(); int score = PlayerForm.SmartSearchScore(item, query.Text);
          if (score >= 0) ranked.Add((item, score)); }
        token.ThrowIfCancellationRequested();
        return ranked.OrderByDescending(x => x.Score).Select(x => x.Item).ToList();
    }
}
