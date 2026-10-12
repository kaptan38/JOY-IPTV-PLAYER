using System.Drawing;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private readonly ComboBox themeBox = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private string ThemeFile => Path.Combine(settingsDir, "tema.txt");

    private void SetupThemes(FlowLayoutPanel toolbar)
    {
        themeBox.Items.AddRange(new object[] { "Gece Antrasit", "Okyanus", "Açık", "Gece Moru",
            "Orman", "Gün Batımı", "Kömür Altın", "Gece Petrol", "Fildişi",
            "Slate Şeftali", "Slate Buz", "Slate Zümrüt" });
        toolbar.Controls.Add(themeBox);
        toolbar.Controls.Add(Button("Görüntü bilgisi", (_, _) => ShowVideoInfo()));
        themeBox.SelectedIndexChanged += (_, _) => ApplyTheme();
        int selected = 9;
        try
        {
            if (File.Exists(ThemeFile)) selected = Math.Clamp(Int32.Parse(File.ReadAllText(ThemeFile)), 0, themeBox.Items.Count - 1);
        }
        catch { /* Default theme. */ }
        themeBox.SelectedIndex = selected;
    }

    private void ShowVideoInfo()
    {
        uint width = 0, height = 0;
        bool hasVideo = player.Size(0, ref width, ref height);
        string resolution = hasVideo ? $"{width} × {height}" : "Yayın başlamadı veya bilgi alınamadı";
        string rate = "Ölçülemiyor", codec = "Bilinmiyor";
        try
        {
            using var media = player.Media;
            object? stats = media?.Statistics;
            object? value = stats?.GetType().GetProperty("InputBitrate")?.GetValue(stats);
            if (value is IConvertible && Convert.ToDouble(value) > 0)
                rate = $"{Convert.ToDouble(value):0.##} (VLC ölçümü)";
            if (media != null)
                foreach (var track in media.Tracks)
                {
                    var type = track.GetType().GetProperty("TrackType")?.GetValue(track)?.ToString();
                    if (type != "Video") continue;
                    var code = track.GetType().GetProperty("Codec")?.GetValue(track);
                    if (code != null) codec = System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(Convert.ToUInt32(code))).TrimEnd('\0');
                    break;
                }
        }
        catch { /* Some streams do not expose statistics. */ }
        var osd = new Form { Text = "Yayın bilgisi", FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, Width = 440, Height = 147,
            BackColor = Color.FromArgb(19, 27, 39), ForeColor = Color.White, Opacity = 0.92,
            Location = PointToScreen(new Point(left.Right + 24, top.Bottom + 24)) };
        var content = new Label { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 12, 8),
            Font = new Font("Segoe UI", 10), ForeColor = Color.White,
            Text = $"{playingChannel?.Name ?? "Yayın bilgisi"}\n" +
                $"Çözünürlük: {resolution}    FPS: {player.Fps:0.##}\n" +
                $"Bit hızı: {rate}    Kodek: {codec}\nSes: {(player.AudioTrackCount > 0 ? "Mevcut" : "Bilgi yok")}    Durum: {player.State}" };
        osd.Controls.Add(content);
        var timer = new System.Windows.Forms.Timer { Interval = 7000 };
        timer.Tick += (_, _) => { timer.Stop(); if (!osd.IsDisposed) osd.Close(); };
        osd.FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); osd.Dispose(); };
        osd.Click += (_, _) => osd.Close();
        osd.Show(this);
        timer.Start();
    }

    private void ApplyTheme()
    {
        int index = Math.Max(0, themeBox.SelectedIndex);
        Color page, panel, input, ink, muted, accent;
        if (index is 9 or 10 or 11)
        {
            page = Color.FromArgb(22, 29, 40); panel = Color.FromArgb(30, 40, 54);
            input = Color.FromArgb(38, 50, 66); ink = Color.FromArgb(238, 242, 247);
            muted = Color.FromArgb(180, 196, 211);
            accent = index == 9 ? Color.FromArgb(159, 85, 66) : index == 10
                ? Color.FromArgb(50, 108, 153) : Color.FromArgb(32, 112, 98);
        }
        else if (index == 2)
        {
            page = Color.FromArgb(238, 243, 249); panel = Color.White;
            input = Color.FromArgb(246, 248, 251); ink = Color.FromArgb(20, 31, 45);
            muted = Color.FromArgb(57, 70, 86); accent = Color.FromArgb(19, 96, 170);
        }
        else if (index == 1)
        {
            page = Color.FromArgb(8, 30, 48); panel = Color.FromArgb(12, 48, 67);
            input = Color.FromArgb(17, 59, 78); ink = Color.FromArgb(238, 249, 253);
            muted = Color.FromArgb(172, 213, 222); accent = Color.FromArgb(0, 122, 154);
        }
        else if (index == 3)
        {
            page = Color.FromArgb(19, 12, 38); panel = Color.FromArgb(39, 25, 65);
            input = Color.FromArgb(54, 39, 77); ink = Color.FromArgb(245, 237, 255);
            muted = Color.FromArgb(198, 175, 220); accent = Color.FromArgb(134, 75, 208);
        }
        else if (index == 4)
        {
            page = Color.FromArgb(11, 29, 25); panel = Color.FromArgb(20, 50, 40);
            input = Color.FromArgb(30, 67, 52); ink = Color.FromArgb(238, 252, 243);
            muted = Color.FromArgb(175, 218, 192); accent = Color.FromArgb(35, 132, 88);
        }
        else if (index == 5)
        {
            page = Color.FromArgb(42, 20, 31); panel = Color.FromArgb(67, 35, 48);
            input = Color.FromArgb(85, 49, 60); ink = Color.FromArgb(255, 244, 233);
            muted = Color.FromArgb(239, 193, 175); accent = Color.FromArgb(205, 90, 65);
        }
        else if (index == 7)
        {
            page = Color.FromArgb(13, 30, 37); panel = Color.FromArgb(22, 46, 54);
            input = Color.FromArgb(34, 60, 67); ink = Color.FromArgb(245, 250, 249);
            muted = Color.FromArgb(182, 214, 209); accent = Color.FromArgb(0, 112, 119);
        }
        else if (index == 8)
        {
            page = Color.FromArgb(236, 233, 225); panel = Color.FromArgb(252, 250, 245);
            input = Color.FromArgb(244, 241, 233); ink = Color.FromArgb(30, 43, 49);
            muted = Color.FromArgb(73, 88, 92); accent = Color.FromArgb(26, 99, 105);
        }
        else if (index == 6)
        {
            page = Color.FromArgb(19, 20, 21); panel = Color.FromArgb(35, 36, 36);
            input = Color.FromArgb(48, 48, 46); ink = Color.FromArgb(251, 244, 219);
            muted = Color.FromArgb(206, 198, 165); accent = Color.FromArgb(157, 118, 42);
        }
        else
        {
            page = Color.FromArgb(25, 29, 37); panel = Color.FromArgb(39, 45, 56);
            input = Color.FromArgb(51, 59, 73); ink = Color.FromArgb(249, 250, 253);
            muted = Color.FromArgb(205, 215, 229); accent = Color.FromArgb(90, 114, 198);
        }
        BackColor = page; ForeColor = ink;
        detailColor = muted;
        StyleChildren(this, panel, input, ink, muted, accent);
        ApplyMenuColors();
        video.BackColor = Color.Black;
        list.Invalidate();
        try { Directory.CreateDirectory(settingsDir); File.WriteAllText(ThemeFile, index.ToString()); }
        catch { /* Theme remains active for this session. */ }
    }

    private void StyleChildren(Control parent, Color panel, Color input, Color ink, Color muted, Color accent)
    {
        foreach (Control control in parent.Controls)
        {
            if (ReferenceEquals(control, video)) continue;
            switch (control)
            {
                case Button button:
                    button.BackColor = accent; button.ForeColor = Color.White;
                    button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = accent;
                    break;
                case TextBox or ComboBox or ListBox:
                    control.BackColor = input; control.ForeColor = ink;
                    break;
                case Panel:
                    control.BackColor = panel; control.ForeColor = ink;
                    break;
                case Label:
                    control.ForeColor = muted;
                    break;
                case CheckBox:
                    control.ForeColor = ink; control.BackColor = panel;
                    break;
                case TrackBar:
                    control.BackColor = panel; control.ForeColor = ink;
                    break;
            }
            if (control.HasChildren) StyleChildren(control, panel, input, ink, muted, accent);
        }
    }
}
