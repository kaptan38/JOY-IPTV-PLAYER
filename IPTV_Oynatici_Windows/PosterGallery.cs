using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Drawing.Imaging;

namespace JoyIptv;

internal sealed class PosterGallery : ScrollableControl
{
    private IReadOnlyList<Channel> items = Array.Empty<Channel>();
    private readonly Dictionary<string, Bitmap> images = new();
    private readonly HashSet<string> pending = new(), failed = new();
    private readonly HttpClient client = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim downloads = new(4);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Font titleFont = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font smallFont = new("Segoe UI", 9);
    private readonly Font symbolFont = new("Segoe UI", 30, FontStyle.Bold);
    private int hoverIndex = -1;
    private readonly PosterDiskCache disk = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JOY_IPTV_Player", "afis_onbellek"));
    private readonly HashSet<string> visibleUrls = new(StringComparer.Ordinal);
    internal event EventHandler? SelectionChanged;
    internal event EventHandler? ItemActivated;
    internal event EventHandler? ImageReady;
    internal Color Accent { get; set; } = Color.FromArgb(44, 205, 190);
    internal Color Muted { get; set; } = Color.FromArgb(170, 190, 208);
    internal int SelectedIndex { get; private set; } = -1;
    internal int ScrollOffset { get => -AutoScrollPosition.Y; set => AutoScrollPosition = new Point(0, Math.Max(0, value)); }
    private int CardWidth => Math.Clamp((ClientSize.Width - 74) / 4, (int)(135 * DeviceDpi / 96d), (int)(210 * DeviceDpi / 96d));
    private int CardHeight => (int)(CardWidth * 1.38) + 55;
    private int Columns => Math.Clamp((ClientSize.Width - 2) / (CardWidth + 18), 1, 4);

