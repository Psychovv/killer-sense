// killer-sense — ferramenta de testes no HARDWARE REAL (Fase 3.1, teste A15).
// Só roda como administrador e só depois de o usuário aprovar cada teste (docs/arquitetura.md §14).
//
// Comandos:
//   read                                   só leitura: modos, %, RPM, temperaturas, modo térmico
//   custom --cpu N --gpu N [--hold S]      grava % (20–60) nas duas fans, põe as duas em Custom,
//                                          acompanha por S segundos (5–60, padrão 20) e SEMPRE volta ao Auto
//   auto                                   só volta as duas fans ao Auto (0x00410009)
//   mode --set quiet|balanced|performance|turbo|eco
//        [--watch S]                       troca o modo de operação (A16) e lê o OPMS por S s (5–120, padrão 10).
//                                          Desempenho/Turbo só na tomada, Eco só na bateria.
//   battery                                só leitura: funções de saúde da bateria (A8)
//   battery --limit on|off                 liga/desliga o limite de carga (SetBatteryHealthControl(1, 1, x))
//
// Proibido por decisão do usuário: modo Max e qualquer % acima de 60 (barulho).
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using KillerSense.Core;
using KillerSense.Firmware;

namespace KillerSense.HwTests;

internal static class Program
{
    private const int MinPercent = 20;
    private const int MaxPercent = 60;
    private const double AbortCpuTemperature = 85;

