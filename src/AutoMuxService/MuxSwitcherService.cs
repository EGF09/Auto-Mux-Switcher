namespace AutoMuxService;

/// <summary>
/// Ana Windows Service sınıfı.
/// PowerMonitor, GpuManager, StateManager ve PipeServer bileşenlerini koordine eder.
/// Güç durumu değiştiğinde kullanıcıya bildirim gönderir ve yanıta göre GPU'yu yönetir.
/// </summary>
public class MuxSwitcherService : BackgroundService
{
    private readonly ILogger<MuxSwitcherService> _logger;
    private readonly GpuManager _gpuManager;
    private readonly PowerMonitor _powerMonitor;
    private readonly StateManager _stateManager;
    private readonly PipeServer _pipeServer;

    public MuxSwitcherService(
        ILogger<MuxSwitcherService> logger,
        GpuManager gpuManager,
        PowerMonitor powerMonitor,
        StateManager stateManager,
        PipeServer pipeServer)
    {
        _logger = logger;
        _gpuManager = gpuManager;
        _powerMonitor = powerMonitor;
        _stateManager = stateManager;
        _pipeServer = pipeServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Auto MUX Switcher Service başlatılıyor ===");

        // 1. dGPU'yu algıla
        if (!_gpuManager.DetectDgpu())
        {
            _logger.LogError("dGPU algılanamadı! Service devam edecek ama GPU işlemleri yapılamayacak.");
        }
        else
        {
            _stateManager.SaveDgpuInstanceId(_gpuManager.DgpuInstanceId!);
        }

        // 2. Pipe sunucusunu başlat
        _pipeServer.MessageReceived += OnPipeMessageReceived;
        _pipeServer.Start();

        // 3. Kapalıyken fiş değişimi kontrolü
        await CheckStartupPowerStateAsync();

        // 4. Güç durumu izlemeyi başlat
        _powerMonitor.PowerStateChanged += OnPowerStateChanged;
        _powerMonitor.Start();

        // Mevcut güç durumunu kaydet
        _stateManager.SavePowerState(_powerMonitor.IsOnAcPower);

        _logger.LogInformation("=== Service çalışıyor. Güç durumu izleniyor... ===");

        // Service çalışmaya devam etsin
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal kapanış
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== Auto MUX Switcher Service durduruluyor ===");

        _powerMonitor.Stop();
        _pipeServer.Stop();
        _powerMonitor.Dispose();
        _pipeServer.Dispose();

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Başlangıçta güç durumunu kontrol eder.
    /// Bilgisayar kapalıyken fiş değişimi olduysa kullanıcıya bildirim gönderir.
    /// </summary>
    private async Task CheckStartupPowerStateAsync()
    {
        var currentIsOnAc = _powerMonitor.IsOnAcPower;

        if (_stateManager.HasPowerStateChangedWhileOff(currentIsOnAc))
        {
            _logger.LogInformation("Kapalıyken güç durumu değişmiş. Kullanıcıya bildirim gönderiliyor...");

            if (currentIsOnAc)
            {
                // Fiş takılmış durumda — dGPU'yu etkinleştir
                await NotifyUserAsync("POWER_CHANGED:AC:STARTUP");
            }
            else
            {
                // Fiş çıkarılmış durumda — dGPU'yu devre dışı bırak
                await NotifyUserAsync("POWER_CHANGED:BATTERY:STARTUP");
            }
        }
        else
        {
            _logger.LogInformation("Güç durumunda değişiklik yok veya ilk çalıştırma.");
        }

        // Mevcut durumu kaydet
        _stateManager.SavePowerState(currentIsOnAc);
    }

    /// <summary>
    /// Güç durumu değiştiğinde çağrılır.
    /// </summary>
    private async void OnPowerStateChanged(object? sender, PowerStateChangedEventArgs e)
    {
        if (!_stateManager.IsAutoSwitchEnabled())
        {
            _logger.LogInformation("Otomatik geçiş devre dışı. İşlem yapılmıyor.");
            return;
        }

        _stateManager.SavePowerState(e.IsOnAcPower);

        if (e.IsOnAcPower)
        {
            _logger.LogInformation("⚡ Fiş takıldı! Kullanıcıya dGPU etkinleştirme bildirimi gönderiliyor...");
            await NotifyUserAsync("POWER_CHANGED:AC");
        }
        else
        {
            _logger.LogInformation("🔋 Fiş çıkarıldı! Kullanıcıya dGPU devre dışı bırakma bildirimi gönderiliyor...");
            await NotifyUserAsync("POWER_CHANGED:BATTERY");
        }
    }

    /// <summary>
    /// Tray uygulamasından gelen yanıtları işler.
    /// </summary>
    private async void OnPipeMessageReceived(object? sender, PipeMessageEventArgs e)
    {
        _logger.LogInformation("Tray uygulamasından yanıt: {Message}", e.Message);

        switch (e.Message)
        {
            case "RESPONSE:YES:DISABLE":
                await HandleDisableGpuAsync();
                break;

            case "RESPONSE:YES:ENABLE":
                await HandleEnableGpuAsync();
                break;

            case "RESPONSE:NO":
                _logger.LogInformation("Kullanıcı işlemi reddetti. Değişiklik yapılmıyor.");
                break;

            case "STATUS_REQUEST":
                await SendStatusAsync();
                break;

            default:
                _logger.LogWarning("Bilinmeyen mesaj: {Message}", e.Message);
                break;
        }
    }

    /// <summary>
    /// dGPU'yu devre dışı bırakır ve sonucu bildirir.
    /// </summary>
    private async Task HandleDisableGpuAsync()
    {
        if (!_gpuManager.IsDgpuDetected)
        {
            await NotifyUserAsync("RESULT:ERROR:dGPU algılanmadı");
            return;
        }

        var success = await _gpuManager.DisableGpuAsync();

        if (success)
        {
            _stateManager.SaveGpuState(false);
            await NotifyUserAsync("RESULT:SUCCESS:DISABLED");
            _logger.LogInformation("✅ dGPU başarıyla devre dışı bırakıldı.");
        }
        else
        {
            await NotifyUserAsync("RESULT:ERROR:dGPU devre dışı bırakılamadı");
            _logger.LogError("❌ dGPU devre dışı bırakılamadı.");
        }
    }

    /// <summary>
    /// dGPU'yu etkinleştirir ve sonucu bildirir.
    /// </summary>
    private async Task HandleEnableGpuAsync()
    {
        if (!_gpuManager.IsDgpuDetected)
        {
            await NotifyUserAsync("RESULT:ERROR:dGPU algılanmadı");
            return;
        }

        var success = await _gpuManager.EnableGpuAsync();

        if (success)
        {
            _stateManager.SaveGpuState(true);
            await NotifyUserAsync("RESULT:SUCCESS:ENABLED");
            _logger.LogInformation("✅ dGPU başarıyla etkinleştirildi.");
        }
        else
        {
            await NotifyUserAsync("RESULT:ERROR:dGPU etkinleştirilemedi");
            _logger.LogError("❌ dGPU etkinleştirilemedi.");
        }
    }

    /// <summary>
    /// Tray uygulamasına durum bilgisi gönderir.
    /// </summary>
    private async Task SendStatusAsync()
    {
        var gpuEnabled = _gpuManager.IsDgpuDetected && _gpuManager.IsGpuEnabled();
        var onAc = _powerMonitor.IsOnAcPower;
        var gpuName = _gpuManager.DgpuName ?? "Algılanmadı";

        var statusMessage = $"STATUS:{gpuName}:{(gpuEnabled ? "ENABLED" : "DISABLED")}:{(onAc ? "AC" : "BATTERY")}";
        await _pipeServer.SendMessageAsync(statusMessage);
    }

    /// <summary>
    /// Tray uygulamasına bildirim mesajı gönderir.
    /// </summary>
    private async Task NotifyUserAsync(string message)
    {
        try
        {
            await _pipeServer.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kullanıcıya bildirim gönderilemedi. Mesaj: {Message}", message);
        }
    }
}
