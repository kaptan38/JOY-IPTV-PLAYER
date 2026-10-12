using System.Drawing;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private void ShowSmartAssistant()
    {
        using var window = new Form
        {
            Text = "JOY • Akıllı yardımcı", Width = 730, Height = 540,
            MinimumSize = new Size(590, 400), StartPosition = FormStartPosition.CenterParent,
            BackColor = BackColor, ForeColor = ForeColor, Font = Font
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(18) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var intro = new Label { Dock = DockStyle.Fill,
            Text = "Birden çok kelimeyle arayın: aksiyon 2024 4k\nÖneriler favorileriniz ve izleme geçmişinizle gelişir." };
        var query = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Başlık, kategori, yıl veya kalite..." };
        var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        void Search()
        {
            if (channels.Count == 0) { details.Text = "Önce bir kaynak yükleyin."; return; }
            if (string.IsNullOrWhiteSpace(query.Text)) { query.Focus(); return; }
            favoritesOnly.Checked = false;
            kinds.SelectedItem = "Tümü"; groups.SelectedIndex = 0;
            filter.Text = query.Text.Trim(); ApplyFilter();
            status.Text = $"Akıllı arama: {visible.Count:N0} sonuç.";
            window.Close(); list.Focus();
        }
        actions.Controls.Add(Button("Kütüphanede ara", (_, _) => Search()));
        actions.Controls.Add(Button("Önerileri göster", (_, _) =>
        {
            favoritesOnly.Checked = false; filter.Clear(); kinds.SelectedItem = "Önerilen";
            groups.SelectedIndex = 0; ApplyFilter();
            if (visible.Count == 0)
            { details.Text = "Öneri için birkaç öğeyi favorilere ekleyin veya en az 30 saniye izleyin."; return; }
            window.Close(); list.Focus();
        }));
        actions.Controls.Add(Button("Yayın durumunu açıkla", (_, _) => details.Text = PlaybackAdvice()));
        query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(); }
        };
        var suggestions = RankRecommendations(channels).Take(8).ToList();
        details.Text = suggestions.Count == 0
            ? "Henüz kişisel öneri yok. Favori ekleyerek başlayabilirsiniz.\r\n\r\n" + PlaybackAdvice()
            : "Sizin için öneriler:\r\n\r\n" + string.Join("\r\n", suggestions.Select(c =>
                c.Name + " — " + RecommendationReason(c)));
        layout.Controls.Add(intro, 0, 0); layout.Controls.Add(query, 0, 1);
        layout.Controls.Add(details, 0, 2); layout.Controls.Add(actions, 0, 3);
        window.Controls.Add(layout);
        Color accent = libraryTabs.Values.FirstOrDefault()?.BackColor ?? Color.SteelBlue;
        StyleChildren(window, BackColor, list.BackColor, ForeColor, ForeColor, accent);
        window.ShowDialog(this);
    }

    private string RecommendationReason(Channel item)
    {
        string key = FavoriteKey(item);
        if (favorites.Contains(key)) return "favorilerinizden";
        if (watchCounts.GetValueOrDefault(key) > 0) return "daha önce izlediniz";
        return "izlediğiniz veya favorilediğiniz kategoriden: " + item.Group;
    }

    private string PlaybackAdvice()
    {
        string state = player.State.ToString();
        string advice = state switch
        {
            "Playing" => "Oynatıcı yayını oynatıyor. Görüntüde takılma varsa Ayarlar'dan Kararlı tamponu deneyin.",
            "Buffering" or "Opening" => "Akış bekleniyor. Sunucu gecikmesi veya ağ bağlantısı etkili olabilir. Araçlar > Bağlantı tanıla ile kontrol edin.",
            "Error" => "Oynatıcı akışı açamadı. Kaynak erişimini ve sunucunun yanıtını bağlantı tanısıyla kontrol edin.",
            "Paused" => "Yayın duraklatıldı. Duraklat / sürdür düğmesiyle devam edebilirsiniz.",
            _ => "Bir öğe seçip Enter'a basarak veya çift tıklayarak yayını başlatın."
        };
        return $"Oynatıcı durumu: {state}\r\nCanlı tampon: {SelectedLiveCacheMs / 1000d:0.0} sn" +
            $"\r\nYeniden bağlanma denemesi: {recoveryAttempts}/3\r\n\r\n{advice}" +
            "\r\n\r\nBu açıklama oynatıcı durumuna dayanır; sunucudaki sorunu kesin olarak belirlemez.";
    }
}
