using System.Management;

namespace AutoMuxTray;

/// <summary>
/// Tray uygulaması tarafında WMI üzerinden dGPU durumunu doğrudan kontrol eden yardımcı sınıf.
/// Service'den bağımsız olarak gerçek GPU durumunu algılar ve periyodik olarak günceller.
/// </summary>
public class GpuStatusChecker : IDisposable
{
    private readonly System.Windows.Forms.Timer _pollingTimer;
    private string? _dgpuInstanceId;
    private string? _dgpuName;
    private bool _isGpuEnabled;
    private bool _disposed;

    /// <summary>
    /// GPU durumu değiştiğinde tetiklenir.
    /// </summary>
    public event EventHandler<GpuStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Algılanan dGPU bilgileri
    /// </summary>
    public string? DgpuName => _dgpuName;
    public string? DgpuInstanceId => _dgpuInstanceId;
    public bool IsDgpuDetected => !string.IsNullOrEmpty(_dgpuInstanceId);
    public bool IsGpuEnabled => _isGpuEnabled;

    public GpuStatusChecker()
    {
        // UI thread'inde çalışacak Windows Forms timer
        _pollingTimer = new System.Windows.Forms.Timer();
        _pollingTimer.Interval = 30_000; // 30 saniye
        _pollingTimer.Tick += OnPollingTimerTick;
    }

    /// <summary>
    /// İlk algılama ve periyodik izlemeyi başlatır.
    /// </summary>
    public void Start()
    {
        // İlk algılama
        DetectDgpu();
        RefreshGpuStatus();

        // Periyodik izlemeyi başlat
        _pollingTimer.Start();
    }

    /// <summary>
    /// Periyodik izlemeyi durdurur.
    /// </summary>
    public void Stop()
    {
        _pollingTimer.Stop();
    }

    /// <summary>
    /// WMI üzerinden sisteme takılı discrete GPU'yu otomatik olarak algılar.
    /// NVIDIA veya AMD GPU'ları tanır (Intel iGPU'yu atlar).
    /// </summary>
    public bool DetectDgpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_VideoController");

            foreach (ManagementObject gpu in searcher.Get())
            {
                var name = gpu["Name"]?.ToString() ?? "";
                var pnpDeviceId = gpu["PNPDeviceID"]?.ToString() ?? "";
                var adapterCompatibility = gpu["AdapterCompatibility"]?.ToString() ?? "";

                // Intel/Microsoft Basic adapter'ları atla (bunlar iGPU veya yazılımsal)
                if (IsIntegratedGpu(name, adapterCompatibility))
                    continue;

                // NVIDIA veya AMD GPU'yu bulduk
                _dgpuInstanceId = pnpDeviceId;
                _dgpuName = name;

                System.Diagnostics.Debug.WriteLine($"dGPU algılandı: {_dgpuName} (ID: {_dgpuInstanceId})");
                return true;
            }

            // dGPU WMI'da bulunamadı, devre dışı olabilir — Registry'den kontrol et
            if (TryGetDgpuFromRegistry())
            {
                System.Diagnostics.Debug.WriteLine($"dGPU Registry'den algılandı: {_dgpuName} (ID: {_dgpuInstanceId})");
                return true;
            }