    private static readonly Dictionary<Fan, FanMode> AutoBoth = new() { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto };
    private static readonly Dictionary<Fan, FanMode> CustomBoth = new() { [Fan.Cpu] = FanMode.Custom, [Fan.Gpu] = FanMode.Custom };

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "gpu")
        {
            return ReadGpu(args); // não precisa de admin nem toca no firmware da Acer
        }

        if (!IsAdministrator())
        {
            Console.Error.WriteLine("Precisa rodar como administrador.");
            return 2;
        }

        if (args.Length == 0)
        {
            Console.Error.WriteLine("Uso: read | auto | custom --cpu N --gpu N [--hold S]");
            return 2;
        }

        using var fw = new AcerWmiFirmware();
        try
        {
            return args[0] switch
            {
                "read" => Read(fw),
                "auto" => RestoreAuto(fw),
                "custom" => Custom(fw, args),
                "mode" => SetMode(fw, args),
                "battery" => Battery(fw, args),
                _ => Fail($"Comando desconhecido: {args[0]}"),
            };
        }
        catch (Exception ex) when (ex is FirmwareException or ArgumentException)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    /// <summary>gpu [--samples N]: estado de energia + temperatura via NVML, uma leitura por segundo.</summary>
    private static int ReadGpu(string[] args)
    {
        var samples = Math.Clamp(ArgInt(args, "--samples") ?? 5, 1, 120);
        using var gpu = new KillerSense.Gpu.NvidiaGpuSensor();
        Log($"GPU: {gpu.InstanceId ?? "não encontrada"}");
        for (var i = 0; i < samples; i++)
        {
            var r = gpu.Read();
            Log(r.PoweredOn ? (r.Temperature is { } t ? $"ligada (D0)  {t} °C" : "ligada (D0)  erro de leitura") : "desligada (não consultada)");
            if (i < samples - 1)
            {
                Thread.Sleep(1000);
            }
        }

        return 0;
    }

    private static int Read(AcerWmiFirmware fw)
    {
        PrintState(fw, "estado");
        return 0;
    }

    private static int RestoreAuto(AcerWmiFirmware fw)
    {
        Log($"escrita: SetGamingFanBehavior(0x{AcerProtocol.EncodeSetFanBehavior(AutoBoth):X8})");
        fw.SetFanModes(AutoBoth);
        PrintState(fw, "depois");
        return 0;
    }

    private static int Custom(AcerWmiFirmware fw, string[] args)
    {
        var cpu = ArgInt(args, "--cpu") ?? throw new ArgumentException("Informe --cpu.");
        var gpu = ArgInt(args, "--gpu") ?? throw new ArgumentException("Informe --gpu.");
        var hold = ArgInt(args, "--hold") ?? 20;
        foreach (var (name, v) in new[] { ("--cpu", cpu), ("--gpu", gpu) })
        {
            if (v is < MinPercent or > MaxPercent)
            {
                throw new ArgumentException($"{name} precisa estar entre {MinPercent} e {MaxPercent}% (limite desta ferramenta).");
            }
        }

        if (hold is < 5 or > 60)
        {
            throw new ArgumentException("--hold precisa estar entre 5 e 60 s.");
        }

        PrintState(fw, "antes");

        // Ctrl+C também devolve o Auto (matar o processo à força não — isso é papel do watchdog na 3.2).
        var aborted = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            aborted = true;
        };

        try
        {
            Log($"escrita 1: SetGamingFanSpeed(0x{AcerProtocol.EncodeSetFanSpeed(Fan.Cpu, cpu):X4})  CPU {cpu}%");
            fw.SetFanSpeedPercent(Fan.Cpu, cpu);
            Log($"escrita 2: SetGamingFanSpeed(0x{AcerProtocol.EncodeSetFanSpeed(Fan.Gpu, gpu):X4})  GPU {gpu}%");
            fw.SetFanSpeedPercent(Fan.Gpu, gpu);
            Log($"escrita 3: SetGamingFanBehavior(0x{AcerProtocol.EncodeSetFanBehavior(CustomBoth):X8})  Custom");
            fw.SetFanModes(CustomBoth);
            PrintState(fw, "custom");

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < hold && !aborted)
            {
                Thread.Sleep(1000);
                var t = fw.ReadSensor(Sensor.CpuTemperature);
                Log($"t={sw.Elapsed.TotalSeconds,4:0}s  CPU {t} °C  fan CPU {fw.ReadSensor(Sensor.CpuFanRpm)} rpm  fan GPU {fw.ReadSensor(Sensor.GpuFanRpm)} rpm");
                if (t == 0 || t >= AbortCpuTemperature)
                {
                    Log($"ABORTADO: temperatura da CPU inválida ou ≥ {AbortCpuTemperature} °C.");
                    break;
                }
            }
        }
        finally
        {
            Log($"escrita final: SetGamingFanBehavior(0x{AcerProtocol.EncodeSetFanBehavior(AutoBoth):X8})  Auto");
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    fw.SetFanModes(AutoBoth);
                    break;
                }
                catch (FirmwareException ex) when (attempt < 3)
                {
                    Log($"falha ao voltar ao Auto (tentativa {attempt}): {ex.Message}");
                    Thread.Sleep(500);
                }
            }

            PrintState(fw, "depois (Auto)");
        }

        return aborted ? 3 : 0;
    }

    private static int SetMode(AcerWmiFirmware fw, string[] args)
    {
        var i = Array.IndexOf(args, "--set");
        var name = i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Informe --set <modo>.");
        if (!Enum.TryParse<OperatingMode>(name, ignoreCase: true, out var mode) || !Enum.IsDefined(mode) || int.TryParse(name, out _))
        {
            throw new ArgumentException($"Modo desconhecido: {name} (quiet, balanced, performance, turbo, eco).");
        }

        var onAc = PowerSource.IsOnAc() ?? throw new ArgumentException("Não consegui saber se está na tomada; nada foi gravado.");
        if (!OperatingModes.IsAllowed(mode, onAc))
        {
            throw new ArgumentException($"{mode} não é permitido {(onAc ? "na tomada" : "na bateria")}; nada foi gravado.");
        }

        Log($"energia: {(onAc ? "tomada" : "bateria")}");
        PrintState(fw, "antes");
        Log($"escrita: SetGamingMiscSetting(0x{AcerProtocol.EncodeSetOperatingMode(mode):X3})  {mode}");
        fw.SetOperatingMode(mode);
        var watch = Math.Clamp(ArgInt(args, "--watch") ?? 10, 5, 120);
        for (var s = 1; s <= watch; s++)
        {
            Thread.Sleep(1000);
            Log($"t={s,2}s  OPMS={fw.GetOperatingMode()}  CPU {fw.ReadSensor(Sensor.CpuTemperature)} °C  fan CPU {fw.ReadSensor(Sensor.CpuFanRpm)} rpm");
        }

        PrintState(fw, "depois");
        return fw.GetOperatingMode() == mode ? 0 : 4;
    }

    private static int Battery(AcerWmiFirmware fw, string[] args)
    {
        PrintBattery(fw, "antes");
        var i = Array.IndexOf(args, "--limit");
        if (i < 0)
        {
            return 0;
        }

        var value = i + 1 < args.Length ? args[i + 1] : "";
        bool on = value switch
        {
            "on" => true,
            "off" => false,
            _ => throw new ArgumentException("Use --limit on ou --limit off."),
        };
        Log($"escrita: SetBatteryHealthControl(uBatteryNo=1, uFunctionMask=1, uFunctionStatus={(on ? 1 : 0)}, uReservedIn=00×5)");
        fw.SetBatteryChargeLimit(on);
        Thread.Sleep(1000);
        PrintBattery(fw, "depois");
        return fw.GetBatteryHealth().ChargeLimitOn == on ? 0 : 4;
    }

    private static void PrintBattery(AcerWmiFirmware fw, string label)
    {
        var h = fw.GetBatteryHealth();
        Log($"[{label}] uFunctionList=0x{h.SupportedMask:X2}  uFunctionStatus={Convert.ToHexString([.. h.Status])}  " +
            $"limite={(h.ChargeLimitOn is { } on ? (on ? "LIGADO" : "desligado") : "?")}  " +
            $"carga={PowerSource.BatteryPercent()?.ToString(CultureInfo.InvariantCulture) ?? "?"}%  energia={(PowerSource.IsOnAc() is { } ac ? (ac ? "tomada" : "bateria") : "?")}");
    }

    private static void PrintState(AcerWmiFirmware fw, string label)
    {
        Log($"[{label}] modo térmico={fw.GetOperatingMode()}  " +
            $"CPU: {fw.GetFanMode(Fan.Cpu)} {fw.GetFanSpeedPercent(Fan.Cpu)}% {fw.ReadSensor(Sensor.CpuFanRpm)} rpm {fw.ReadSensor(Sensor.CpuTemperature)} °C  " +
            $"GPU: {fw.GetFanMode(Fan.Gpu)} {fw.GetFanSpeedPercent(Fan.Gpu)}% {fw.ReadSensor(Sensor.GpuFanRpm)} rpm");
    }

    private static int? ArgInt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static void Log(string message) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
}
