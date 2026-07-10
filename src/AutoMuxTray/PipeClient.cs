using System.IO.Pipes;
using System.Text;

namespace AutoMuxTray;

/// <summary>
/// Named Pipe istemcisi. Tray uygulaması ile Service arasındaki iletişimi sağlar.
/// Service'den gelen bildirimleri dinler ve kullanıcı yanıtlarını geri gönderir.
/// </summary>
public class PipeClient : IDisposable
{
    public const string PipeName = "AutoMuxSwitcherPipe";
    public const string NotifyPipeName = "AutoMuxSwitcherPipe_Notify";

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
    /// Service'e mesaj gönderir.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await pipeClient.ConnectAsync(5000); // 5 saniye timeout

            var bytes = Encoding.UTF8.GetBytes(message);
            await pipeClient.WriteAsync(bytes);
            await pipeClient.FlushAsync();
        }
        catch (TimeoutException)
        {
            // Service çalışmıyor olabilir
            System.Diagnostics.Debug.WriteLine("Service'e bağlanılamadı (timeout).");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mesaj gönderilirken hata: {ex.Message}");
        }
    }

    /// <summary>
    /// Service'den gelen bildirimleri sürekli olarak dinler.
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
