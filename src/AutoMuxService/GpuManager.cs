using System.Diagnostics;
using System.Management;

namespace AutoMuxService;

/// <summary>
/// dGPU cihazını algılama, etkinleştirme ve devre dışı bırakma işlemlerini yönetir.
/// pnputil.exe kullanarak GPU'yu yazılımsal olarak kontrol eder.
/// </summary>
public class GpuManager
{
    private readonly ILogger<GpuManager> _logger;
    private string? _dgpuInstanceId;
    private string? _dgpuName;

    public GpuManager(ILogger<GpuManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// dGPU cihaz bilgisi (algılandıysa)
    /// </summary>
    public string? DgpuName => _dgpuName;
    public string? DgpuInstanceId => _dgpuInstanceId;
    public bool IsDgpuDetected => !string.IsNullOrEmpty(_dgpuInstanceId);

    /// <summary>
    /// WMI üzerinden sisteme takılı discrete GPU'yu otomatik olarak algılar.
    /// NVIDIA veya AMD GPU'ları tanır (Intel iGPU'yu atlar).
    /// </summary>
    public bool DetectDgpu()
    {
        try
        {
            _logger.LogInformation("dGPU algılama başlatılıyor...");

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_VideoController");

            foreach (ManagementObject gpu in searcher.Get())
            {
                var name = gpu["Name"]?.ToString() ?? "";
                var pnpDeviceId = gpu["PNPDeviceID"]?.ToString() ?? "";
                var status = gpu["Status"]?.ToString() ?? "";
                var adapterCompatibility = gpu["AdapterCompatibility"]?.ToString() ?? "";

                _logger.LogDebug(
                    "GPU bulundu: {Name}, PNP: {PnpId}, Durum: {Status}, Üretici: {Vendor}",
                    name, pnpDeviceId, status, adapterCompatibility);

                // Intel/Microsoft Basic adapter'ları atla (bunlar iGPU veya yazılımsal)
                if (IsIntegratedGpu(name, adapterCompatibility))
                {
                    _logger.LogDebug("Atlanan (iGPU/yazılımsal): {Name}", name);
                    continue;
                }

                // NVIDIA veya AMD GPU'yu bulduk
                _dgpuInstanceId = pnpDeviceId;
                _dgpuName = name;
                _logger.LogInformation(
                    "dGPU algılandı: {Name} (ID: {InstanceId})", _dgpuName, _dgpuInstanceId);
                return true;
            }

            _logger.LogWarning("Sistemde discrete GPU bulunamadı.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "dGPU algılama sırasında hata oluştu.");
            return false;
        }
    }

    /// <summary>
    /// dGPU'nun şu anda etkin olup olmadığını kontrol eder.
    /// </summary>
    public bool IsGpuEnabled()
    {
        if (!IsDgpuDetected)
        {
            _logger.LogWarning("dGPU algılanmadı, durum kontrol edilemiyor.");
            return false;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_VideoController WHERE PNPDeviceID = '{EscapeWmiString(_dgpuInstanceId!)}'");

            foreach (ManagementObject gpu in searcher.Get())
            {
                var availability = Convert.ToInt32(gpu["Availability"]);
                var status = gpu["Status"]?.ToString() ?? "";
                var configManagerErrorCode = Convert.ToInt32(gpu["ConfigManagerErrorCode"]);

                // ConfigManagerErrorCode:
                // 0 = Device is working properly
                // 22 = Device is disabled
                bool isEnabled = configManagerErrorCode == 0;

                _logger.LogDebug(
                    "GPU durumu — Availability: {Avail}, Status: {Status}, ErrorCode: {ErrorCode}, Etkin: {Enabled}",
                    availability, status, configManagerErrorCode, isEnabled);

                return isEnabled;
            }

            // GPU WMI'da bulunamadı — muhtemelen tamamen devre dışı
            _logger.LogDebug("GPU WMI sorgusunda bulunamadı, muhtemelen devre dışı.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU durumu kontrol edilirken hata oluştu.");
            return false;
        }
    }

    /// <summary>
    /// dGPU'yu devre dışı bırakır (pnputil /disable-device).
    /// Pil tasarrufu için fiş çıkarıldığında çağrılır.
    /// </summary>
    public async Task<bool> DisableGpuAsync()
    {
        if (!IsDgpuDetected)
        {
            _logger.LogWarning("dGPU algılanmadı, devre dışı bırakılamıyor.");
            return false;
        }

        _logger.LogInformation("dGPU devre dışı bırakılıyor: {Name}", _dgpuName);
        return await ExecutePnpUtilAsync("/disable-device", _dgpuInstanceId!);
    }

    /// <summary>
    /// dGPU'yu etkinleştirir (pnputil /enable-device).
    /// Fiş takıldığında çağrılır.
    /// </summary>
    public async Task<bool> EnableGpuAsync()
    {
        if (!IsDgpuDetected)
        {
            _logger.LogWarning("dGPU algılanmadı, etkinleştirilemiyor.");
            return false;
        }

        _logger.LogInformation("dGPU etkinleştiriliyor: {Name}", _dgpuName);
        return await ExecutePnpUtilAsync("/enable-device", _dgpuInstanceId!);
    }

    /// <summary>
    /// pnputil.exe komutunu çalıştırır.
    /// </summary>
    private async Task<bool> ExecutePnpUtilAsync(string action, string instanceId)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"{action} \"{instanceId}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "pnputil {Action} başarılı. Çıktı: {Output}", action, output.Trim());
                return true;
            }
            else
            {
                _logger.LogError(
                    "pnputil {Action} başarısız (ExitCode: {ExitCode}). Hata: {Error}, Çıktı: {Output}",
                    action, process.ExitCode, error.Trim(), output.Trim());
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "pnputil çalıştırılırken hata oluştu ({Action}).", action);
            return false;
        }
    }

    /// <summary>
    /// GPU'nun entegre (iGPU) veya yazılımsal adaptör olup olmadığını kontrol eder.
    /// </summary>
    private static bool IsIntegratedGpu(string name, string vendor)
    {
        var lowerName = name.ToLowerInvariant();
        var lowerVendor = vendor.ToLowerInvariant();

        // Intel iGPU'lar
        if (lowerVendor.Contains("intel") || lowerName.Contains("intel"))
            return true;

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
}
