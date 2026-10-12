using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private readonly HttpClient epgHttp = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    { Timeout = TimeSpan.FromSeconds(90) };
    private string EpgLabel(Channel item)
    {
        if (item.Kind != "Canlı TV" || item.EpgId.Length == 0 ||
            !programmes.TryGetValue(item.EpgId, out var entries)) return "";
        DateTimeOffset now = DateTimeOffset.Now;
        var current = entries.FirstOrDefault(e => e.Start <= now && e.End > now);
        if (current == null) return "";
        var next = entries.FirstOrDefault(e => e.Start >= current.End);
        return "  |  Şimdi: " + current.Title + (next == null ? "" : "  ·  Sırada: " + next.Title);
    }

    private async Task LoadXmltvAsync(bool quiet)
    {
        if (String.IsNullOrWhiteSpace(epgAddress.Text))
        {
            if (!quiet) MessageBox.Show(this, "XMLTV EPG adresini girin.", "EPG");
            return;
        }
        try
        {
            status.Text = "EPG yükleniyor...";
            string url = RequireHttp(epgAddress.Text);
            using var response = await epgHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            const int limit = 64 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit)
                throw new InvalidOperationException("XMLTV dosyası 64 MB sınırını aşıyor.");
            using var incoming = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[32768]; int read;
            while ((read = await incoming.ReadAsync(buffer)) > 0)
            {
                if (memory.Length + read > limit) throw new InvalidOperationException("XMLTV dosyası 64 MB sınırını aşıyor.");
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;
            var relevant = channels.Select(c => c.EpgId).Where(id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var loaded = await Task.Run(() => ParseXmltv(memory, relevant));
            programmes.Clear();
            foreach (var pair in loaded) programmes[pair.Key] = pair.Value;
            ApplyFilter();
            status.Text = $"EPG hazır: {programmes.Count} kanal eşleşti.";
        }
        catch (Exception e)
        {
            status.Text = "EPG yüklenemedi: " + ExplainError(e);
            if (!quiet) MessageBox.Show(this, ExplainError(e), "EPG hatası");
        }
    }

    private static Dictionary<string, List<TvProgramme>> ParseXmltv(Stream stream, HashSet<string> relevant)
    {
        var result = new Dictionary<string, List<TvProgramme>>(StringComparer.OrdinalIgnoreCase);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var xml = XDocument.Load(reader);
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (var row in xml.Descendants("programme"))
        {
            string id = (string?)row.Attribute("channel") ?? "";
            if (!relevant.Contains(id)) continue;
            if (!TryXmltvTime((string?)row.Attribute("start"), out var start) ||
                !TryXmltvTime((string?)row.Attribute("stop"), out var end) || end < now) continue;
            if (!result.TryGetValue(id, out var entries)) result[id] = entries = new List<TvProgramme>();
            entries.Add(new TvProgramme(start, end, (string?)row.Element("title") ?? "Program adı yok"));
        }
        foreach (var entries in result.Values) entries.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    private static bool TryXmltvTime(string? value, out DateTimeOffset time)
    {
        time = default;
        if (value == null) return false;
        var m = Regex.Match(value, @"^(\d{14})(?:\s*([+-])(\d{2})(\d{2}))?");
        if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        var offset = DateTimeOffset.Now.Offset;
        if (m.Groups[2].Success)
        {
            int hours = Int32.Parse(m.Groups[3].Value), minutes = Int32.Parse(m.Groups[4].Value);
            if (hours > 14 || minutes > 59) return false;
            offset = new TimeSpan(hours, minutes, 0);
            if (m.Groups[2].Value == "-") offset = -offset;
        }
        time = new DateTimeOffset(date, offset);
        return true;
    }

    private async Task<bool> ShowUnifiedEpgAsync(Channel item)
    {
        if (item.Kind != "Canlı TV") return false;
        if (item.EpgId.Length > 0 && programmes.TryGetValue(item.EpgId, out var entries))
        {
            var now = DateTimeOffset.Now;
            var upcoming = entries.Where(e => e.End > now).Take(8)
                .Select(e => e.Start.ToLocalTime().ToString("HH:mm") + " – " +
                    e.End.ToLocalTime().ToString("HH:mm") + "  " + e.Title);
            MessageBox.Show(this, String.Join("\n", upcoming), "EPG: " + item.Name);
            return true;
        }
        if (activeSource == 3)
        {
            try
            {
                if (item.EpgId.Length == 0) throw new InvalidOperationException("Kanal için EPG kimliği yok.");
                using var doc = await StalkerRequestAsync("type=itv&action=get_epg_info&ch_id=" +
                    Uri.EscapeDataString(item.EpgId) + "&period=1&JsHttpRequest=1-xml", true);
                var data = doc.RootElement.GetProperty("js");
                if (data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty("data", out var list)) data = list;
                if (data.ValueKind != System.Text.Json.JsonValueKind.Array)
                    throw new InvalidOperationException("Portal EPG listesi döndürmedi.");
                var lines = new List<string>();
                foreach (var row in data.EnumerateArray().Take(8))
                    lines.Add(Str(row, "start") + " – " + Str(row, "end") + "  " +
                        (Str(row, "name").Length > 0 ? Str(row, "name") : Str(row, "title")));
                MessageBox.Show(this, lines.Count > 0 ? String.Join("\n", lines) : "EPG verisi bulunamadı.", "EPG: " + item.Name);
            }
            catch (Exception e) { MessageBox.Show(this, ExplainError(e), "Stalker EPG"); }
            return true;
        }
        if (activeSource is 0 or 1)
        {
            MessageBox.Show(this, "Bu kanalda EPG görünmüyor. XMLTV URL girip EPG yükle'ye basın; tvg-id eşleşmelidir.", "EPG");
            return true;
        }
        return false;
    }
}
