using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoMuxSetup;

/// <summary>
/// Kurulum, onarım ve kaldırma işlemlerini yürüten sınıf.
/// Tüm işlemler admin yetkisiyle çalışır (app.manifest tarafından garanti edilir).
/// </summary>
public class SetupActions
{
    // Yapılandırma sabitleri
    private const string InstallDir = @"C:\Program Files\AutoMuxSwitcher";
    private const string ServiceName = "AutoMuxSwitcher";
    private const string LegacyServiceName = "AutoMuxSwitcherService";
    private const string TrayExeName = "AutoMuxTray.exe";
    private const string TrayProcessName = "AutoMuxTray";
    private const string ServiceExeName = "AutoMuxService.exe";
    private const string RegistryKeyPath = @"SOFTWARE\AutoMuxSwitcher";

    private static string ServiceDir => Path.Combine(InstallDir, "service");
    private static string TrayDir => Path.Combine(InstallDir, "tray");
    private static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "AutoMuxTray.lnk");

    /// <summary>
    /// İlerleme bildirimi için delegate.
    /// </summary>
    public Action<int, string>? OnProgress;

    private void Report(int percent, string message)
    {
        OnProgress?.Invoke(percent, message);
    }

    // ================================================================
    // YÜKLE (Install)
    // ================================================================

    /// <summary>
    /// Tam kurulumu gerçekleştirir.
    /// Eski kurulum varsa önce temizler, sonra yeni kurulumu yapar.
    /// </summary>
    public async Task<bool> InstallAsync(string sourceDir)
    {
        try
        {
            var sourceService = Path.Combine(sourceDir, "service");
            var sourceTray = Path.Combine(sourceDir, "tray");

            // Kaynak dosya kontrolü
            if (!ValidateSourceFiles(sourceService, sourceTray))
                return false;

            // 1. Tray uygulamasını kapat
            Report(5, "Tray uygulaması kapatılıyor...");
            StopTrayApp();

            // 2. Mevcut Service'i durdur ve sil
            Report(15, "Mevcut Service kontrol ediliyor...");
            StopAndDeleteService(ServiceName);
            StopAndDeleteService(LegacyServiceName);

            // 3. Eski kurulum dosyalarını temizle
            Report(25, "Eski kurulum temizleniyor...");
            CleanInstallDirectory();

            // 4. Dizinleri oluştur
            Report(35, "Kurulum dizinleri oluşturuluyor...");
            Directory.CreateDirectory(ServiceDir);
            Directory.CreateDirectory(TrayDir);

            // 5. Dosyaları kopyala
            Report(45, "Service dosyaları kopyalanıyor...");
            CopyDirectory(sourceService, ServiceDir);

            Report(55, "Tray dosyaları kopyalanıyor...");
            CopyDirectory(sourceTray, TrayDir);

            // 6. Windows Service oluştur
            Report(65, "Windows Service oluşturuluyor...");
            if (!CreateWindowsService())
                return false;

            // 7. Service'i başlat
            Report(75, "Service başlatılıyor...");
            StartService();

            // 8. Başlangıç kısayolu oluştur
            Report(85, "Başlangıç kısayolu oluşturuluyor...");
            CreateStartupShortcut();

            // 9. Tray uygulamasını başlat
            Report(90, "Tray uygulaması başlatılıyor...");
            StartTrayApp();

            // 10. Doğrulama
            Report(95, "Kurulum doğrulanıyor...");
            await Task.Delay(1500);
            var result = VerifyInstallation();

            Report(100, result
                ? "✅ Kurulum başarıyla tamamlandı!"
                : "⚠️ Kurulum tamamlandı ancak bazı bileşenler doğrulanamadı.");

            return result;
        }
        catch (Exception ex)
        {
            Report(100, $"❌ Kurulum hatası: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    // ONAR (Repair)
    // ================================================================

    /// <summary>
    /// Mevcut kurulumu onarır — dosyaları günceller, servisi yeniden başlatır.
    /// </summary>
    public async Task<bool> RepairAsync(string sourceDir)
    {
        try
        {
            var sourceService = Path.Combine(sourceDir, "service");
            var sourceTray = Path.Combine(sourceDir, "tray");

            if (!ValidateSourceFiles(sourceService, sourceTray))
                return false;

            // 1. Tray'i kapat
            Report(10, "Tray uygulaması kapatılıyor...");
            StopTrayApp();

            // 2. Service'i durdur
            Report(20, "Service durduruluyor...");
            StopService(ServiceName);

            // 3. Dosyaları güncelle
            Report(35, "Service dosyaları güncelleniyor...");
            Directory.CreateDirectory(ServiceDir);
            CopyDirectory(sourceService, ServiceDir);

            Report(50, "Tray dosyaları güncelleniyor...");
            Directory.CreateDirectory(TrayDir);
            CopyDirectory(sourceTray, TrayDir);

            // 4. Service'i kontrol et, yoksa oluştur
            Report(65, "Service kontrol ediliyor...");
            if (!IsServiceInstalled(ServiceName))
            {
                Report(70, "Service bulunamadı, yeniden oluşturuluyor...");
                CreateWindowsService();
            }

            // 5. Service'i başlat
            Report(75, "Service başlatılıyor...");
            StartService();

            // 6. Başlangıç kısayolunu güncelle
            Report(85, "Başlangıç kısayolu güncelleniyor...");
            CreateStartupShortcut();

            // 7. Tray'i başlat
            Report(90, "Tray uygulaması başlatılıyor...");
            StartTrayApp();

            // 8. Doğrulama
            Report(95, "Onarım doğrulanıyor...");
            await Task.Delay(1500);
            var result = VerifyInstallation();

            Report(100, result
                ? "✅ Onarım başarıyla tamamlandı!"
                : "⚠️ Onarım tamamlandı ancak bazı bileşenler doğrulanamadı.");

            return result;
        }
        catch (Exception ex)
        {
            Report(100, $"❌ Onarım hatası: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    // KALDIR (Uninstall)
    // ================================================================

    /// <summary>
    /// Uygulamayı tamamen kaldırır.
    /// </summary>
    public async Task<bool> UninstallAsync()
    {
        try
        {
            // 1. Tray'i kapat
            Report(10, "Tray uygulaması kapatılıyor...");
            StopTrayApp();

            // 2. Service'i durdur ve sil
            Report(25, "Windows Service kaldırılıyor...");
            StopAndDeleteService(ServiceName);
            StopAndDeleteService(LegacyServiceName);

            // 3. Başlangıç kısayolunu sil
            Report(45, "Başlangıç kısayolu kaldırılıyor...");
            DeleteStartupShortcut();

            // 4. Kurulum dosyalarını sil
            Report(60, "Kurulum dosyaları siliniyor...");
            CleanInstallDirectory();

            // 5. Registry'yi temizle
            Report(80, "Registry kayıtları temizleniyor...");
            CleanRegistry();

            // 6. Doğrulama
            Report(95, "Kaldırma doğrulanıyor...");
            await Task.Delay(500);

            bool installDirGone = !Directory.Exists(InstallDir);
            bool serviceGone = !IsServiceInstalled(ServiceName);

            if (installDirGone && serviceGone)
            {
                Report(100, "✅ Kaldırma başarıyla tamamlandı!");
                return true;
            }
            else
            {
                var msg = "⚠️ Kaldırma tamamlandı ancak ";
                if (!installDirGone) msg += "bazı dosyalar silinemedi. ";
                if (!serviceGone) msg += "service tamamen kaldırılamadı. ";
                msg += "Bilgisayarı yeniden başlattıktan sonra kalıntıları temizleyebilirsiniz.";
                Report(100, msg);
                return false;
            }
        }
        catch (Exception ex)
        {
            Report(100, $"❌ Kaldırma hatası: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    // Yardımcı Metodlar
    // ================================================================

    private bool ValidateSourceFiles(string sourceService, string sourceTray)
    {
        if (!System.IO.File.Exists(Path.Combine(sourceService, ServiceExeName)))
        {
            Report(100, $"❌ Kaynak dosya bulunamadı: {sourceService}\\{ServiceExeName}");
            return false;
        }
        if (!System.IO.File.Exists(Path.Combine(sourceTray, TrayExeName)))
        {
            Report(100, $"❌ Kaynak dosya bulunamadı: {sourceTray}\\{TrayExeName}");
            return false;
        }
        return true;
    }

    private static void StopTrayApp()
    {
        try
        {
            var processes = Process.GetProcessesByName(TrayProcessName);
            foreach (var p in processes)
            {
                p.Kill();
                p.WaitForExit(3000);
                p.Dispose();
            }
            if (processes.Length > 0)
                Thread.Sleep(1000);
        }
        catch { /* Zaten çalışmıyordur */ }
    }

    private static void StartTrayApp()
    {
        var trayExePath = Path.Combine(TrayDir, TrayExeName);
        if (System.IO.File.Exists(trayExePath))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = trayExePath,
                WorkingDirectory = TrayDir,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
    }

    private void StopAndDeleteService(string serviceName)
    {
        if (!IsServiceInstalled(serviceName))
            return;

        StopService(serviceName);

        // Service'i sil
        RunScExe($"delete {serviceName}");
        Thread.Sleep(2000);
    }

    private void StopService(string serviceName)
    {
        if (!IsServiceInstalled(serviceName))
            return;

        try
        {
            using var sc = new System.ServiceProcess.ServiceController(serviceName);
            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running ||
                sc.Status == System.ServiceProcess.ServiceControllerStatus.StartPending)
            {
                RunScExe($"stop {serviceName}");
                Thread.Sleep(3000);
            }
        }
        catch { /* Service zaten durmuştur */ }
    }

    private void StartService()
    {
        RunScExe($"start {ServiceName}");
        Thread.Sleep(2000);
    }

    private static bool IsServiceInstalled(string serviceName)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(serviceName);
            _ = sc.Status; // Status'a erişim denenerek kontrol edilir
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool CreateWindowsService()
    {
        var serviceBinPath = $"\"{Path.Combine(ServiceDir, ServiceExeName)}\"";

        var createResult = RunScExe($"create {ServiceName} binPath= {serviceBinPath} start= auto DisplayName= \"Auto MUX Switcher\"");
        if (!createResult)
        {
            Report(70, "❌ Service oluşturulamadı!");
            return false;
        }

        // Açıklama ve hata kurtarma politikası
        RunScExe($"description {ServiceName} \"Güç durumuna göre dGPU otomatik yönetimi — Pil tasarrufu için ekran kartını otomatik açar/kapatır.\"");
        RunScExe($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");

        return true;
    }

    private static bool RunScExe(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(15000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            System.IO.File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    private static void CleanInstallDirectory()
    {
        if (Directory.Exists(InstallDir))
        {
            try
            {
                Directory.Delete(InstallDir, recursive: true);
                Thread.Sleep(500);
            }
            catch { /* Bazı dosyalar kilitli olabilir */ }
        }
    }

    private static void CreateStartupShortcut()
    {
        try
        {
            var trayExePath = Path.Combine(TrayDir, TrayExeName);

            // PowerShell ile COM nesnesi üzerinden kısayol oluştur
            var psScript =
                $"$ws = New-Object -ComObject WScript.Shell; " +
                $"$sc = $ws.CreateShortcut('{StartupShortcutPath}'); " +
                $"$sc.TargetPath = '{trayExePath}'; " +
                $"$sc.WorkingDirectory = '{TrayDir}'; " +
                $"$sc.Description = 'Auto MUX Switcher — System Tray'; " +
                $"$sc.Save()";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(10000);
        }
        catch { /* Kısayol oluşturulamazsa devam et */ }
    }

    private static void DeleteStartupShortcut()
    {
        try
        {
            if (System.IO.File.Exists(StartupShortcutPath))
                System.IO.File.Delete(StartupShortcutPath);
        }
        catch { /* Devam et */ }
    }

    private static void CleanRegistry()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch { }
    }

    private static bool VerifyInstallation()
    {
        bool serviceOk = IsServiceInstalled(ServiceName);
        bool trayRunning = Process.GetProcessesByName(TrayProcessName).Length > 0;
        return serviceOk && trayRunning;
    }

    /// <summary>
    /// Kurulumun mevcut durumunu kontrol eder.
    /// </summary>
    public static InstallationStatus GetInstallationStatus()
    {
        bool dirExists = Directory.Exists(InstallDir);
        bool serviceExists = IsServiceInstalled(ServiceName);
        bool trayRunning = Process.GetProcessesByName(TrayProcessName).Length > 0;

        return new InstallationStatus(dirExists, serviceExists, trayRunning);
    }
}

/// <summary>
/// Mevcut kurulum durumu bilgisi.
/// </summary>
public record InstallationStatus(
    bool InstallDirectoryExists,
    bool ServiceInstalled,
    bool TrayRunning)
{
    public bool IsInstalled => InstallDirectoryExists || ServiceInstalled;

    public string Summary
    {
        get
        {
            if (!IsInstalled)
                return "Kurulu değil";

            var parts = new List<string>();
            parts.Add(InstallDirectoryExists ? "✅ Dosyalar mevcut" : "❌ Dosyalar eksik");
            parts.Add(ServiceInstalled ? "✅ Service kurulu" : "❌ Service kurulu değil");
            parts.Add(TrayRunning ? "✅ Tray çalışıyor" : "⚠️ Tray çalışmıyor");
            return string.Join("  |  ", parts);
        }
    }
}
