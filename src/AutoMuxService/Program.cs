using AutoMuxService;

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
