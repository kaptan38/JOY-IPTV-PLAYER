namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private void NormalizeSourceInput()
    {
        string value = address.Text.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value[1..^1].Trim();
        if (value.Length == 0) throw new FormatException("Bir M3U dosyası seçin veya sağlayıcının adresini girin.");
        address.Text = value;
        // Portal/API choices remain explicit. Only interchange the two M3U modes.
        if (source.SelectedIndex is not (0 or 1)) return;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            source.SelectedIndex = 1;
            return;
        }
        if (uri?.IsFile == true) { value = uri.LocalPath; address.Text = value; }
        if (!File.Exists(value))
            throw new FormatException("M3U dosyası bulunamadı. Dosya seç düğmesini kullanın; internet bağlantısı http:// veya https:// ile başlamalı.");
        source.SelectedIndex = 0;
    }

    private static void RequirePlaylist(List<Channel> items)
    {
        if (items.Count == 0)
            throw new InvalidDataException("Bu kaynakta oynatılabilir yayın bulunamadı. Sunucu M3U yerine giriş veya hata sayfası döndürmüş olabilir. Bağlantıyı ve aboneliği kontrol edin.");
    }
}
