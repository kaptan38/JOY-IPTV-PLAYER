using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private readonly Label librarySummary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 0, 3, 0), Text = "Kütüphane • kaynak bekleniyor" };
    private readonly ToolTip libraryTip = new() { AutoPopDelay = 8000, InitialDelay = 500 };
    private int tooltipIndex = -1;
    private bool cardList = true;
    private Color selectionColor = Color.FromArgb(44, 205, 190);
    private Color detailColor = Color.FromArgb(180, 194, 210);
    private Font? cardTitleFont;
    private Font? cardDetailFont;

    private void SetupVisuals()
    {
        cardTitleFont = new Font("Segoe UI", 10, FontStyle.Bold);
        cardDetailFont = new Font("Segoe UI", 8.5f);
        try { cardList = File.ReadAllText(Path.Combine(settingsDir, "liste_gorunumu.txt")) != "compact"; }
        catch { }
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.IntegralHeight = false;
        list.ItemHeight = (int)Math.Round((cardList ? 62 : 32) * DeviceDpi / 96d);
        list.DrawItem += DrawLibraryItem;
        list.MouseMove += (_, e) =>
        {
            int index = list.IndexFromPoint(e.Location);
            if (index == tooltipIndex) return;
            tooltipIndex = index;
            libraryTip.SetToolTip(list, index >= 0 && index < visible.Count
                ? visible[index].Name + "\n" + visible[index].Kind + " • " + visible[index].Group : "");
        };
        list.MouseLeave += (_, _) => { tooltipIndex = -1; libraryTip.Hide(list); };
        FormClosed += (_, _) => { libraryTip.Dispose(); cardTitleFont?.Dispose(); cardDetailFont?.Dispose(); };
        UpdateVisualSummary();
        ApplyTheme();
    }

    private void ToggleListCards()
    {
        cardList = !cardList;
        int topIndex = list.Items.Count > 0 ? list.TopIndex : 0;
        list.ItemHeight = (int)Math.Round((cardList ? 62 : 32) * DeviceDpi / 96d);
        if (list.Items.Count > 0) list.TopIndex = Math.Min(topIndex, list.Items.Count - 1);
        list.Invalidate();
        try { Directory.CreateDirectory(settingsDir); File.WriteAllText(Path.Combine(settingsDir, "liste_gorunumu.txt"), cardList ? "cards" : "compact"); }
        catch { }
        status.Text = cardList ? "Kart görünümü etkin." : "Kompakt liste etkin.";
    }

    private void UpdateVisualSummary()
    {
        librarySummary.Text = $"{kinds.SelectedItem ?? "Kütüphane"} • {visible.Count:N0} sonuç";
        tooltipIndex = -1;
        libraryTip.SetToolTip(list, "");
    }

    private void DrawLibraryItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= visible.Count) return;
        var item = visible[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool playing = playingChannel == item;
        bool favorite = list.Items[e.Index]?.ToString()?.StartsWith("★ ", StringComparison.Ordinal) == true;
        int scale(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));
        Color ink = selected ? Color.FromArgb(14, 40, 46) : list.ForeColor;
        Color background = selected ? selectionColor : list.BackColor;
        using var canvas = new SolidBrush(list.BackColor);
        e.Graphics.FillRectangle(canvas, e.Bounds);
        var card = new Rectangle(e.Bounds.X + scale(3), e.Bounds.Y + scale(3),
            Math.Max(1, e.Bounds.Width - scale(6)), Math.Max(1, e.Bounds.Height - scale(6)));
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, card);
        int textX = card.X + scale(cardList ? 53 : 10);
        if (cardList)
        {
            var icon = new Rectangle(card.X + scale(8), card.Y + scale(9), scale(35), scale(35));
            Color iconColor = item.Kind == "Canlı TV" ? Color.FromArgb(33, 107, 131) :
                item.Kind == "Film" ? Color.FromArgb(155, 86, 69) : Color.FromArgb(101, 87, 142);
            using var iconBrush = new SolidBrush(iconColor);
            e.Graphics.FillRectangle(iconBrush, icon);
            TextRenderer.DrawText(e.Graphics, item.Kind == "Canlı TV" ? "TV" : item.Kind == "Film" ? "F" : "D",
                cardTitleFont ?? list.Font, icon, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        string marker = playing ? "▶ " : favorite ? "★ " : "";
        var titleRect = new Rectangle(textX, card.Y + scale(cardList ? 7 : 1),
            Math.Max(1, card.Right - textX - scale(7)), scale(24));
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(e.Graphics, marker + item.Name, cardTitleFont ?? list.Font, titleRect, ink, flags);
        if (cardList)
        {
            string subtitle = (kinds.SelectedItem as string) == "Önerilen" ? RecommendationReason(item)
                : item.Kind + " • " + item.Group + EpgLabel(item);
            var detailRect = new Rectangle(textX, card.Y + scale(32), titleRect.Width, scale(20));
            TextRenderer.DrawText(e.Graphics, subtitle, cardDetailFont ?? list.Font, detailRect,
                selected ? Color.FromArgb(29, 69, 75) : detailColor, flags);
        }
        if ((e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, card, ink, background);
    }

    internal void VerifyVisualLayout(string screenshotPath)
    {
        channels.Clear();
        foreach (string name in new[] { "Son Yolculuk", "Gece İstanbul", "Ufkun Ötesi", "Sessiz Şehir", "Mavi Rota", "Yeni Bir Gün", "Gölge", "Kuzey Işığı" })
            channels.Add(new Channel(name, "Örnek Sinema", "", "Film", Description: "Tasarım önizlemesi için örnek içerik. Gerçek yayın veya film bilgisi değildir."));
        channels.Add(new Channel("Örnek Dizi", "Diziler", "", "Dizi"));
        channels.Add(new Channel("TRT 1 HD", "Ulusal", "", "Canlı TV"));
        channels.Add(new Channel("Haber • Canlı", "Haber", "", "Canlı TV"));
        themeBox.SelectedIndex = 9;
        Width = 1400; Height = 900;
        Show(); Application.DoEvents();
        foreach (string kind in new[] { "Canlı TV", "Film", "Dizi" })
        {
            kinds.SelectedItem = kind; RefreshGroups(); Application.DoEvents();
            if (visible.Count == 0 || cinemaMainHost == null || cinemaMainHost.Width < 250 || cinemaMainHost.Height < 100)
                throw new InvalidOperationException("Menü yerleşim kontrolü başarısız: " + kind);
            if (kind != "Canlı TV" && (cinemaGallery == null || !cinemaGallery.Visible))
                throw new InvalidOperationException("Afiş galerisi açılmadı: " + kind);
        }
        kinds.SelectedItem = "Film"; RefreshGroups();
        filter.Text = "Son Yolculuk";
        if (visible.Count != 1) throw new InvalidOperationException("Galeri arama kontrolü başarısız.");
        filter.Clear();
        ToggleFullscreen();
        if (video.Parent != this || cinemaRoot!.Visible) throw new InvalidOperationException("Tam ekran kontrolü başarısız.");
        ToggleFullscreen();
        if (video.Parent != cinemaVideoHost || !cinemaRoot!.Visible) throw new InvalidOperationException("Tam ekran dönüş kontrolü başarısız.");
        kinds.SelectedItem = "Canlı TV"; Application.DoEvents(); ToggleFullscreen(); ToggleFullscreen(); Application.DoEvents();
        if (video.Left < left.Right) throw new InvalidOperationException("Kanal listesi video alanını örtüyor.");
        kinds.SelectedItem = "Film"; RefreshGroups(); list.SelectedIndex = 0;
        status.Text = "Tasarım önizlemesi • örnek içerik • yayın oynatılmıyor";
        // Render the application controls at a fixed viewport independently of
        // the hosted runner's small desktop working area.
        cinemaRoot!.Dock = DockStyle.None;
        cinemaRoot.Size = new Size(1400, 900); cinemaRoot.PerformLayout();
        Application.DoEvents();
        using var bitmap = new Bitmap(cinemaRoot.Width, cinemaRoot.Height);
        cinemaRoot.DrawToBitmap(bitmap, new Rectangle(0, 0, cinemaRoot.Width, cinemaRoot.Height));
        bitmap.Save(screenshotPath, ImageFormat.Png);
        Close();
    }
}
