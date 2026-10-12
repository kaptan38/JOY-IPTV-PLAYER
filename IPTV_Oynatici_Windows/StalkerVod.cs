using System.Text.Json;
using System.Net;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private string stalkerCatalogNote = "";

    private static JsonElement StalkerJs(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("js", out var js)) return js;
        return root;
    }

    private static JsonElement StalkerRows(JsonElement root)
    {
        var js = StalkerJs(root);
        if (js.ValueKind == JsonValueKind.Object && js.TryGetProperty("data", out var data)) return data;
        return js;
    }

    private async Task LoadStalkerVodAsync(List<Channel> output, int generation)
    {
        var failures = new List<string>();
        foreach (var (section, kind) in new[] { ("vod", "Film"), ("series", "Dizi") })
        {
            if (generation != stalkerCatalogGeneration || IsDisposed) return;
            try
            {
                var categories = new Dictionary<string, (string Name, int Position)>();
                try
                {
                    using var categoryDoc = await StalkerRequestAsync($"type={section}&action=get_categories&JsHttpRequest=1-xml", true);
                    var categoryRows = StalkerRows(categoryDoc.RootElement);
                    if (categoryRows.ValueKind == JsonValueKind.Array)
                        foreach (var category in categoryRows.EnumerateArray())
                        {
                            string id = Str(category, "id");
                            if (id.Length == 0) id = Str(category, "category_id");
                            if (id.Length > 0 && !categories.ContainsKey(id))
                            {
                                string title = Str(category, "title");
                                if (title.Length == 0) title = Str(category, "category_name");
                                categories[id] = (title, categories.Count);
                            }
                        }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { throw; }
                catch (Exception) { /* Categories are optional in some portals. */ }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int page = 1; page <= 300; page++)
                {
                    if (generation != stalkerCatalogGeneration || IsDisposed) return;
                    if ((page == 1 || page % 10 == 0) && (playingChannel == null || !player.IsPlaying))
                        status.Text = $"Stalker {kind} kataloğu alınıyor: sayfa {page}...";
                    using var doc = await StalkerRequestAsync($"type={section}&action=get_ordered_list&p={page}&JsHttpRequest=1-xml", true);
                    var js = StalkerJs(doc.RootElement);
                    var rows = StalkerRows(doc.RootElement);
                    if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0) break;
                    int added = 0;
                    foreach (var row in rows.EnumerateArray())
                    {
                        string id = Str(row, "id"), name = Str(row, "name");
                        if (id.Length == 0 || name.Length == 0 || !seen.Add(id)) continue;
                        string categoryId = Str(row, "category_id");
                        if (categoryId.Length == 0) categoryId = Str(row, "genre_id");
                        bool series = section == "series" || Str(row, "is_series") == "1";
                        string itemKind = series ? "Dizi" : kind;
                        string command = series ? section + ":" + id : Str(row, "cmd");
                        string directUrl = series ? "" : Str(row, "url");
                        if (!series && command.Length == 0 && directUrl.Length == 0) continue;
                        if (!categories.TryGetValue(categoryId, out var category))
                        {
                            category = (categoryId.Length > 0 ? "Kategori " + categoryId : "Genel", categories.Count);
                            categories[categoryId] = category;
                        }
                        output.Add(new Channel(name, !string.IsNullOrEmpty(category.Name) ? category.Name : "Genel",
                            directUrl, itemKind, command.Length > 0 ? command : null,
                            category.Position, output.Count + 1));
                        added++;
                    }
                    if (added == 0) break;
                    if (page == 1 || page % 10 == 0) PublishStalkerCatalogProgress(output, generation);
                    if (js.ValueKind == JsonValueKind.Object &&
                        int.TryParse(Str(js, "total_items"), out int total) && total > 0 && seen.Count >= total) break;
                    if (page == 300) failures.Add(kind + " listesi çok büyük; ilk 300 sayfa alındı.");
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                failures.Add("Portal istek sınırına ulaştı (HTTP 429). Alınan katalog korundu; daha sonra tekrar Yükle'yi deneyin.");
                break;
            }
            catch (Exception) { failures.Add(kind + " listesi portal tarafından alınamadı."); }
        }
        stalkerCatalogNote = String.Join(" ", failures);
    }

    private void PublishStalkerCatalogProgress(List<Channel> items, int generation)
    {
        if (generation != stalkerCatalogGeneration || IsDisposed) return;
        if (catalogBeforeEpisodes != null)
        {
            catalogBeforeEpisodes = items.OrderBy(c => c.Kind == "Canlı TV" ? 0 : c.Kind == "Film" ? 1 : 2)
                .ThenBy(c => c.BouquetOrder).ThenBy(c => c.ChannelOrder).ToList();
            return;
        }
        string selectedKind = kinds.SelectedItem as string ?? "Canlı TV";
        string selectedGroup = groups.SelectedItem as string ?? "Tümü";
        channels.Clear();
        channels.AddRange(items.OrderBy(c => c.Kind == "Canlı TV" ? 0 : c.Kind == "Film" ? 1 : 2)
            .ThenBy(c => c.BouquetOrder).ThenBy(c => c.ChannelOrder));
        RefreshGroups();
        if (kinds.Items.Contains(selectedKind)) kinds.SelectedItem = selectedKind;
        if (groups.Items.Contains(selectedGroup)) groups.SelectedItem = selectedGroup;
    }

    private async Task LoadStalkerEpisodesAsync(Channel item, int attemptGeneration)
    {
        string[] identity = (item.Command ?? "").Split(':', 2);
        if (identity.Length != 2 || identity[1].Length == 0) throw new InvalidOperationException("Dizi kimliği geçersiz.");
        string section = identity[0], id = identity[1];
        var episodes = new List<Channel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int page = 1; page <= 100; page++)
        {
            if (attemptGeneration != recoveryGeneration) return;
            using var doc = await StalkerRequestAsync($"type={section}&action=get_ordered_list&movie_id=" +
                Uri.EscapeDataString(id) + $"&p={page}&JsHttpRequest=1-xml", true);
            var js = StalkerJs(doc.RootElement);
            var rows = StalkerRows(doc.RootElement);
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0) break;
            int added = 0;
            foreach (var season in rows.EnumerateArray())
            {
                string cmd = Str(season, "cmd");
                string seasonName = Str(season, "name");
                if (cmd.Length == 0) continue;
                if (season.TryGetProperty("series", out var numbers) && numbers.ValueKind is
                    (JsonValueKind.Array or JsonValueKind.String))
                {
                    var entries = numbers.ValueKind == JsonValueKind.Array ?
                        numbers.EnumerateArray().Select(ep => ep.ValueKind == JsonValueKind.Object ? Str(ep, "id") : ep.ToString()) :
                        (numbers.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
                    foreach (var number in entries)
                    {
                        if (number.Length == 0 || !seen.Add(cmd + "|" + number)) continue;
                        episodes.Add(new Channel($"{item.Name} - {seasonName} Bölüm {number}", seasonName,
                            "", "Bölüm", EncodeStalkerEpisode(section, cmd, number), ChannelOrder: episodes.Count));
                        added++;
                    }
                }
                else if (seen.Add(cmd))
                {
                    episodes.Add(new Channel($"{item.Name} - {seasonName}", seasonName, "", "Bölüm",
                        EncodeStalkerEpisode(section, cmd, "0"), ChannelOrder: episodes.Count));
                    added++;
                }
            }
            if (added == 0) break;
            if (js.ValueKind == JsonValueKind.Object && int.TryParse(Str(js, "total_items"), out int total) &&
                int.TryParse(Str(js, "max_page_items"), out int size) && size > 0 && page * size >= total) break;
        }
        if (attemptGeneration != recoveryGeneration) return;
        if (episodes.Count == 0) throw new InvalidOperationException("Portal bu dizi için bölüm listesi göndermedi.");
        catalogBeforeEpisodes = channels.ToList();
        channels.Clear(); channels.AddRange(episodes); filter.Clear(); kinds.SelectedItem = "Bölüm"; RefreshGroups();
        status.Text = $"{episodes.Count} bölüm listelendi. Dizilere dönmek için Yükle'ye basın.";
    }

    private static string EncodeStalkerEpisode(string section, string cmd, string episode) =>
        section + "|" + Uri.EscapeDataString(cmd) + "|" + Uri.EscapeDataString(episode);

    private async Task<string> ResolveStalkerPlayUrlAsync(Channel item)
    {
        string command = item.Command ?? "", section = item.Kind == "Canlı TV" ? "itv" : "vod", series = "";
        if (item.Kind == "Bölüm")
        {
            string[] parts = command.Split('|', 3);
            if (parts.Length != 3) throw new InvalidOperationException("Bölüm bağlantısı eksik.");
            section = parts[0]; command = Uri.UnescapeDataString(parts[1]);
            series = "&series=" + Uri.EscapeDataString(Uri.UnescapeDataString(parts[2]));
        }
        string direct = command.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase) ? command[7..].Trim() :
            command.StartsWith("ffrt ", StringComparison.OrdinalIgnoreCase) ? command[5..].Trim() : command.Trim();
        if (item.Kind != "Canlı TV" && item.Kind != "Bölüm" &&
            Uri.TryCreate(direct, UriKind.Absolute, out var existing) && existing.Scheme is ("http" or "https"))
        {
            // Some portals return a final VOD link and have no create_link endpoint for it.
            return direct;
        }
        using var doc = await StalkerRequestAsync($"type={section}&action=create_link&cmd=" +
            Uri.EscapeDataString(command) + series + "&JsHttpRequest=1-xml", true);
        var js = StalkerJs(doc.RootElement);
        string url = Str(js, "cmd").Trim();
        if (url.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase)) url = url[7..].Trim();
        else if (url.StartsWith("ffrt ", StringComparison.OrdinalIgnoreCase)) url = url[5..].Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Portal oynatma bağlantısı göndermedi.");
        return url;
    }
}
