namespace AutoMuxSetup;

/// <summary>
/// Ana kurulum formu.
/// Modern koyu tema, 3 büyük buton (Yükle/Onar/Kaldır),
/// ilerleme çubuğu ve log alanı.
/// </summary>
public class SetupForm : Form
{
    // UI bileşenleri
    private readonly Button _installButton;
    private readonly Button _repairButton;
    private readonly Button _uninstallButton;
    private readonly ProgressBar _progressBar;
    private readonly RichTextBox _logBox;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _progressLabel;
    private readonly Button _closeButton;
    private readonly Panel _headerPanel;
    private readonly Panel _buttonPanel;

    // İş mantığı
    private readonly SetupActions _actions;
    private bool _isRunning;

    // Renk paleti — koyu tema
    private static readonly Color BackColor_Dark = Color.FromArgb(24, 24, 32);
    private static readonly Color PanelColor = Color.FromArgb(32, 32, 44);
    private static readonly Color HeaderColor = Color.FromArgb(20, 20, 28);
    private static readonly Color AccentGreen = Color.FromArgb(46, 204, 113);
    private static readonly Color AccentOrange = Color.FromArgb(243, 156, 18);
    private static readonly Color AccentRed = Color.FromArgb(231, 76, 60);
    private static readonly Color TextPrimary = Color.FromArgb(236, 240, 241);
    private static readonly Color TextSecondary = Color.FromArgb(149, 165, 166);
    private static readonly Color ProgressBg = Color.FromArgb(44, 44, 56);
    private static readonly Color LogBg = Color.FromArgb(18, 18, 24);
    private static readonly Color ButtonHoverGreen = Color.FromArgb(39, 174, 96);
    private static readonly Color ButtonHoverOrange = Color.FromArgb(211, 132, 12);
    private static readonly Color ButtonHoverRed = Color.FromArgb(192, 57, 43);

