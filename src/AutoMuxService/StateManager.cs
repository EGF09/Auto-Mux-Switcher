using Microsoft.Win32;

namespace AutoMuxService;

/// <summary>
/// Registry üzerinden uygulama durumunu yönetir.
/// Son bilinen güç durumunu ve GPU durumunu kalıcı olarak saklar.
/// Bilgisayar kapalıyken fiş değişimi senaryosunu destekler.
/// </summary>
public class StateManager
{
    private const string RegistryKeyPath = @"SOFTWARE\AutoMuxSwitcher";
    private const string LastPowerStateValue = "LastPowerState";  // "AC" veya "Battery"
    private const string LastGpuStateValue = "LastGpuState";      // "Enabled" veya "Disabled"
    private const string DgpuInstanceIdValue = "DgpuInstanceId";  // Algılanan dGPU ID'si
    private const string UserPreferenceValue = "AutoSwitchEnabled"; // Kullanıcı tercihi

    private readonly ILogger<StateManager> _logger;

    public StateManager(ILogger<StateManager> logger)
    {
        _logger = logger;
        EnsureRegistryKeyExists();
    }

    /// <summary>
    /// Son bilinen güç durumunu kaydeder.
    /// </summary>
    /// <param name="isOnAcPower">true = AC, false = Pil</param>
    public void SavePowerState(bool isOnAcPower)
    {
        var state = isOnAcPower ? "AC" : "Battery";
        WriteRegistryValue(LastPowerStateValue, state);
        _logger.LogDebug("Güç durumu kaydedildi: {State}", state);
    }

    /// <summary>
    /// Son bilinen güç durumunu okur.
    /// </summary>
    /// <returns>true = AC, false = Pil, null = kayıt yok</returns>
    public bool? GetLastPowerState()
    {
        var state = ReadRegistryValue(LastPowerStateValue);
        return state switch
        {
            "AC" => true,
            "Battery" => false,
            _ => null
        };
    }

    /// <summary>
    /// Son bilinen GPU durumunu kaydeder.
    /// </summary>
    /// <param name="isEnabled">true = etkin, false = devre dışı</param>
    public void SaveGpuState(bool isEnabled)
    {
        var state = isEnabled ? "Enabled" : "Disabled";
        WriteRegistryValue(LastGpuStateValue, state);
        _logger.LogDebug("GPU durumu kaydedildi: {State}", state);
    }

    /// <summary>
    /// Son bilinen GPU durumunu okur.
    /// </summary>
    /// <returns>true = etkin, false = devre dışı, null = kayıt yok</returns>
    public bool? GetLastGpuState()
    {
        var state = ReadRegistryValue(LastGpuStateValue);
        return state switch
        {
            "Enabled" => true,
            "Disabled" => false,
            _ => null
        };
    }

    /// <summary>
    /// Algılanan dGPU Instance ID'sini kaydeder.
    /// </summary>
    public void SaveDgpuInstanceId(string instanceId)
    {
        WriteRegistryValue(DgpuInstanceIdValue, instanceId);
        _logger.LogDebug("dGPU Instance ID kaydedildi: {Id}", instanceId);
    }

    /// <summary>
    /// Kayıtlı dGPU Instance ID'sini okur.
    /// </summary>
    public string? GetDgpuInstanceId()
    {
        return ReadRegistryValue(DgpuInstanceIdValue);
    }

    /// <summary>
    /// Otomatik geçiş özelliğinin aktif olup olmadığını kontrol eder.
    /// Varsayılan: aktif (true).
    /// </summary>
    public bool IsAutoSwitchEnabled()
    {
        var value = ReadRegistryValue(UserPreferenceValue);
        return value != "false"; // Varsayılan olarak aktif
    }

    /// <summary>
    /// Otomatik geçiş tercihini kaydeder.
    /// </summary>
    public void SetAutoSwitchEnabled(bool enabled)
    {
        WriteRegistryValue(UserPreferenceValue, enabled ? "true" : "false");
        _logger.LogInformation("Otomatik geçiş tercihi: {Enabled}", enabled);
    }

    /// <summary>
    /// Mevcut güç durumu ile son kayıtlı güç durumunu karşılaştırır.
    /// Bilgisayar kapalıyken fiş değişimi olup olmadığını tespit eder.
    /// </summary>
    /// <param name="currentIsOnAc">Mevcut güç durumu</param>
    /// <returns>true = değişiklik var (kapalıyken fiş durumu değişmiş)</returns>
    public bool HasPowerStateChangedWhileOff(bool currentIsOnAc)
    {
        var lastState = GetLastPowerState();
        if (lastState == null)
        {
            _logger.LogInformation("İlk çalıştırma, önceki güç durumu kaydı yok.");
            return false;
        }

        var changed = lastState.Value != currentIsOnAc;
        if (changed)
        {
            _logger.LogInformation(
                "Kapalıyken güç durumu değişmiş: {Last} → {Current}",
                lastState.Value ? "AC" : "Pil",
                currentIsOnAc ? "AC" : "Pil");
        }

        return changed;
    }

    private void EnsureRegistryKeyExists()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath);
            _logger.LogDebug("Registry anahtarı hazır: HKLM\\{Path}", RegistryKeyPath);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "HKLM Registry anahtarı oluşturulamadı (yetki sorunu). HKCU kullanılacak.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registry anahtarı oluşturulurken hata oluştu.");
        }
    }

    private void WriteRegistryValue(string name, string value)
    {
        try
        {
            // Önce HKLM dene (service yetkisiyle çalışır)
            using var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath);
            key?.SetValue(name, value, RegistryValueKind.String);
        }
        catch (UnauthorizedAccessException)
        {
            // Yetki yoksa HKCU kullan
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(name, value, RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry değeri yazılamadı: {Name}", name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registry değeri yazılamadı: {Name}", name);
        }
    }

    private string? ReadRegistryValue(string name)
    {
        try
        {
            // Önce HKLM kontrol et
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            var value = key?.GetValue(name)?.ToString();
            if (value != null) return value;
        }
        catch { /* HKLM okunamadı, HKCU dene */ }

        try
        {
            // HKCU kontrol et
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(name)?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registry değeri okunamadı: {Name}", name);
            return null;
        }
    }
}
