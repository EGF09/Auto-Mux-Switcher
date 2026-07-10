using System.Drawing;

namespace AutoMuxTray;

/// <summary>
/// System Tray uygulaması ana context sınıfı.
/// Tray ikonu, sağ tık menüsü ve Service ile iletişimi yönetir.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PipeClient _pipeClient;
    private readonly NotificationManager _notificationManager;
    private readonly ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _statusMenuItem = null!;
    private ToolStripMenuItem _gpuMenuItem = null!;
    private bool _isGpuEnabled = true;
    private string _gpuName = "dGPU";

    public TrayApplicationContext()
    {
        _pipeClient = new PipeClient();
        _notificationManager = new NotificationManager();

        // Sağ tık menüsünü oluştur
        _contextMenu = CreateContextMenu();

        // Tray ikonunu oluştur
        _notifyIcon = new NotifyIcon();
        _notifyIcon.Icon = SystemIcons.Shield;
        _notifyIcon.Text = "Auto MUX Switcher — dGPU Aktif";
        _notifyIcon.ContextMenuStrip = _contextMenu;
        _notifyIcon.Visible = true;

        _notifyIcon.DoubleClick += OnTrayIconDoubleClick;

        // Pipe istemcisini başlat
        _pipeClient.MessageReceived += OnServiceMessageReceived;
        _pipeClient.StartListening();
    }

    /// <summary>
    /// Sağ tık menüsünü oluşturur.
    /// </summary>
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Durum bilgisi (tıklanamaz)
        _statusMenuItem = new ToolStripMenuItem("⚡ Durum: Yükleniyor...")
        {
            Enabled = false
        };
        menu.Items.Add(_statusMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        // GPU durumu
        _gpuMenuItem = new ToolStripMenuItem("🟢 dGPU: Aktif");
        _gpuMenuItem.Click += OnGpuMenuItemClick;
        menu.Items.Add(_gpuMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        // Durum sorgula
        var refreshItem = new ToolStripMenuItem("🔄 Durumu Güncelle");
        refreshItem.Click += async (s, e) => await RequestStatusAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new ToolStripSeparator());

        // Çıkış
        var exitItem = new ToolStripMenuItem("❌ Çıkış");
        exitItem.Click += OnExitClick;
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Service'den gelen mesajları işler.
    /// UI thread'inde çalıştırılır.
    /// </summary>
    private void OnServiceMessageReceived(object? sender, string message)
    {
        // UI thread'ine marshal et
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.Invoke(() => ProcessServiceMessage(message));
        }
        else
        {
            ProcessServiceMessage(message);
        }
    }

    /// <summary>
    /// Service mesajlarını ayrıştırır ve uygun işlemi yapar.
    /// </summary>
    private async void ProcessServiceMessage(string message)
    {
        var parts = message.Split(':');

        switch (parts[0])
        {
            case "POWER_CHANGED":
                await HandlePowerChangedAsync(parts);
                break;

            case "RESULT":
                HandleResult(parts);
                break;

            case "STATUS":
                HandleStatus(parts);
                break;

            default:
                System.Diagnostics.Debug.WriteLine($"Bilinmeyen mesaj: {message}");
                break;
        }
    }

    /// <summary>
    /// Güç değişikliği mesajını işler ve kullanıcıya dialog gösterir.
    /// </summary>
    private async Task HandlePowerChangedAsync(string[] parts)
    {
        if (parts.Length < 2) return;

        var powerState = parts[1]; // "AC" veya "BATTERY"
        var isStartup = parts.Length >= 3 && parts[2] == "STARTUP";

        bool userAccepted;

        if (powerState == "BATTERY")
        {
            // Fiş çıkarıldı — dGPU devre dışı bırakılsın mı?
            userAccepted = _notificationManager.ShowUnpluggedNotification(isStartup);

            if (userAccepted)
            {
                await _pipeClient.SendMessageAsync("RESPONSE:YES:DISABLE");
            }
            else
            {
                await _pipeClient.SendMessageAsync("RESPONSE:NO");
            }
        }
        else if (powerState == "AC")
        {
            // Fiş takıldı — dGPU etkinleştirilsin mi?
            userAccepted = _notificationManager.ShowPluggedInNotification(isStartup);

            if (userAccepted)
            {
                await _pipeClient.SendMessageAsync("RESPONSE:YES:ENABLE");
            }
            else
            {
                await _pipeClient.SendMessageAsync("RESPONSE:NO");
            }
        }
    }

    /// <summary>
    /// İşlem sonucu mesajını işler.
    /// </summary>
    private void HandleResult(string[] parts)
    {
        if (parts.Length < 3) return;

        var resultType = parts[1]; // "SUCCESS" veya "ERROR"
        var detail = parts[2];    // "ENABLED", "DISABLED" veya hata mesajı

        if (resultType == "SUCCESS")
        {
            var isEnabled = detail == "ENABLED";
            _isGpuEnabled = isEnabled;

            UpdateTrayState();
            _notificationManager.ShowResultNotification(_notifyIcon, true, isEnabled);
        }
        else if (resultType == "ERROR")
        {
            _notificationManager.ShowError(_notifyIcon, detail);
        }
    }

    /// <summary>
    /// Durum mesajını işler ve UI'ı günceller.
    /// FORMAT: STATUS:GpuName:ENABLED/DISABLED:AC/BATTERY
    /// </summary>
    private void HandleStatus(string[] parts)
    {
        if (parts.Length < 4) return;

        _gpuName = parts[1];
        _isGpuEnabled = parts[2] == "ENABLED";
        var powerState = parts[3]; // "AC" veya "BATTERY"

        _statusMenuItem.Text = powerState == "AC"
            ? "⚡ Güç: Fişe takılı (AC)"
            : "🔋 Güç: Pil";

        UpdateTrayState();
    }

    /// <summary>
    /// Tray ikonu ve menüsünü güncel GPU durumuna göre günceller.
    /// </summary>
    private void UpdateTrayState()
    {
        if (_isGpuEnabled)
        {
            _notifyIcon.Icon = CreateTrayIcon(true);
            _notifyIcon.Text = $"Auto MUX Switcher — {_gpuName}: Aktif";
            _gpuMenuItem.Text = $"🟢 {_gpuName}: Aktif";
        }
        else
        {
            _notifyIcon.Icon = CreateTrayIcon(false);
            _notifyIcon.Text = $"Auto MUX Switcher — {_gpuName}: Devre Dışı (Pil Tasarrufu)";
            _gpuMenuItem.Text = $"🟠 {_gpuName}: Devre Dışı";
        }
    }

    /// <summary>
    /// Service'den durum bilgisi talep eder.
    /// </summary>
    private async Task RequestStatusAsync()
    {
        await _pipeClient.SendMessageAsync("STATUS_REQUEST");
    }

    /// <summary>
    /// GPU menü öğesine tıklandığında durum bilgisi gösterir.
    /// </summary>
    private void OnGpuMenuItemClick(object? sender, EventArgs e)
    {
        var statusText = _isGpuEnabled
            ? $"Ekran Kartı: {_gpuName}\nDurum: Aktif ✅\n\nGPU performans modunda çalışıyor."
            : $"Ekran Kartı: {_gpuName}\nDurum: Devre Dışı 🔋\n\nPil tasarrufu modunda çalışıyor.";

        MessageBox.Show(
            statusText,
            "Auto MUX Switcher — GPU Durumu",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);
    }

    /// <summary>
    /// Tray ikonuna çift tıklandığında durum sorgular.
    /// </summary>
    private async void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        await RequestStatusAsync();
    }

    /// <summary>
    /// Çıkış menü öğesine tıklandığında uygulamayı kapatır.
    /// </summary>
    private void OnExitClick(object? sender, EventArgs e)
    {
        _pipeClient.StopListening();
        _pipeClient.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }

    // Win32 API - Icon handle'ı serbest bırakmak için
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Basit bir tray ikonu oluşturur (programatik olarak).
    /// Yeşil = GPU aktif, Turuncu = GPU devre dışı
    /// </summary>
    private static Icon CreateTrayIcon(bool gpuEnabled)
    {
        var size = 16; // Tray standart boyut
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        // Arka plan daire
        var bgColor = gpuEnabled
            ? Color.FromArgb(34, 180, 34)   // Yeşil
            : Color.FromArgb(255, 140, 0);  // Turuncu

        using var bgBrush = new SolidBrush(bgColor);
        graphics.FillEllipse(bgBrush, 0, 0, size - 1, size - 1);

        // "G" harfi
        using var font = new Font("Segoe UI", 8, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var textSize = graphics.MeasureString("G", font);
        var x = (size - textSize.Width) / 2;
        var y = (size - textSize.Height) / 2;
        graphics.DrawString("G", font, textBrush, x, y);

        // Icon oluştur ve handle'ı düzgün yönet
        var hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone(); // Clone ile kopyasını al
        DestroyIcon(hIcon); // Orijinal handle'ı serbest bırak
        return icon;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeClient.Dispose();
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
