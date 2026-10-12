using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;

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
    internal event EventHandler? SelectionChanged;
    internal event EventHandler? ItemActivated;
    internal event EventHandler? ImageReady;
    internal Color Accent { get; set; } = Color.FromArgb(44, 205, 190);
    internal Color Muted { get; set; } = Color.FromArgb(170, 190, 208);
    internal int SelectedIndex { get; private set; } = -1;
    internal int ScrollOffset { get => -AutoScrollPosition.Y; set => AutoScrollPosition = new Point(0, Math.Max(0, value)); }
    private int CardWidth => Math.Max(130, (int)(164 * DeviceDpi / 96d));
    private int CardHeight => (int)(286 * DeviceDpi / 96d);
    private int Columns => Math.Max(1, (ClientSize.Width - 20) / (CardWidth + 18));

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
        if (images.TryGetValue(url, out var image)) return image;
        if (pending.Count < 64 && !failed.Contains(url) && pending.Add(url)) _ = DownloadAsync(url);
        return null;
    }

    private async Task DownloadAsync(string url)
    {
        bool acquired = false;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new FormatException();
            await downloads.WaitAsync(lifetime.Token); acquired = true;
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            response.EnsureSuccessStatusCode();
            const int limit = 3 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException();
            using var input = await response.Content.ReadAsStreamAsync(lifetime.Token);
            using var memory = new MemoryStream();
            byte[] buffer = new byte[16384]; int read;
            while ((read = await input.ReadAsync(buffer, lifetime.Token)) > 0)
            {
                if (memory.Length + read > limit) throw new InvalidDataException();
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;
            using var original = Image.FromStream(memory);
            if ((long)original.Width * original.Height > 20_000_000) throw new InvalidDataException();
            var bitmap = new Bitmap(original, 220, 330);
            if (IsDisposed) { bitmap.Dispose(); return; }
            if (images.Count >= 128)
            {
                string first = images.Keys.First(); images[first].Dispose(); images.Remove(first);
            }
            images[url] = bitmap; Invalidate(); ImageReady?.Invoke(this, EventArgs.Empty);
        }
        catch { if (!IsDisposed) failed.Add(url); }
        finally { if (acquired) downloads.Release(); pending.Remove(url); }
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
        for (int index = firstRow * Columns; index < Math.Min(items.Count, lastRow * Columns); index++)
        {
            var item = items[index];
            int x = 12 + (index % Columns) * (CardWidth + 18), y = 10 + (index / Columns) * CardHeight + AutoScrollPosition.Y;
            var poster = new Rectangle(x, y, CardWidth, CardHeight - 65);
            using var path = Rounded(poster, 10);
            using var gradient = new LinearGradientBrush(poster, Color.FromArgb(35, 78, 94),
                item.Kind == "Film" ? Color.FromArgb(120, 69, 64) : Color.FromArgb(51, 56, 98), 45f);
            e.Graphics.FillPath(gradient, path);
            Image? image = GetPoster(item.PosterUrl);
            if (image != null)
            {
                var state = e.Graphics.Save(); e.Graphics.SetClip(path); e.Graphics.DrawImage(image, poster); e.Graphics.Restore(state);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, item.Kind == "Canlı TV" ? "TV" : item.Kind == "Film" ? "F" : "D",
                    symbolFont, poster, Color.FromArgb(205, 225, 232), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, item.Kind, smallFont,
                    new Rectangle(x + 9, poster.Bottom - 28, CardWidth - 18, 22), Color.White, TextFormatFlags.HorizontalCenter);
            }
            if (index == SelectedIndex) { using var pen = new Pen(Accent, 3); e.Graphics.DrawPath(pen, path); }
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
        int column = (e.X - 12) / (CardWidth + 18), row = (e.Y - 10 - AutoScrollPosition.Y) / CardHeight;
        if (e.X < 12 || row < 0 || column < 0 || column >= Columns) return;
        if ((e.X - 12) % (CardWidth + 18) >= CardWidth) return;
        SelectItem(row * Columns + column);
    }
    protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); if (SelectedIndex >= 0) ItemActivated?.Invoke(this, e); }
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
