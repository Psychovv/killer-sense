// killer-sense — serviço principal (fans). Roda como serviço do Windows (LocalSystem) ou, para testes,
// como console de administrador. Opção --simulated usa o firmware simulado (nenhuma chamada ao hardware).
using KillerSense.Core;
using KillerSense.Firmware;
using KillerSense.Service;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

var simulated = args.Contains("--simulated", StringComparer.Ordinal);
var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "logs");
Directory.CreateDirectory(logDir);
using var fileLogger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(logDir, simulated ? "simulated-service-.log" : "service-.log"), // o simulado não se mistura ao log real
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSerilog(fileLogger);
builder.Services.AddWindowsService(o => o.ServiceName = "KillerSense");
builder.Services.AddSingleton<IAcerFirmware>(_ => simulated ? new SimulatedFirmware() : new AcerWmiFirmware());
builder.Services.AddSingleton<IGpuSensor>(_ => simulated ? new SimulatedGpuSensor() : new KillerSense.Gpu.NvidiaGpuSensor());
builder.Services.AddSingleton(new HeartbeatOptions(simulated ? @"Local\KillerSense.Heartbeat.Simulated" : Heartbeat.DefaultName));
builder.Services.AddSingleton(new IpcOptions(simulated ? KillerSense.Ipc.IpcProtocol.SimulatedPipeName : KillerSense.Ipc.IpcProtocol.PipeName));
// Perfil: config.json (serviço) ou um arquivo à parte no modo simulado.
// --cpu-entry N (30–90): só para testes no hardware — força a curva a assumir com a CPU fria e NÃO salva nada.
var entryArg = Array.IndexOf(args, "--cpu-entry");
var testEntry = entryArg >= 0 && entryArg + 1 < args.Length
    && double.TryParse(args[entryArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e)
    ? e : (double?)null;
ProfileStore? store = testEntry is not null ? null
    : new ProfileStore(simulated ? Path.Combine(Path.GetTempPath(), "KillerSense", "config.simulated.json") : ProfileStore.DefaultPath);
ControlStateStore? control = testEntry is not null ? null
    : new ControlStateStore(simulated ? Path.Combine(Path.GetTempPath(), "KillerSense", "control-disabled.simulated") : ControlStateStore.DefaultPath);
var profile = FanProfile.Default;
if (store is not null)
{
    profile = store.Load(out var problem);
    if (problem is not null)
    {
        fileLogger.Warning("Perfil salvo ignorado, usando o padrão: {Problem}", problem);
    }
}

if (testEntry is { } entry)
{
    profile = profile with
    {
        CpuEntryTemperature = entry,
        CpuCurve = FanCurve.Create([new(entry, 10), new(entry + 15, 35), new(95, 100)]),
    };
}

builder.Services.AddSingleton(sp => new FanControlEngine(sp.GetRequiredService<IAcerFirmware>(), profile));
builder.Services.AddSingleton(sp => new ChargeLimitMonitor(sp.GetRequiredService<IAcerFirmware>()));
builder.Services.AddSingleton(sp => new FanControlWorker(
    sp.GetRequiredService<FanControlEngine>(),
    sp.GetRequiredService<ChargeLimitMonitor>(),
    sp.GetRequiredService<IGpuSensor>(),
    sp.GetRequiredService<HeartbeatOptions>(),
    sp.GetRequiredService<IpcOptions>(),
    store,
    control,
    sp.GetRequiredService<ILogger<FanControlWorker>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<FanControlWorker>());
if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddSingleton<IHostLifetime, KillerSenseServiceLifetime>();
}

using var host = builder.Build();
var worker = host.Services.GetRequiredService<FanControlWorker>();
AppDomain.CurrentDomain.UnhandledException += (_, _) => worker.RestoreAutoNow("exceção não tratada");
await host.RunAsync().ConfigureAwait(false);