    public SetupForm()
    {
        _actions = new SetupActions();
        _actions.OnProgress = OnProgressUpdate;

        // Form ayarları
        Text = "Auto MUX Switcher — Kurulum Aracı";
        Size = new Size(540, 640);
        MinimumSize = new Size(480, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackColor_Dark;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        // Header panel
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = HeaderColor,
            Padding = new Padding(20, 0, 20, 0)
        };

        _titleLabel = new Label
        {
            Text = "⛨  Auto MUX Switcher",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _headerPanel.Controls.Add(_titleLabel);

        // Durum etiketi
        _statusLabel = new Label
        {
            Text = "Durum kontrol ediliyor...",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextSecondary,
            Location = new Point(24, 78),
            AutoSize = true
        };

        // Buton paneli
        _buttonPanel = new Panel
        {
            Location = new Point(24, 100),
            Size = new Size(476, 230),
            BackColor = Color.Transparent
        };

        // Yükle butonu
        _installButton = CreateActionButton(
            "⬇  YÜKLE",
            "Uygulamayı sisteme kurar veya yeniden kurar",
            AccentGreen,
            ButtonHoverGreen,
            0);
        _installButton.Click += OnInstallClick;

        // Onar butonu
        _repairButton = CreateActionButton(
            "🔧  ONAR",
            "Mevcut kurulumu onarır ve dosyaları günceller",
            AccentOrange,
            ButtonHoverOrange,
            76);
        _repairButton.Click += OnRepairClick;

        // Kaldır butonu
        _uninstallButton = CreateActionButton(
            "✖  KALDIR",
            "Uygulamayı sistemden tamamen kaldırır",
            AccentRed,
            ButtonHoverRed,
            152);
        _uninstallButton.Click += OnUninstallClick;

        _buttonPanel.Controls.AddRange([_installButton, _repairButton, _uninstallButton]);

        // İlerleme etiketi
        _progressLabel = new Label
        {
            Text = "Hazır",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextSecondary,
            Location = new Point(24, 340),
            AutoSize = true
        };

        // İlerleme çubuğu
        _progressBar = new ProgressBar
        {
            Location = new Point(24, 360),
            Size = new Size(476, 8),
            Style = ProgressBarStyle.Continuous,
            BackColor = ProgressBg,
            Maximum = 100,
            Value = 0
        };

        // Log alanı
        _logBox = new RichTextBox
        {
            Location = new Point(24, 378),
            Size = new Size(476, 175),
            BackColor = LogBg,
            ForeColor = TextSecondary,
            Font = new Font("Cascadia Code", 8.5f, FontStyle.Regular, GraphicsUnit.Point, 162),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };

        // Kapat butonu
        _closeButton = new Button
        {
            Text = "Kapat",
            Size = new Size(90, 34),
            Location = new Point(410, 560),
            FlatStyle = FlatStyle.Flat,
            BackColor = PanelColor,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
        _closeButton.FlatAppearance.BorderSize = 1;
        _closeButton.Click += (_, _) => Close();

        // Form'a ekle
        Controls.AddRange([
            _headerPanel,
            _statusLabel,
            _buttonPanel,
            _progressLabel,
            _progressBar,
            _logBox,
            _closeButton
        ]);

        // Form yüklendiğinde durum kontrolü
        Load += OnFormLoad;
    }

    /// <summary>
    /// Form yüklendiğinde mevcut kurulum durumunu kontrol eder.
    /// </summary>
    private void OnFormLoad(object? sender, EventArgs e)
    {
        UpdateInstallationStatus();
    }

    /// <summary>
    /// Kurulum durumunu kontrol edip UI'ı günceller.
    /// </summary>
    private void UpdateInstallationStatus()
    {
        var status = SetupActions.GetInstallationStatus();
        _statusLabel.Text = status.IsInstalled
            ? $"Kurulum durumu: {status.Summary}"
            : "Kurulum durumu: Kurulu değil — Yeni kurulum yapabilirsiniz.";
    }

    // ================================================================
    // Buton Event Handler'ları
    // ================================================================

    private async void OnInstallClick(object? sender, EventArgs e)
    {
        if (_isRunning) return;

        var sourceDir = GetSourceDirectory();
        if (sourceDir == null) return;

        var status = SetupActions.GetInstallationStatus();
        if (status.IsInstalled)
        {
            var confirm = MessageBox.Show(
                "Mevcut kurulum tespit edildi.\n\n" +
                "Devam ederseniz eski kurulum silinip yeniden kurulacak.\nDevam etmek istiyor musunuz?",
                "Kurulum Onayı",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;
        }

        await RunActionAsync("Kurulum", () => _actions.InstallAsync(sourceDir));
    }

    private async void OnRepairClick(object? sender, EventArgs e)
    {
        if (_isRunning) return;

        var sourceDir = GetSourceDirectory();
        if (sourceDir == null) return;

        await RunActionAsync("Onarım", () => _actions.RepairAsync(sourceDir));
    }

    private async void OnUninstallClick(object? sender, EventArgs e)
    {
        if (_isRunning) return;

        var confirm = MessageBox.Show(
            "Auto MUX Switcher tamamen kaldırılacak.\n\n" +
            "Silinecekler:\n" +
            "  • Windows Service\n" +
            "  • Başlangıç kısayolu\n" +
            "  • Kurulum dosyaları (C:\\Program Files\\AutoMuxSwitcher)\n" +
            "  • Registry kayıtları\n\n" +
            "Proje kaynak kodlarınıza dokunulmayacak.\n\n" +
            "Devam etmek istiyor musunuz?",
            "Kaldırma Onayı",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        await RunActionAsync("Kaldırma", () => _actions.UninstallAsync());
    }

    // ================================================================
    // İşlem yürütme ve UI güncelleme
    // ================================================================

    /// <summary>
    /// Bir işlemi asenkron olarak çalıştırır, butonları kilitler ve ilerlemeyi gösterir.
    /// </summary>
    private async Task RunActionAsync(string actionName, Func<Task<bool>> action)
    {
        _isRunning = true;
        SetButtonsEnabled(false);
        _logBox.Clear();
        _progressBar.Value = 0;
        _progressLabel.Text = $"{actionName} başlatılıyor...";

        AppendLog($"══ {actionName} başlatıldı ══");
        AppendLog("");

        bool success;
        try
        {
            success = await action();
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Beklenmeyen hata: {ex.Message}", Color.Red);
            success = false;
        }

        AppendLog("");
        AppendLog(success
            ? $"══ {actionName} başarıyla tamamlandı ══"
            : $"══ {actionName} tamamlandı (uyarılar mevcut) ══");

        UpdateInstallationStatus();
        SetButtonsEnabled(true);
        _isRunning = false;
    }

    /// <summary>
    /// İlerleme güncelleme callback'i — UI thread'inde çalıştırılır.
    /// </summary>
    private void OnProgressUpdate(int percent, string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnProgressUpdate(percent, message));
            return;
        }

        _progressBar.Value = Math.Clamp(percent, 0, 100);
        _progressLabel.Text = message;

        // Mesajı renklendir
        Color color;
        if (message.StartsWith("✅"))
            color = AccentGreen;
        else if (message.StartsWith("⚠️"))
            color = AccentOrange;
        else if (message.StartsWith("❌"))
            color = AccentRed;
        else
            color = TextSecondary;

        AppendLog(message, color);
    }

    /// <summary>
    /// Log alanına renkli metin ekler.
    /// </summary>
    private void AppendLog(string text, Color? color = null)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendLog(text, color));
            return;
        }

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color ?? TextSecondary;
        _logBox.AppendText(text + Environment.NewLine);
        _logBox.ScrollToCaret();
    }

    /// <summary>
    /// Butonları etkinleştirir veya devre dışı bırakır.
    /// </summary>
    private void SetButtonsEnabled(bool enabled)
    {
        _installButton.Enabled = enabled;
        _repairButton.Enabled = enabled;
        _uninstallButton.Enabled = enabled;
        _closeButton.Enabled = enabled;

        _installButton.BackColor = enabled ? AccentGreen : Color.FromArgb(60, 60, 70);
        _repairButton.BackColor = enabled ? AccentOrange : Color.FromArgb(60, 60, 70);
        _uninstallButton.BackColor = enabled ? AccentRed : Color.FromArgb(60, 60, 70);
    }

    // ================================================================
    // Yardımcı Metodlar
    // ================================================================

    /// <summary>
    /// Kaynak dizinini tespit eder.
    /// Setup EXE'sinin yanındaki service/ ve tray/ klasörlerini arar.
    /// Bulamazsa kullanıcıya seçtirir.
    /// </summary>
    private string? GetSourceDirectory()
    {
        // Önce exe'nin yanında ara
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;

        // Exe'nin yanındaki publish dizini yapısını kontrol et
        // Yapı: setup/AutoMuxSetup.exe, service/..., tray/...
        // Üst dizin service ve tray içermeli
        var parentDir = Path.GetDirectoryName(exeDir.TrimEnd(Path.DirectorySeparatorChar));
        if (parentDir != null &&
            Directory.Exists(Path.Combine(parentDir, "service")) &&
            Directory.Exists(Path.Combine(parentDir, "tray")))
        {
            return parentDir;
        }

        // Exe'nin kendi dizininde de kontrol et
        if (Directory.Exists(Path.Combine(exeDir, "service")) &&
            Directory.Exists(Path.Combine(exeDir, "tray")))
        {
            return exeDir;
        }

        // Bulunamadı — kullanıcıya sor
        using var dialog = new FolderBrowserDialog
        {
            Description = "publish klasörünü seçin (service/ ve tray/ alt klasörlerini içeren dizin):",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var selected = dialog.SelectedPath;
            if (Directory.Exists(Path.Combine(selected, "service")) &&
                Directory.Exists(Path.Combine(selected, "tray")))
            {
                return selected;
            }

            MessageBox.Show(
                "Seçilen dizinde service/ ve tray/ klasörleri bulunamadı.\n\n" +
                "Önce projeyi derleyin:\n" +
                "  dotnet publish src/AutoMuxService -c Release -o publish/service\n" +
                "  dotnet publish src/AutoMuxTray -c Release -o publish/tray",
                "Kaynak Dosyalar Bulunamadı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        return null;
    }

    /// <summary>
    /// Bir aksiyon butonu oluşturur (Yükle/Onar/Kaldır).
    /// Hover efekti ve açıklama satırı dahil.
    /// </summary>
    private Button CreateActionButton(string title, string description, Color bgColor, Color hoverColor, int yOffset)
    {
        var button = new Button
        {
            Size = new Size(476, 66),
            Location = new Point(0, yOffset),
            FlatStyle = FlatStyle.Flat,
            BackColor = bgColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0),
            Cursor = Cursors.Hand,
            Text = $"{title}\n",
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hoverColor;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(
            Math.Max(0, hoverColor.R - 20),
            Math.Max(0, hoverColor.G - 20),
            Math.Max(0, hoverColor.B - 20));

        // Çift satır: başlık + açıklama
        button.Paint += (sender, e) =>
        {
            if (sender is not Button btn) return;

            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            var descFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            var textColor = btn.Enabled ? Color.White : Color.FromArgb(140, 140, 150);
            var descColor = btn.Enabled ? Color.FromArgb(255, 255, 255, 180) : Color.FromArgb(100, 100, 110);

            using var titleBrush = new SolidBrush(textColor);
            using var descBrush = new SolidBrush(descColor);

            e.Graphics.DrawString(title, titleFont, titleBrush, 18, 12);
            e.Graphics.DrawString(description, descFont, descBrush, 18, 36);

            titleFont.Dispose();
            descFont.Dispose();
        };

        // Varsayılan text rendering'i gizle
        button.Text = "";

        return button;
    }
}
