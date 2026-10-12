using System.Drawing;
using System.Globalization;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private void ShowEpgSearch()
    {
        if (programmes.Count == 0)
        {
            MessageBox.Show(this, "Önce Ayarlar > Bağlantı ve EPG bölümünden XMLTV program rehberini yükleyin.", "EPG arama");
            return;
        }
        using var window = new Form { Text = "EPG program arama", Width = 760, Height = 490,
            StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var query = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Program veya kanal adı ara..." };
        var results = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "Sonuca çift tıklayınca ilgili kanal açılır." };
        var matches = new List<Channel>();
        void Search()
        {
            matches.Clear(); results.Items.Clear();
            string term = query.Text.Trim();
            if (term.Length < 2) { hint.Text = "Aramak için en az iki harf girin."; return; }
            var compare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
            var options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (var channel in channels.Where(c => c.Kind == "Canlı TV" && c.EpgId.Length > 0))
            {
                if (!programmes.TryGetValue(channel.EpgId, out var entries)) continue;
                foreach (var programme in entries.Where(p => p.End > now && p.Start < now.AddDays(2)))
                {
                    if (compare.IndexOf(channel.Name, term, options) < 0 &&
                        compare.IndexOf(programme.Title, term, options) < 0) continue;
                    matches.Add(channel);
                    results.Items.Add($"{programme.Start.ToLocalTime():dd.MM HH:mm}  |  {channel.Name}  |  {programme.Title}");
                    if (matches.Count >= 250) break;
                }
                if (matches.Count >= 250) break;
            }
            hint.Text = $"{matches.Count} sonuç" + (matches.Count >= 250 ? " (ilk 250)" : "") +
                ". Çift tıklayınca kanal açılır.";
        }
        query.TextChanged += (_, _) => Search();
        results.DoubleClick += async (_, _) =>
        {
            if (results.SelectedIndex < 0 || results.SelectedIndex >= matches.Count) return;
            Channel channel = matches[results.SelectedIndex];
            if (!channels.Contains(channel)) return;
            kinds.SelectedItem = "Canlı TV";
            groups.SelectedIndex = 0; favoritesOnly.Checked = false; filter.Clear();
            int index = visible.FindIndex(c => ReferenceEquals(c, channel));
            if (index < 0) return;
            list.SelectedIndex = index;
            window.Close();
            await PlaySelectedAsync();
        };
        window.Controls.Add(results); window.Controls.Add(query); window.Controls.Add(hint);
        window.ShowDialog(this);
    }
}
