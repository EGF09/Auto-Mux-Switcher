using System.Drawing;

namespace AutoMuxTray;

/// <summary>
/// System Tray uygulaması ana context sınıfı.
/// Tray ikonu, sağ tık menüsü ve Service ile iletişimi yönetir.
/// GpuStatusChecker ile doğrudan GPU durumunu izler.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PipeClient _pipeClient;
    private readonly NotificationManager _notificationManager;
    private readonly GpuStatusChecker _gpuStatusChecker;
    private readonly ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _statusMenuItem = null!;
    private ToolStripMenuItem _gpuMenuItem = null!;
    private bool _isGpuEnabled = true;
    private string _gpuName = "dGPU";

    public TrayApplicationContext()
    {
        _pipeClient = new PipeClient();
        _notificationManager = new NotificationManager();
        _gpuStatusChecker = new GpuStatusChecker();

        // Sağ tık menüsünü oluştur
        _contextMenu = CreateContextMenu();

        // Tray ikonunu oluştur
        _notifyIcon = new NotifyIcon();
        _notifyIcon.Icon = GetTrayIcon(true);
        _notifyIcon.Text = "Auto MUX Switcher — Yükleniyor...";
        _notifyIcon.ContextMenuStrip = _contextMenu;
        _notifyIcon.Visible = true;

        _notifyIcon.DoubleClick += OnTrayIconDoubleClick;

        // GPU durum izleyicisini başlat
        _gpuStatusChecker.StatusChanged += OnGpuStatusChanged;
        _gpuStatusChecker.Start();

        // İlk GPU durumuna göre Tray'i güncelle
        _isGpuEnabled = _gpuStatusChecker.IsGpuEnabled;
        _gpuName = _gpuStatusChecker.DgpuName ?? "dGPU";
        UpdateTrayState();

        // Pipe istemcisini başlat
        _pipeClient.MessageReceived += OnServiceMessageReceived;
        _pipeClient.StartListening();

        // Service'den durum bilgisi iste (ek senkronizasyon)
        _ = RequestStatusAsync();
    }

    /// <summary>
    /// GPU durum izleyicisinden gelen güncelleme.
    /// WMI'dan doğrudan okunan gerçek GPU durumu.
    /// </summary>
    private void OnGpuStatusChanged(object? sender, GpuStatusEventArgs e)
    {
        _gpuName = e.GpuName;
        _isGpuEnabled = e.IsEnabled;
        UpdateTrayState();
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
        refreshItem.Click += OnRefreshClick;
        menu.Items.Add(refreshItem);

        menu.Items.Add(new ToolStripSeparator());

        // Çıkış
        var exitItem = new ToolStripMenuItem("❌ Çıkış");
        exitItem.Click += OnExitClick;
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Durumu güncelle butonuna tıklandığında hem WMI hem Service'den güncelle.
    /// </summary>
    private async void OnRefreshClick(object? sender, EventArgs e)
    {
        // Önce WMI'dan doğrudan kontrol et
        _gpuStatusChecker.RefreshGpuStatus();
        _isGpuEnabled = _gpuStatusChecker.IsGpuEnabled;
        _gpuName = _gpuStatusChecker.DgpuName ?? "dGPU";
        UpdateTrayState();

        // Service'den de iste
        await RequestStatusAsync();
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
    /// GPU değişikliği sonrası durumu WMI'dan da doğrula.
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

            // Kısa bir gecikme sonrası WMI'dan doğrula
            var verifyTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            verifyTimer.Tick += (s, e) =>
            {
                verifyTimer.Stop();
                verifyTimer.Dispose();
                _gpuStatusChecker.RefreshGpuStatus();
            };
            verifyTimer.Start();
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
            _notifyIcon.Icon = GetTrayIcon(true);
            _notifyIcon.Text = $"Auto MUX Switcher — {_gpuName}: Aktif";
            _gpuMenuItem.Text = $"🟢 {_gpuName}: Aktif";
        }
        else
        {
            _notifyIcon.Icon = GetTrayIcon(false);
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
        // Güncel WMI durumunu kontrol et
        _gpuStatusChecker.RefreshGpuStatus();
        _isGpuEnabled = _gpuStatusChecker.IsGpuEnabled;
        UpdateTrayState();

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
    /// Tray ikonuna çift tıklandığında durumu günceller.
    /// </summary>
    private async void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        _gpuStatusChecker.RefreshGpuStatus();
        _isGpuEnabled = _gpuStatusChecker.IsGpuEnabled;
        _gpuName = _gpuStatusChecker.DgpuName ?? "dGPU";
        UpdateTrayState();

        await RequestStatusAsync();
    }

    /// <summary>
    /// Çıkış menü öğesine tıklandığında uygulamayı kapatır.
    /// </summary>
    private void OnExitClick(object? sender, EventArgs e)
    {
        _gpuStatusChecker.Stop();
        _gpuStatusChecker.Dispose();
        _pipeClient.StopListening();
        _pipeClient.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }

    /// <summary>
    /// Dosyadan tray ikonunu yükler.
    /// Yeşil = GPU aktif, Sarı/Turuncu = GPU devre dışı
    /// </summary>
    private static Icon GetTrayIcon(bool gpuEnabled)
    {
        try
        {
            string iconFileName = gpuEnabled ? "green_shield.ico" : "yellow_shield.ico";
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconFileName);

            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
            
            // İkon bulunamazsa varsayılanı kullan
            return SystemIcons.Shield;
        }
        catch
        {
            return SystemIcons.Shield;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gpuStatusChecker.Dispose();
            _pipeClient.Dispose();
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
