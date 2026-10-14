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
        var snapshot = new LibrarySnapshot(channels.ToArray(), prefix,
            new HashSet<string>(favorites, StringComparer.Ordinal), new Dictionary<string, int>(watchCounts), libraryCatalogVersion);
        try
        {
            if (debounce) await Task.Delay(180, token);
            var result = await Task.Run(() => LibrarySearch.Filter(snapshot, query, token), token);
            if (token.IsCancellationRequested || generation != libraryFilterGeneration ||
                snapshot.CatalogVersion != libraryCatalogVersion || prefix != FavoritePrefix || IsDisposed || Disposing) return;
            var position = categoryPositions.GetValueOrDefault(category, (previous, 0));
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
