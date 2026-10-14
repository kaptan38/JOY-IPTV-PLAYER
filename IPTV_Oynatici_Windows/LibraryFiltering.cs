namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private CancellationTokenSource? libraryFilterCancellation;
    private int libraryFilterGeneration, libraryCatalogVersion;
    private Task libraryFilterTask = Task.CompletedTask;
    private string FavoritePrefix => activeSource + "|" + (activeSource == 3 ? stalkerBase : address.Text.Trim()) + "|";
    private readonly Dictionary<string, (Channel? Selected, int Top)> categoryPositions = new();
    private string displayedCategory = "";

    private void ApplyFilter()
    {
        if (updatingLibrary || IsDisposed || Disposing) return;
        libraryFilterTask = RequestLibraryFilterAsync(false);
    }
    private async Task RequestLibraryFilterAsync(bool debounce)
    {
        if (updatingLibrary || IsDisposed || Disposing) return;
        libraryFilterCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); libraryFilterCancellation = cancellation;
        var token = cancellation.Token; int generation = ++libraryFilterGeneration;
        var previous = list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count ? visible[list.SelectedIndex] : null;
        if (displayedCategory.Length > 0) categoryPositions[displayedCategory] = (previous, list.TopIndex);
        var query = new LibraryQuery(filter.Text.Trim(), kinds.SelectedItem as string ?? "Canlı TV",
            groups.SelectedItem as string ?? "Tümü", favoritesOnly.Checked);
        string category = query.Kind + "|" + query.Group;
        string prefix = FavoritePrefix;
        string sourceKey = loadedSourceKey.Length > 0 ? loadedSourceKey : CacheKey();
        string homeChoice = selectedHomeSection;
        var recent = recentItems.Values.ToArray(); var continues = continueItems.Values.ToArray();
        var positions = new Dictionary<string, long>(resumePositions);
        var snapshot = new LibrarySnapshot(channels.ToArray(), prefix,
            new HashSet<string>(favorites, StringComparer.Ordinal), new Dictionary<string, int>(watchCounts), libraryCatalogVersion);
        try
        {
            if (debounce) await Task.Delay(180, token);
            var calculated = await Task.Run(() =>
            {
                var items = LibrarySearch.Filter(snapshot, query, token);
                IReadOnlyList<HomeSection> sections = query.Kind == "Tümü"
                    ? BuildHomeSections(snapshot, sourceKey, recent, continues, positions, token) : Array.Empty<HomeSection>();
                if (query.Kind == "Tümü" && query.Text.Length == 0 && !query.FavoritesOnly && homeChoice.Length > 0)
                {
                    var section = sections.FirstOrDefault(s => s.Title == homeChoice);
                    if (section != null) items = section.Items.Where(x => query.Group == "Tümü" || query.Group == x.Item.Kind + " / " + x.Item.Group).Select(x => x.Item).ToList();
                }
                return (Items: items, Sections: sections);
            }, token);
            var result = calculated.Items;
            if (token.IsCancellationRequested || generation != libraryFilterGeneration ||
                snapshot.CatalogVersion != libraryCatalogVersion || prefix != FavoritePrefix || IsDisposed || Disposing) return;
            (Channel? Selected, int Top) position = categoryPositions.GetValueOrDefault(category, (previous, 0));
            homeSections = calculated.Sections;
            visible = result; displayedCategory = category;
            int index = position.Selected == null ? -1 : result.IndexOf(position.Selected);
            list.SetItems(result, index >= 0 ? index : result.Count > 0 ? 0 : -1);
            if (query.Text.Length == 0) list.TopIndex = position.Top;
            UpdateVisualSummary(); RefreshCinemaCatalog();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed && generation == libraryFilterGeneration) status.Text = "Kütüphane araması tamamlanamadı."; }
        finally { if (ReferenceEquals(libraryFilterCancellation, cancellation)) libraryFilterCancellation = null; }
    }
    private void WaitForLibraryFilterForVerification()
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!libraryFilterTask.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(2); }
        if (!libraryFilterTask.IsCompleted) throw new TimeoutException("Kütüphane araması zaman aşımı.");
        libraryFilterTask.GetAwaiter().GetResult();
    }
}
