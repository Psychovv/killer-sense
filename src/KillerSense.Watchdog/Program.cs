// killer-sense — watchdog. Processo mínimo: se o serviço morrer com fan em manual, devolve o Auto do EC.
using KillerSense.Watchdog;
using Serilog;

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "logs");
Directory.CreateDirectory(logDir);
using var fileLogger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(logDir, "watchdog-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSerilog(fileLogger);
builder.Services.AddWindowsService(o => o.ServiceName = "KillerSenseWatchdog");
builder.Services.AddHostedService<WatchdogWorker>();
using var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