            System.Diagnostics.Debug.WriteLine("Sistemde discrete GPU bulunamadı.");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"dGPU algılama hatası: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// dGPU'nun şu anda etkin olup olmadığını WMI üzerinden kontrol eder.
    /// </summary>
    public bool CheckGpuEnabled()
    {
        if (!IsDgpuDetected)
            return false;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_VideoController WHERE PNPDeviceID = '{EscapeWmiString(_dgpuInstanceId!)}'");

            foreach (ManagementObject gpu in searcher.Get())
            {
                var configManagerErrorCode = Convert.ToInt32(gpu["ConfigManagerErrorCode"]);

                // ConfigManagerErrorCode:
                // 0 = Device is working properly
                // 22 = Device is disabled
                return configManagerErrorCode == 0;
            }

            // GPU WMI sorgusunda bulunamadı — muhtemelen tamamen devre dışı
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU durumu kontrol hatası: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Win32_PnPEntity üzerinden devre dışı bırakılmış GPU'yu bile algılar.
    /// Win32_VideoController devre dışı GPU'ları göstermeyebilir.
    /// </summary>
    public bool DetectDgpuIncludingDisabled()
    {
        try
        {
            // Önce normal yoldan dene
            if (DetectDgpu())
                return true;

            // Normal yoldan bulunamazsa PnPEntity'den dene
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Display'");

            foreach (ManagementObject device in searcher.Get())
            {
                var name = device["Name"]?.ToString() ?? "";
                var deviceId = device["DeviceID"]?.ToString() ?? "";
                var manufacturer = device["Manufacturer"]?.ToString() ?? "";

                if (IsIntegratedGpu(name, manufacturer))
                    continue;

                // dGPU bulundu (devre dışı olsa bile)
                _dgpuInstanceId = deviceId;
                _dgpuName = name;

                System.Diagnostics.Debug.WriteLine($"dGPU (PnP) algılandı: {_dgpuName} (ID: {_dgpuInstanceId})");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PnP dGPU algılama hatası: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// GPU durumunu günceller ve değişiklik varsa event fırlatır.
    /// </summary>
    public void RefreshGpuStatus()
    {
        try
        {
            // dGPU algılanmadıysa devre dışı dahil dene
            if (!IsDgpuDetected)
                DetectDgpuIncludingDisabled();

            var wasEnabled = _isGpuEnabled;
            _isGpuEnabled = CheckGpuEnabled();

            if (wasEnabled != _isGpuEnabled || !IsDgpuDetected)
            {
                StatusChanged?.Invoke(this, new GpuStatusEventArgs(
                    _dgpuName ?? "dGPU",
                    _isGpuEnabled,
                    IsDgpuDetected));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU durum güncelleme hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Timer event'i — periyodik kontrol
    /// </summary>
    private void OnPollingTimerTick(object? sender, EventArgs e)
    {
        RefreshGpuStatus();
    }

    /// <summary>
    /// Registry'den kaydedilmiş dGPU bilgisini okur.
    /// dGPU devre dışı bırakıldığında WMI'da görünmeyebilir.
    /// </summary>
    private bool TryGetDgpuFromRegistry()
    {
        try
        {
            string registryPath = @"SOFTWARE\AutoMuxSwitcher";

            // HKLM kontrol et
            using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(registryPath);
            var instanceId = hklmKey?.GetValue("DgpuInstanceId")?.ToString();
            if (!string.IsNullOrEmpty(instanceId))
            {
                _dgpuInstanceId = instanceId;
                _dgpuName = "dGPU (Devre Dışı)";
                return true;
            }

            // HKCU kontrol et
            using var hkcuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath);
            instanceId = hkcuKey?.GetValue("DgpuInstanceId")?.ToString();
            if (!string.IsNullOrEmpty(instanceId))
            {
                _dgpuInstanceId = instanceId;
                _dgpuName = "dGPU (Devre Dışı)";
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// GPU'nun entegre (iGPU) veya yazılımsal adaptör olup olmadığını kontrol eder.
    /// Intel UHD/Iris ve AMD Radeon entegre (APU) GPU'larını tanır.
    /// </summary>
    private static bool IsIntegratedGpu(string name, string vendor)
    {
        var lowerName = name.ToLowerInvariant();
        var lowerVendor = vendor.ToLowerInvariant();

        // Intel iGPU'lar (Intel UHD, Intel Iris, vb.)
        if (lowerVendor.Contains("intel") || lowerName.Contains("intel"))
            return true;

        // AMD APU entegre GPU'lar
        // AMD Radeon(TM) Graphics, AMD Radeon Graphics, AMD Radeon Vega gibi isimler
        // NOT: AMD Radeon RX, Radeon Pro gibi discrete kartları HARİÇ tutmalıyız
        if (lowerVendor.Contains("amd") || lowerVendor.Contains("advanced micro"))
        {
            // Kesin discrete GPU isimleri — bunlar iGPU DEĞİL
            if (lowerName.Contains("radeon rx") ||
                lowerName.Contains("radeon pro") ||
                lowerName.Contains("radeon r5") ||
                lowerName.Contains("radeon r7") ||
                lowerName.Contains("radeon r9") ||
                lowerName.Contains("radeon vii") ||
                lowerName.Contains("instinct"))
                return false;

            // "AMD Radeon(TM) Graphics" veya "AMD Radeon Graphics" → APU iGPU
            if (lowerName.Contains("radeon") && lowerName.Contains("graphics") &&
                !lowerName.Contains("rx") && !lowerName.Contains("pro"))
                return true;

            // Radeon Vega entegre (Ryzen APU)
            if (lowerName.Contains("vega") && !lowerName.Contains("vega frontier"))
                return true;
        }

        // Microsoft Basic Display Adapter (yazılımsal)
        if (lowerName.Contains("microsoft basic") || lowerName.Contains("basic display"))
            return true;

        return false;
    }

    /// <summary>
    /// WMI sorgusu için özel karakterleri escape eder.
    /// </summary>
    private static string EscapeWmiString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
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
/// GPU durum event argümanları.
/// </summary>
public class GpuStatusEventArgs : EventArgs
{
    public string GpuName { get; }
    public bool IsEnabled { get; }
    public bool IsDetected { get; }

    public GpuStatusEventArgs(string gpuName, bool isEnabled, bool isDetected)
    {
        GpuName = gpuName;
        IsEnabled = isEnabled;
        IsDetected = isDetected;
    }
}