    internal PosterGallery()
    {
        DoubleBuffered = true; AutoScroll = true; TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    internal void SetItems(IReadOnlyList<Channel> value, int selected)
    {
        items = value; SelectedIndex = selected >= 0 && selected < items.Count ? selected : -1;
        AutoScrollMinSize = new Size(0, ((items.Count + Columns - 1) / Columns) * CardHeight + 16);
        Invalidate();
    }

    internal void SelectItem(int index)
    {
        if (index < 0 || index >= items.Count || index == SelectedIndex) return;
        SelectedIndex = index; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal Image? GetPoster(string url)
    {
        if (String.IsNullOrWhiteSpace(url) || IsDisposed) return null;
        if (images.TryGetValue(url, out var image)) return image;
        if (pending.Count < 64 && !failed.Contains(url) && pending.Add(url)) _ = DownloadAsync(url);
        return null;
    }

    private async Task DownloadAsync(string url)
    {
        bool acquired = false; Bitmap? bitmap = null; PosterCacheEntry? cached = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new FormatException();
            await downloads.WaitAsync(lifetime.Token); acquired = true;
            // Obsolete queued cards yield the slot to the newly visible viewport.
            if (items.Count > 0 && !visibleUrls.Contains(url) &&
                !(SelectedIndex >= 0 && SelectedIndex < items.Count && items[SelectedIndex].PosterUrl == url)) return;
            cached = await disk.ReadAsync(url, lifetime.Token);
            if (cached != null && !cached.IsStale) bitmap = await Task.Run(() => PosterDiskCache.Decode(cached.Bytes), lifetime.Token);
            else
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    const int limit = PosterDiskCache.ImageLimit;
                    if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException();
                    using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var memory = new MemoryStream();
                    byte[] buffer = new byte[16384]; int read;
                    while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                    { if (memory.Length + read > limit) throw new InvalidDataException(); await memory.WriteAsync(buffer.AsMemory(0, read), timeout.Token); }
                    byte[] bytes = memory.ToArray();
                    bitmap = await Task.Run(() => PosterDiskCache.Decode(bytes), lifetime.Token);
                    byte[] scaled = await Task.Run(() =>
                    { using var data = new MemoryStream(); bitmap!.Save(data, ImageFormat.Png); return data.ToArray(); }, lifetime.Token);
                    try { await disk.WriteAsync(url, scaled, lifetime.Token); }
                    catch (IOException) { /* Disk failure must not hide a downloaded poster. */ }
                    catch (UnauthorizedAccessException) { }
                }
                catch when (cached != null && !lifetime.IsCancellationRequested)
                { bitmap?.Dispose(); bitmap = await Task.Run(() => PosterDiskCache.Decode(cached.Bytes), lifetime.Token); }
            }
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            if (images.Count >= 128)
            { string first = images.Keys.First(); images[first].Dispose(); images.Remove(first); }
            images[url] = bitmap!; bitmap = null; Invalidate(); ImageReady?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch { if (!IsDisposed) { if (failed.Count >= 512) failed.Clear(); failed.Add(url); } }
        finally
        {
            bitmap?.Dispose(); if (acquired) downloads.Release(); pending.Remove(url);
            if (!IsDisposed && !lifetime.IsCancellationRequested) Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AutoScrollMinSize = new Size(0, ((items.Count + Columns - 1) / Columns) * CardHeight + 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (items.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "Bu görünümde içerik yok. Kaynak ekleyin veya filtreyi değiştirin.",
                titleFont, new Rectangle(20, 40, Math.Max(1, Width - 40), 90), Muted, TextFormatFlags.WordBreak);
            return;
        }
        int firstRow = Math.Max(0, -AutoScrollPosition.Y / CardHeight);
        int lastRow = firstRow + ClientSize.Height / CardHeight + 2;
        visibleUrls.Clear();
        for (int i = firstRow * Columns; i < Math.Min(items.Count, lastRow * Columns); i++) visibleUrls.Add(items[i].PosterUrl);
        for (int index = firstRow * Columns; index < Math.Min(items.Count, lastRow * Columns); index++)
        {
            var item = items[index];
            int x = 12 + (index % Columns) * (CardWidth + 18), y = 10 + (index / Columns) * CardHeight + AutoScrollPosition.Y;
            var poster = new Rectangle(x, y, CardWidth, CardHeight - 55);
            using var path = Rounded(poster, 10);
            using var gradient = new LinearGradientBrush(poster, Color.FromArgb(35, 78, 94),
                item.Kind == "Film" ? Color.FromArgb(120, 69, 64) : Color.FromArgb(51, 56, 98), 45f);
            e.Graphics.FillPath(gradient, path);
            Image? image = GetPoster(item.PosterUrl);
            if (image != null)
            {
                var state = e.Graphics.Save(); e.Graphics.SetClip(path);
                float zoom = Math.Max(poster.Width / (float)image.Width, poster.Height / (float)image.Height);
                var target = new RectangleF(poster.X + (poster.Width - image.Width * zoom) / 2,
                    poster.Y + (poster.Height - image.Height * zoom) / 2, image.Width * zoom, image.Height * zoom);
                e.Graphics.DrawImage(image, target); e.Graphics.Restore(state);
            }
            else
            {
                using var decoration = new Pen(Color.FromArgb(40, 230, 240, 255), 1);
                for (int orbit = 0; orbit < 3; orbit++)
                    e.Graphics.DrawEllipse(decoration, poster.X + poster.Width / 2 - 42 - orbit * 22,
                        poster.Y + 30 - orbit * 14, 84 + orbit * 44, 84 + orbit * 44);
                TextRenderer.DrawText(e.Graphics, item.Name, titleFont,
                    new Rectangle(x + 16, y + poster.Height / 2 - 10, CardWidth - 32, poster.Height / 2 - 30),
                    Color.White, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, "JOY KÜTÜPHANE", smallFont,
                    new Rectangle(x + 12, poster.Bottom - 30, CardWidth - 24, 22), Color.FromArgb(190, 213, 225), TextFormatFlags.SingleLine);
            }
            var badge = new Rectangle(x + 10, y + 10, Math.Min(CardWidth - 20, 74), 25);
            using var badgeFill = new SolidBrush(Color.FromArgb(220, 22, 31, 44));
            using var badgePath = Rounded(badge, 6); e.Graphics.FillPath(badgeFill, badgePath);
            TextRenderer.DrawText(e.Graphics, item.Kind == "Canlı TV" ? "CANLI" : item.Kind.ToUpperInvariant(), smallFont,
                badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (index == SelectedIndex || index == hoverIndex)
            {
                using var pen = new Pen(index == SelectedIndex ? Accent : Muted, index == SelectedIndex ? 3 : 1);
                e.Graphics.DrawPath(pen, path);
                if (index == hoverIndex)
                {
                    var play = new Rectangle(poster.Right - 48, poster.Bottom - 49, 35, 35);
                    using var playFill = new SolidBrush(Accent); e.Graphics.FillEllipse(playFill, play);
                    TextRenderer.DrawText(e.Graphics, "▶", titleFont, play, Color.FromArgb(14, 40, 46),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            TextRenderer.DrawText(e.Graphics, item.Name, titleFont, new Rectangle(x, poster.Bottom + 8, CardWidth, 25),
                ForeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, item.Group, smallFont, new Rectangle(x, poster.Bottom + 34, CardWidth, 20),
                Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath(); int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        int index = HitIndex(e.Location); if (index >= 0) SelectItem(index);
    }
    private int HitIndex(Point point)
    {
        int localY = point.Y - 10 - AutoScrollPosition.Y;
        if (point.X < 12 || localY < 0) return -1;
        int column = (point.X - 12) / (CardWidth + 18), row = localY / CardHeight;
        int index = row * Columns + column;
        return column >= Columns || (point.X - 12) % (CardWidth + 18) >= CardWidth || index >= items.Count ? -1 : index;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); int next = HitIndex(e.Location);
        if (hoverIndex == next) return;
        hoverIndex = next; Cursor = next >= 0 ? Cursors.Hand : Cursors.Default; Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverIndex = -1; Invalidate(); }
    protected override void OnDoubleClick(EventArgs e)
    { base.OnDoubleClick(e); if (HitIndex(PointToClient(Cursor.Position)) >= 0 && SelectedIndex >= 0) ItemActivated?.Invoke(this, e); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter) { ItemActivated?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        int step = e.KeyCode switch { Keys.Left => -1, Keys.Right => 1, Keys.Up => -Columns, Keys.Down => Columns, _ => 0 };
        if (step != 0 && items.Count > 0)
        {
            SelectItem(Math.Clamp(Math.Max(0, SelectedIndex) + step, 0, items.Count - 1));
            int rowY = SelectedIndex / Columns * CardHeight;
            if (rowY < -AutoScrollPosition.Y || rowY + CardHeight > -AutoScrollPosition.Y + ClientSize.Height)
                AutoScrollPosition = new Point(0, rowY);
            e.Handled = true;
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel(); client.Dispose();
            foreach (var image in images.Values) image.Dispose(); images.Clear();
            titleFont.Dispose(); smallFont.Dispose(); symbolFont.Dispose();
        }
        base.Dispose(disposing);
    }
}


