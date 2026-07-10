using System.Management;
using System.Runtime.InteropServices;

namespace AutoMuxService;

/// <summary>
/// Güç durumu değişikliklerini izler (AC/Pil geçişleri).
/// WMI eventlerini ve polling'i birlikte kullanarak güvenilir izleme sağlar.
/// Windows Service'de çalışabilmek için WinForms yerine P/Invoke kullanır.
/// </summary>
public class PowerMonitor : IDisposable
{
    private readonly ILogger<PowerMonitor> _logger;
    private readonly System.Timers.Timer _pollingTimer;
    private ManagementEventWatcher? _wmiWatcher;
    private bool _lastKnownIsOnAc;
    private bool _disposed;

    // Win32 API - Güç durumu sorgusu
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;           // 0=Offline, 1=Online, 255=Unknown
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    /// <summary>
    /// Güç durumu değiştiğinde tetiklenir.
    /// true = AC (fişe takılı), false = Pil (fişten çıkarılmış)
    /// </summary>
    public event EventHandler<PowerStateChangedEventArgs>? PowerStateChanged;

    public PowerMonitor(ILogger<PowerMonitor> logger)
    {
        _logger = logger;

        // Mevcut güç durumunu al
        _lastKnownIsOnAc = GetCurrentPowerStatus();

        // 5 saniyelik polling timer (yedek mekanizma)
        _pollingTimer = new System.Timers.Timer(5000);
        _pollingTimer.Elapsed += OnPollingTimerElapsed;
        _pollingTimer.AutoReset = true;
    }

    /// <summary>
    /// Mevcut güç durumu: true = AC, false = Pil
    /// </summary>
    public bool IsOnAcPower => _lastKnownIsOnAc;

    /// <summary>
    /// Güç durumu izlemeyi başlatır.
    /// </summary>
    public void Start()
    {
        _logger.LogInformation("Güç durumu izleme başlatılıyor... Mevcut durum: {Status}",
            _lastKnownIsOnAc ? "AC (Fişe takılı)" : "Pil");

        // WMI event watcher'ı başlat
        StartWmiWatcher();

        // Polling timer'ı başlat (yedek mekanizma)
        _pollingTimer.Start();
    }

    /// <summary>
    /// Güç durumu izlemeyi durdurur.
    /// </summary>
    public void Stop()
    {
        _logger.LogInformation("Güç durumu izleme durduruluyor...");

        _pollingTimer.Stop();
        StopWmiWatcher();
    }

    /// <summary>
    /// Win32 API üzerinden mevcut güç durumunu okur.
    /// </summary>
    private static bool GetCurrentPowerStatus()
    {
        if (GetSystemPowerStatus(out var status))
        {
            // ACLineStatus: 0=Offline (pil), 1=Online (AC)
            return status.ACLineStatus == 1;
        }

        // Okunamadıysa varsayılan olarak AC kabul et
        return true;
    }

    /// <summary>
    /// WMI üzerinden güç yönetimi olaylarını dinler.
    /// </summary>
    private void StartWmiWatcher()
    {
        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_PowerManagementEvent");
            _wmiWatcher = new ManagementEventWatcher(query);
            _wmiWatcher.EventArrived += OnWmiPowerEvent;
            _wmiWatcher.Start();
            _logger.LogInformation("WMI güç olayı izleyici başlatıldı.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WMI güç olayı izleyici başlatılamadı. Yalnızca polling kullanılacak.");
        }
    }

    private void StopWmiWatcher()
    {
        if (_wmiWatcher != null)
        {
            try
            {
                _wmiWatcher.Stop();
                _wmiWatcher.Dispose();
                _wmiWatcher = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WMI watcher durdurulurken hata oluştu.");
            }
        }
    }

    /// <summary>
    /// WMI güç olayı geldiğinde çağrılır.
    /// EventType değerleri:
    ///   7  = OEM Event
    ///   10 = Power Status Change
    ///   11 = Entering Suspend
    ///   18 = Resume from Suspend
    /// </summary>
    private void OnWmiPowerEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var eventType = Convert.ToInt32(e.NewEvent.Properties["EventType"].Value);

            _logger.LogDebug("WMI güç olayı alındı. EventType: {EventType}", eventType);

            // EventType 10 = Power Status Change (AC/Pil geçişi)
            if (eventType == 10)
            {
                CheckPowerStatusChange();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WMI güç olayı işlenirken hata oluştu.");
        }
    }

    /// <summary>
    /// Polling timer'ı tarafından periyodik olarak çağrılır.
    /// </summary>
    private void OnPollingTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        CheckPowerStatusChange();
    }

    /// <summary>
    /// Mevcut güç durumunu kontrol eder ve değişiklik varsa event fırlatır.
    /// </summary>
    private void CheckPowerStatusChange()
    {
        try
        {
            var currentIsOnAc = GetCurrentPowerStatus();

            if (currentIsOnAc != _lastKnownIsOnAc)
            {
                var previousIsOnAc = _lastKnownIsOnAc;
                _lastKnownIsOnAc = currentIsOnAc;

                _logger.LogInformation(
                    "Güç durumu değişti: {Previous} → {Current}",
                    previousIsOnAc ? "AC" : "Pil",
                    currentIsOnAc ? "AC" : "Pil");

                PowerStateChanged?.Invoke(this, new PowerStateChangedEventArgs(currentIsOnAc));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Güç durumu kontrol edilirken hata oluştu.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _pollingTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Güç durumu değişikliği event argümanları.
/// </summary>
public class PowerStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// true = AC güce geçildi (fiş takıldı), false = Pil güce geçildi (fiş çıkarıldı)
    /// </summary>
    public bool IsOnAcPower { get; }

    public PowerStateChangedEventArgs(bool isOnAcPower)
    {
        IsOnAcPower = isOnAcPower;
    }
}
