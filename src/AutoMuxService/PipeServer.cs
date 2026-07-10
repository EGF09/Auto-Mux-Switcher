using System.IO.Pipes;
using System.Text;

namespace AutoMuxService;

/// <summary>
/// Named Pipe sunucusu. Service ile Tray uygulaması arasındaki iletişimi yönetir.
/// Service tarafında çalışır, Tray uygulamasından gelen bağlantıları dinler.
/// </summary>
public class PipeServer : IDisposable
{
    public const string PipeName = "AutoMuxSwitcherPipe";

    private readonly ILogger<PipeServer> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    /// <summary>
    /// Tray uygulamasından yanıt geldiğinde tetiklenir.
    /// </summary>
    public event EventHandler<PipeMessageEventArgs>? MessageReceived;

    public PipeServer(ILogger<PipeServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Pipe sunucusunu başlatır ve bağlantıları dinlemeye başlar.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = ListenForConnectionsAsync(_cts.Token);
        _logger.LogInformation("Pipe sunucusu başlatıldı: {PipeName}", PipeName);
    }

    /// <summary>
    /// Pipe sunucusunu durdurur.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _logger.LogInformation("Pipe sunucusu durduruluyor...");
    }

    /// <summary>
    /// Tray uygulamasına mesaj gönderir.
    /// Yeni bir pipe bağlantısı oluşturur ve mesajı yazar.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        try
        {
            // Bildirim göndermek için yeni bir pipe instance oluştur
            using var pipeServer = new NamedPipeServerStream(
                PipeName + "_Notify",
                PipeDirection.Out,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message);

            _logger.LogDebug("Tray uygulaması bağlantısı bekleniyor (bildirim)...");

            // 10 saniye timeout ile bağlantı bekle
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipeServer.WaitForConnectionAsync(timeoutCts.Token);

            var bytes = Encoding.UTF8.GetBytes(message);
            await pipeServer.WriteAsync(bytes);
            await pipeServer.FlushAsync();

            _logger.LogDebug("Mesaj gönderildi: {Message}", message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Tray uygulaması bağlantı zaman aşımı. Bildirim gönderilemedi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mesaj gönderilirken hata oluştu.");
        }
    }

    /// <summary>
    /// Sürekli olarak gelen bağlantıları dinler.
    /// </summary>
    private async Task ListenForConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message);

                _logger.LogDebug("Tray uygulaması bağlantısı bekleniyor...");
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                // Mesajı oku
                var buffer = new byte[1024];
                var bytesRead = await pipeServer.ReadAsync(buffer, cancellationToken);

                if (bytesRead > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    _logger.LogDebug("Mesaj alındı: {Message}", message);

                    MessageReceived?.Invoke(this, new PipeMessageEventArgs(message));
                }
            }
            catch (OperationCanceledException)
            {
                // Normal kapanış
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe dinleme sırasında hata oluştu.");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Pipe mesajı event argümanları.
/// </summary>
public class PipeMessageEventArgs : EventArgs
{
    public string Message { get; }

    public PipeMessageEventArgs(string message)
    {
        Message = message;
    }
}
