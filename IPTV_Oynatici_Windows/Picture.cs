using LibVLCSharp.Shared;

namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private readonly ComboBox pictureProfile = new() { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
    private string PictureFile => Path.Combine(settingsDir, "goruntu.txt");

    private void SetupPictureControls(FlowLayoutPanel toolbar)
    {
        pictureProfile.Items.AddRange(new object[] { "Renk: Orijinal", "Renk: Dengeli", "Renk: Canlı", "Renk: Net görünüm (VLC)" });
        toolbar.Controls.Add(pictureProfile);
        pictureProfile.SelectedIndexChanged += (_, _) =>
        {
            ApplyPictureProfile();
            try { Directory.CreateDirectory(settingsDir); File.WriteAllText(PictureFile, pictureProfile.SelectedIndex.ToString()); }
            catch { }
        };
        int selected = 0;
        try { if (File.Exists(PictureFile)) selected = Math.Clamp(Int32.Parse(File.ReadAllText(PictureFile)), 0, 3); }
        catch { }
        pictureProfile.SelectedIndex = selected;
        player.Playing += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed) ApplyPictureProfile(); });
        };
    }

    private void ApplyPictureProfile()
    {
        try
        {
            if (pictureProfile.SelectedIndex <= 0)
            { player.SetAdjustInt(VideoAdjustOption.Enable, 0); return; }
            player.SetAdjustInt(VideoAdjustOption.Enable, 1);
            bool vivid = pictureProfile.SelectedIndex == 2;
            bool clear = pictureProfile.SelectedIndex == 3;
            player.SetAdjustFloat(VideoAdjustOption.Contrast, clear ? 1.11f : vivid ? 1.16f : 1.07f);
            player.SetAdjustFloat(VideoAdjustOption.Brightness, vivid ? 1.02f : 1.01f);
            player.SetAdjustFloat(VideoAdjustOption.Saturation, clear ? 1.07f : vivid ? 1.14f : 1.05f);
            player.SetAdjustFloat(VideoAdjustOption.Gamma, clear ? 0.98f : 1.0f);
        }
        catch { /* Video is not ready yet. Playing event retries. */ }
    }
}
