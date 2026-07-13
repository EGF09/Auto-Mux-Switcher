using System.Threading;
using AutoMuxService;

const string MutexName = "Global\\AutoMuxSwitcherServiceMutex";
bool createdNew;
var mutex = new Mutex(true, MutexName, out createdNew);

if (!createdNew)
{
    Console.WriteLine("AutoMuxSwitcher Service is already running. Exiting.");
    return;
}

try
{
    var builder = Host.CreateApplicationBuilder(args);

// Windows Service olarak çalıştırmayı etkinleştir
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "AutoMuxSwitcher";
    });

    // Servisleri kaydet
    builder.Services.AddSingleton<GpuManager>();
    builder.Services.AddSingleton<PowerMonitor>();
    builder.Services.AddSingleton<StateManager>();
    builder.Services.AddSingleton<PipeServer>();
    builder.Services.AddHostedService<MuxSwitcherService>();

    // Logging yapılandırması
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "AutoMuxSwitcher";
        settings.LogName = "Application";
    });

    var host = builder.Build();
    host.Run();
}
finally
{
    mutex.ReleaseMutex();
    mutex.Dispose();
}

