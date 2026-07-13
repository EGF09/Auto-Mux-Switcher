using System.IO.Pipes;
using System.Text;

namespace AutoMuxTray;

/// <summary>
/// Named Pipe istemcisi. Tray uygulaması ile Service arasındaki iletişimi sağlar.
/// Service'den gelen bildirimleri dinler ve kullanıcı yanıtlarını geri gönderir.
/// Bağlantı hatalarında otomatik yeniden deneme mekanizması içerir.
/// </summary>
public class PipeClient : IDisposable
{
    public const string PipeName = "AutoMuxSwitcherPipe";
    public const string NotifyPipeName = "AutoMuxSwitcherPipe_Notify";

    private const int MaxRetries = 3;
    private const int RetryDelayMs = 1000;
    private const int ConnectTimeoutMs = 5000;

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    /// <summary>
    /// Service'den mesaj geldiğinde tetiklenir.
    /// </summary>
    public event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Bildirimleri dinlemeye başlar.
    /// </summary>
    public void StartListening()
    {
        _cts = new CancellationTokenSource();
        _listenTask = ListenForNotificationsAsync(_cts.Token);
    }

    /// <summary>
    /// Dinlemeyi durdurur.
    /// </summary>
    public void StopListening()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Service'e mesaj gönderir. Başarısız olursa yeniden dener.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                await pipeClient.ConnectAsync(ConnectTimeoutMs);

                var bytes = Encoding.UTF8.GetBytes(message);
                await pipeClient.WriteAsync(bytes);
                await pipeClient.FlushAsync();

                System.Diagnostics.Debug.WriteLine($"Mesaj gönderildi (deneme {attempt}): {message}");
                return; // Başarılı, çık
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Service'e bağlanılamadı (timeout, deneme {attempt}/{MaxRetries}).");
            }
            catch (IOException ex) when (attempt < MaxRetries)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Pipe IO hatası (deneme {attempt}/{MaxRetries}): {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Mesaj gönderilirken hata (deneme {attempt}/{MaxRetries}): {ex.Message}");
                
                if (attempt >= MaxRetries)
                    return;
            }

            // Yeniden denemeden önce bekle
            if (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelayMs);
            }
        }
    }

    /// <summary>
    /// Service'den gelen bildirimleri sürekli olarak dinler.
    /// Bağlantı kesilirse otomatik olarak tekrar bağlanır.
    /// </summary>
    private async Task ListenForNotificationsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(
                    ".", NotifyPipeName, PipeDirection.In);

                await pipeClient.ConnectAsync(cancellationToken);

                var buffer = new byte[4096];
                var bytesRead = await pipeClient.ReadAsync(buffer, cancellationToken);

                if (bytesRead > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    System.Diagnostics.Debug.WriteLine($"Service'den mesaj alındı: {message}");
                    MessageReceived?.Invoke(this, message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bildirim dinleme hatası: {ex.Message}");
                try
                {
                    // Service çalışmıyor olabilir, biraz bekleyip tekrar dene
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopListening();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
