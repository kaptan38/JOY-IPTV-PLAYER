using System.Text;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private bool updatingLibrary;
    private CancellationTokenSource? sourceLoadCancellation;
    private bool sourceCloseRequested;
    private CancellationToken SourceLoadToken => sourceLoadCancellation?.Token ?? CancellationToken.None;

    private IProgress<int> M3uProgress(int generation) => new Progress<int>(count =>
    {
        if (!IsDisposed && generation == stalkerCatalogGeneration && loadingSource)
            status.Text = $"Liste arka planda işleniyor: {count:N0} yayın...";
    });

    private static async Task<(List<Channel> Items, string Header)> ReadM3uStreamAsync(
        Stream stream, Uri? baseUri, IProgress<int> progress, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
        var parser = new M3uAccumulator();
        string header = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? "";
        parser.AddLine(header, baseUri);
        int nextReport = 5000;
        string? line;
        while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
        {
            token.ThrowIfCancellationRequested();
            parser.AddLine(line, baseUri);
            if (parser.Items.Count >= nextReport)
            { progress.Report(parser.Items.Count); nextReport += 5000; }
        }
        return (parser.Items, header);
    }

    private void ReplaceLoadedCatalog(List<Channel> loaded)
    {
        loadedSourceKey = CacheKey(); selectedHomeSection = "";
        libraryCatalogVersion++; categoryPositions.Clear(); displayedCategory = "";
        updatingLibrary = true;
        try { channels.Clear(); channels.AddRange(loaded); OnChannelsLoaded(); currentLibraryKind = kinds.SelectedItem as string ?? "Canlı TV"; }
        finally { updatingLibrary = false; }
        RefreshGroups();
    }
}

