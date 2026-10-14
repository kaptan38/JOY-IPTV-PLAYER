using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private Action? showSettings;
    private readonly List<(string Name, double Latitude, double Longitude)> weatherCities = new();
    private string WeatherCityFile => Path.Combine(settingsDir, "hava_sehri.txt");

    private void SetupSettings(FlowLayoutPanel toolbar)
    {
        var settings = new Form { Text = "Ayarlar", Width = 540, Height = 620,
            StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(440, 480),
            BackColor = BackColor, ForeColor = ForeColor };
        var pages = new TabControl { Dock = DockStyle.Fill };
        var appearance = new TabPage("Görüntü ve tema") { AutoScroll = true };
        var connection = new TabPage("Bağlantı ve EPG") { AutoScroll = true };
        var weather = new TabPage("Hava durumu") { AutoScroll = true };
        pages.TabPages.AddRange(new[] { appearance, connection, weather });
        settings.Controls.Add(pages);

        FlowLayoutPanel Panel(TabPage tab)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
            tab.Controls.Add(panel); return panel;
        }
        var appearancePanel = Panel(appearance);
        var connectionPanel = Panel(connection);
        var weatherPanel = Panel(weather);
        void Move(Control control, FlowLayoutPanel target, int width = 440)
        {
            toolbar.Controls.Remove(control);
            control.Width = width;
            control.Margin = new Padding(4, 7, 4, 7);
            target.Controls.Add(control);
        }
        Move(picture, appearancePanel);
        Move(pictureProfile, appearancePanel);
        Move(themeBox, appearancePanel);
        SetupCinemaPreferences(appearancePanel);
        MoveButton("Görüntü bilgisi", appearancePanel);
        MoveButton("AI araçlarını seç", appearancePanel);
        Move(epgAddress, connectionPanel);
        MoveButton("EPG yükle", connectionPanel);
        MoveButton("Bağlantı tanıla", connectionPanel);
        connectionPanel.Controls.Add(Button("Kaynak DNS / ağ tanısı", async (_, _) => await DiagnoseSourceNetworkAsync()));
        connectionPanel.Controls.Add(Button("Windows VPN ayarlarını aç", (_, _) => OpenWindowsVpnSettings()));
        MoveButton("Kaynak süresi", connectionPanel);
        SetupLiveBuffer(connectionPanel);

        var speed = Button("İnternet indirme hızını ölç (8 MB)", (_, _) => { });
        Move(speed, connectionPanel);
        var speedResult = new Label { Text = "Test sonucu burada gösterilir.", AutoSize = true,
            MaximumSize = new Size(440, 0) };
        connectionPanel.Controls.Add(speedResult);
        speed.Click += async (_, _) => await MeasureSpeedAsync();
        async Task MeasureSpeedAsync()
        {
            speed.Enabled = false; speedResult.Text = "İndirme ölçülüyor...";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                var connectionWatch = Stopwatch.StartNew();
                using var response = await client.GetAsync("https://speed.cloudflare.com/__down?bytes=8388608",
                    HttpCompletionOption.ResponseHeadersRead);
                connectionWatch.Stop();
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                byte[] buffer = new byte[64 * 1024]; long bytes = 0;
                var watch = Stopwatch.StartNew(); int read;
                while ((read = await stream.ReadAsync(buffer)) > 0) bytes += read;
                watch.Stop();
                if (bytes < 4_000_000) throw new IOException("Test dosyası eksik alındı.");
                speedResult.Text = $"İndirme: {bytes * 8d / Math.Max(0.01, watch.Elapsed.TotalSeconds) / 1_000_000:0.0} Mbps " +
                    $"({bytes / 1_048_576d:0.0} MB, {watch.Elapsed.TotalSeconds:0.0} sn). " +
                    $"İlk yanıt: {connectionWatch.ElapsedMilliseconds} ms (DNS ve TLS dâhil). Tek bağlantı örneğidir.";
            }
            catch (Exception e) { speedResult.Text = "Hız ölçülemedi: " + e.Message; }
            finally { speed.Enabled = true; }
        }

        var city = new TextBox { Width = 440, PlaceholderText = "Şehir ara (ör. Ankara)" };
        try { if (File.Exists(WeatherCityFile)) city.Text = File.ReadAllText(WeatherCityFile); } catch { }
        var search = Button("Şehir ara", (_, _) => { });
        var cities = new ListBox { Width = 440, Height = 150 };
        var getWeather = Button("Seçili şehrin hava durumunu göster", (_, _) => { });
        var weatherResult = new Label { Text = "Bir şehir arayın ve sonuçlardan seçin.", AutoSize = true,
            MaximumSize = new Size(440, 0) };
        search.Click += async (_, _) => await SearchCitiesAsync();
        getWeather.Click += async (_, _) => await ShowWeatherAsync();
        weatherPanel.Controls.Add(city); weatherPanel.Controls.Add(search);
        weatherPanel.Controls.Add(cities); weatherPanel.Controls.Add(getWeather); weatherPanel.Controls.Add(weatherResult);
        city.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchCitiesAsync(); } };
        cities.DoubleClick += async (_, _) => await ShowWeatherAsync();
        async Task SearchCitiesAsync()
        {
            if (city.Text.Trim().Length < 2) { weatherResult.Text = "En az iki harf girin."; return; }
            search.Enabled = false; weatherResult.Text = "Şehirler aranıyor...";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                string url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                    Uri.EscapeDataString(city.Text.Trim()) + "&count=8&language=tr&format=json";
                using var doc = JsonDocument.Parse(await client.GetStringAsync(url));
                weatherCities.Clear(); cities.Items.Clear();
                if (doc.RootElement.TryGetProperty("results", out var results))
                    foreach (var item in results.EnumerateArray())
                    {
                        string name = item.GetProperty("name").GetString() ?? "";
                        string country = item.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                        string region = item.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
                        weatherCities.Add(($"{name}, {region}, {country}", item.GetProperty("latitude").GetDouble(),
                            item.GetProperty("longitude").GetDouble()));
                        cities.Items.Add(weatherCities[^1].Name);
                    }
                weatherResult.Text = cities.Items.Count == 0 ? "Şehir bulunamadı." : "Hava durumunu görmek için bir şehir seçin.";
            }
            catch (Exception e) { weatherResult.Text = "Şehir aranamadı: " + e.Message; }
            finally { search.Enabled = true; }
        }
        async Task ShowWeatherAsync()
        {
            if (cities.SelectedIndex < 0 || cities.SelectedIndex >= weatherCities.Count)
            { weatherResult.Text = "Önce bir şehir seçin."; return; }
            var selected = weatherCities[cities.SelectedIndex];
            getWeather.Enabled = false; weatherResult.Text = "Hava durumu alınıyor...";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                string url = $"https://api.open-meteo.com/v1/forecast?latitude={selected.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&longitude={selected.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
                using var doc = JsonDocument.Parse(await client.GetStringAsync(url));
                var current = doc.RootElement.GetProperty("current");
                int code = current.GetProperty("weather_code").GetInt32();
                string condition = code switch
                {
                    0 => "Açık", 1 or 2 => "Parçalı bulutlu", 3 => "Bulutlu", 45 or 48 => "Sisli",
                    51 or 53 or 55 or 56 or 57 => "Çisenti", 61 or 63 or 65 or 66 or 67 => "Yağmurlu",
                    71 or 73 or 75 or 77 => "Karlı", 80 or 81 or 82 => "Sağanak",
                    95 or 96 or 99 => "Gök gürültülü", _ => "Değişken"
                };
                weatherResult.Text = $"{selected.Name}\n{condition}, {current.GetProperty("temperature_2m").GetDouble():0.#} °C" +
                    $" | Hissedilen {current.GetProperty("apparent_temperature").GetDouble():0.#} °C" +
                    $"\nNem %{current.GetProperty("relative_humidity_2m").GetInt32()}" +
                    $" | Rüzgâr {current.GetProperty("wind_speed_10m").GetDouble():0.#} km/sa";
                try { Directory.CreateDirectory(settingsDir); File.WriteAllText(WeatherCityFile, city.Text.Trim()); } catch { }
            }
            catch (Exception e) { weatherResult.Text = "Hava durumu alınamadı: " + e.Message; }
            finally { getWeather.Enabled = true; }
        }

        void RefreshSettingsTheme()
        {
            settings.BackColor = BackColor; settings.ForeColor = ForeColor;
            Color accent = toolbar.Controls.OfType<Button>().FirstOrDefault()?.BackColor ?? Color.SteelBlue;
            Color input = themeBox.SelectedIndex switch
            {
                2 => Color.FromArgb(246, 248, 251),
                1 => Color.FromArgb(17, 59, 78),
                3 => Color.FromArgb(54, 39, 77),
                4 => Color.FromArgb(30, 67, 52),
                5 => Color.FromArgb(85, 49, 60),
                6 => Color.FromArgb(48, 48, 46),
                _ => Color.FromArgb(29, 34, 43)
            };
            StyleChildren(settings, BackColor, input, ForeColor, ForeColor, accent);
        }
        themeBox.SelectedIndexChanged += (_, _) => RefreshSettingsTheme();
        showSettings = () =>
        {
            RefreshSettingsTheme();
            settings.ShowDialog(this);
        };
        FormClosed += (_, _) => settings.Dispose();

        void MoveButton(string caption, FlowLayoutPanel target)
        {
            var button = toolbar.Controls.OfType<Button>().FirstOrDefault(b => b.Text == caption);
            if (button != null) Move(button, target);
        }
    }
}

