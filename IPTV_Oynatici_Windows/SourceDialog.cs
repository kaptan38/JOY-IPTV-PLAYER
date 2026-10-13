namespace JoyIptv;

internal sealed partial class PlayerForm
{
    private Form? sourceDialog;
    private Label? sourceHint, sourceDialogResult;
    private ProgressBar? sourceDialogProgress;
    private Button? sourceLoadButton;
    private ComboBox? sourceProfiles;
    private readonly ProgressBar cinemaLoadProgress = new() { Dock = DockStyle.Bottom, Height = 3,
        Style = ProgressBarStyle.Marquee, Visible = false };

    private Form BuildCinemaSourcesWindow()
    {
        var window = new Form { Text = "JOY IPTV • Kaynaklar", Size = new Size(860, 590),
            MinimumSize = new Size(720, 540), StartPosition = FormStartPosition.CenterParent,
            BackColor = BackColor, ForeColor = ForeColor, Font = Font, AutoScaleMode = AutoScaleMode.Dpi };
        sourceDialog = window;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
            ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 56, 42, 66, 66, 48, 72, 86 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Dock = DockStyle.Fill, Text = "Kütüphaneni bağla",
            Font = new Font("Segoe UI", 20, FontStyle.Bold) };
        layout.Controls.Add(title, 0, 0);
        var type = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        type.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        type.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        type.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        type.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        type.Controls.Add(new Label { Text = "Kaynak türü", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        source.Dock = DockStyle.Fill; type.Controls.Add(source, 1, 0);
        var pick = Button("Dosya seç", (_, _) => PickFile()); pick.AutoSize = false; pick.Dock = DockStyle.Fill; type.Controls.Add(pick, 2, 0);
        layout.Controls.Add(type, 0, 1);
        layout.Controls.Add(SourceField("Adres veya M3U dosyası", address), 0, 2);
        var credentials = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        credentials.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        credentials.Controls.Add(SourceField("Kullanıcı adı / MAC (portal için)", username), 0, 0);
        credentials.Controls.Add(SourceField("Parola (Xtream için)", password), 1, 0);
        layout.Controls.Add(credentials, 0, 3);
        sourceHint = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        layout.Controls.Add(sourceHint, 0, 4);
        sourceProfiles = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        var profiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        profiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        profiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profiles.Controls.Add(new Label { Text = "Kayıtlı kaynaklar", Dock = DockStyle.Fill }, 0, 0);
        profiles.SetColumnSpan(profiles.GetControlFromPosition(0, 0)!, 4);
        profiles.Controls.Add(sourceProfiles, 0, 1);
        foreach (var action in new[] {
            ("Aç", new EventHandler((_, _) => { savedSourceBox.SelectedIndex = sourceProfiles.SelectedIndex; OpenSource(); })),
            ("Kaydet", new EventHandler((_, _) => SaveSource())),
            ("Sil", new EventHandler((_, _) => { savedSourceBox.SelectedIndex = sourceProfiles.SelectedIndex; RemoveSource(); })) })
        { var button = Button(action.Item1, action.Item2); button.Dock = DockStyle.Fill; profiles.Controls.Add(button, profiles.Controls.Count - 1, 1); }
        layout.Controls.Add(profiles, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        sourceLoadButton = Button("Yükle / yeniden dene", async (_, _) => await LoadAsync());
        sourceLoadButton.Name = "sourceLoad"; sourceLoadButton.Height = 40;
        actions.Controls.Add(sourceLoadButton);
        actions.Controls.Add(Button("Ağ tanısı", async (_, _) => await DiagnoseSourceNetworkAsync()));
        actions.Controls.Add(Button("Kaynak süresi", (_, _) => ShowSourceDates()));
        var close = Button("Kapat / iptal", (_, _) => window.Close()); close.Name = "sourceClose"; actions.Controls.Add(close);
        layout.Controls.Add(actions, 0, 6);
        var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        feedback.RowStyles.Add(new RowStyle(SizeType.Absolute, 7));
        feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sourceDialogProgress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = loadingSource };
        sourceDialogResult = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = status.Text,
            Padding = new Padding(0, 6, 0, 0) };
        feedback.Controls.Add(sourceDialogProgress, 0, 0); feedback.Controls.Add(sourceDialogResult, 0, 1);
        layout.Controls.Add(feedback, 0, 7); window.Controls.Add(layout);
        Color accent = top.Controls.OfType<Button>().FirstOrDefault()?.BackColor ?? Color.FromArgb(159, 85, 66);
        StyleChildren(window, top.BackColor, address.BackColor, ForeColor, detailColor, accent);
        title.ForeColor = ForeColor;
        RefreshSourceDialogProfiles(); UpdateFields(); SetCinemaLoading(loadingSource);
        window.FormClosing += (_, e) =>
        {
            if (!loadingSource) return;
            e.Cancel = true; sourceCloseRequested = true; sourceLoadCancellation?.Cancel();
            sourceDialogResult.Text = "Yükleme iptal ediliyor...";
        };
        window.FormClosed += (_, _) => ReleaseCinemaSourceWindow();
        return window;
    }

    private static TableLayoutPanel SourceField(string caption, TextBox field)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(0, 0, 12, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill }, 0, 0);
        field.Dock = DockStyle.Fill; field.Margin = Padding.Empty; panel.Controls.Add(field, 0, 1);
        return panel;
    }

    private void ReleaseCinemaSourceWindow()
    {
        foreach (Control control in new Control[] { source, address, username, password })
        { control.Parent?.Controls.Remove(control); control.Dock = DockStyle.None; control.Margin = new Padding(4, 0, 4, 0); top.Controls.Add(control); }
        source.Width = 120; address.Width = 330; username.Width = password.Width = 145;
        sourceDialog = null; sourceHint = null; sourceDialogResult = null;
        sourceDialogProgress = null; sourceLoadButton = null; sourceProfiles = null;
    }

    private void UpdateSourceDialogFields()
    {
        if (sourceHint == null) return;
        sourceHint.Text = source.SelectedIndex switch
        {
            2 => "Xtream: sunucu adresi, kullanıcı adı ve parola girin.",
            3 => "Stalker: portal adresi ve size verilen MAC adresini girin.",
            _ => "M3U: internet adresi ve dosya otomatik tanınır. Yanlış tür seçilse de uygun biçime geçilir."
        };
    }

    private void RefreshSourceDialogProfiles()
    {
        if (sourceProfiles == null) return;
        sourceProfiles.Items.Clear();
        sourceProfiles.Items.AddRange(savedSourceBox.Items.Cast<object>().ToArray());
        if (sourceProfiles.Items.Count > 0) sourceProfiles.SelectedIndex = Math.Max(0, savedSourceBox.SelectedIndex);
    }

    private void SetCinemaLoading(bool value)
    {
        cinemaLoadProgress.Visible = value;
        if (sourceDialogProgress != null) sourceDialogProgress.Visible = value;
        if (sourceLoadButton != null) sourceLoadButton.Enabled = !value;
        if (sourceDialog != null) SetSourceInputsEnabled(sourceDialog, !value);
        RefreshCinemaEmptyState();
    }

    private static void SetSourceInputsEnabled(Control parent, bool enabled)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is System.Windows.Forms.Button or TextBox or ComboBox) control.Enabled = enabled || control.Name == "sourceClose";
            else SetSourceInputsEnabled(control, enabled);
        }
    }
}
