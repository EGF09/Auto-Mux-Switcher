namespace AutoMuxTray;

/// <summary>
/// Auto MUX Switcher — System Tray Uygulaması
/// Windows Service ile Named Pipe üzerinden iletişim kurarak
/// kullanıcıya güç değişikliği bildirimlerini gösterir.
/// </summary>
static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        // Tek instance kontrolü
        const string mutexName = "AutoMuxSwitcher_TrayApp_Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Zaten çalışıyor
            MessageBox.Show(
                "Auto MUX Switcher zaten çalışıyor.",
                "Auto MUX Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogError("ThreadException", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogError("UnhandledException", e.ExceptionObject as Exception);
            };

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception ex)
        {
            LogError("Main", ex);
        }
        finally
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }

    private static void LogError(string source, Exception? ex)
    {
        var message = $"[{DateTime.Now}] {source}: {ex?.ToString() ?? "Unknown error"}";
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
        try
        {
            File.AppendAllText(logPath, message + Environment.NewLine);
        }
        catch { /* Log dosyasına yazılamadıysa sessiz geç */ }

        MessageBox.Show(
            $"Auto MUX Switcher Hatası:\n\n{ex?.Message ?? "Bilinmeyen hata"}\n\nDetay: {logPath}",
            "Auto MUX Switcher — Hata",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}