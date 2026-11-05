using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KillerSense.App.Platform;

/// <summary>Uma amostra da CPU; cada valor é null quando o contador não existe ou ainda não tem dado.</summary>
public sealed record CpuSample(double? ClockMhz, double? UtilizationPercent, double? PackageWatts);

/// <summary>
/// Clock, uso e potência da CPU pelos contadores de desempenho do Windows (PDH), sem admin e sem driver.
/// Nomes em inglês (<c>PdhAddEnglishCounterW</c>): funcionam em qualquer idioma do Windows.
/// <list type="bullet">
/// <item>Clock = "Processor Frequency" (base) × "% Processor Performance", como o Gerenciador de Tarefas.</item>
/// <item>Potência do pacote = variação do "Energy Meter" RAPL (energia em pWh); conferido com o HWiNFO no teste A18.</item>
/// </list>
/// Usar só com a tela aberta: <see cref="Sample"/> uma vez por segundo, <see cref="Dispose"/> ao sair.
/// </summary>
internal sealed partial class CpuCounters : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private readonly nint _query;
    private readonly nint _frequency;
    private readonly nint _performance;
    private readonly nint _utility;
    private readonly nint _energy;
    private readonly Stopwatch _clock = new();
    private long? _lastEnergy;
    private bool _disposed;

    public CpuCounters()
    {
        if (PdhOpenQueryW(null, 0, out _query) != 0)
        {
            _query = 0;
            return;
        }

        _frequency = Add(@"\Processor Information(_Total)\Processor Frequency");
        _performance = Add(@"\Processor Information(_Total)\% Processor Performance");
        _utility = Add(@"\Processor Information(_Total)\% Processor Utility");
        _energy = Add(@"\Energy Meter(rapl_package0_pkg)\Energy");
        _ = PdhCollectQueryData(_query); // primeira coleta: os contadores de taxa precisam de duas
        _lastEnergy = Raw(_energy);
        _clock.Start();
    }

    public CpuSample Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_query == 0 || PdhCollectQueryData(_query) != 0)
        {
            return new CpuSample(null, null, null);
        }

        var freq = Formatted(_frequency);
        var perf = Formatted(_performance);
        double? clock = freq is { } f && perf is { } p ? f * p / 100 : null;
        double? usage = Formatted(_utility) is { } u ? Math.Clamp(u, 0, 100) : null;

        double? watts = null;
        var energy = Raw(_energy);
        var dt = _clock.Elapsed.TotalSeconds;
        if (energy is { } e && _lastEnergy is { } last && e >= last && dt > 0.2)
        {
            watts = (e - last) * 3600 / 1e12 / dt; // pWh → W
        }

        _lastEnergy = energy;
        _clock.Restart();
        return new CpuSample(clock, usage, watts);
    }

    private nint Add(string path) => PdhAddEnglishCounterW(_query, path, 0, out var counter) == 0 ? counter : 0;

    private static double? Formatted(nint counter)
    {
        if (counter == 0 || PdhGetFormattedCounterValue(counter, PdhFmtDouble | PdhFmtNoCap100, out _, out var value) != 0 || value.CStatus > 1)
        {
            return null; // CStatus 0 = válido, 1 = válido (novo dado)
        }

        return double.IsFinite(value.DoubleValue) ? value.DoubleValue : null;
    }

    private static long? Raw(nint counter)
    {
        if (counter == 0 || PdhGetRawCounterValue(counter, out _, out var raw) != 0 || raw.CStatus > 1)
        {
            return null;
        }

        return raw.FirstValue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_query != 0)
        {
            _ = PdhCloseQuery(_query);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue; // união; com PDH_FMT_DOUBLE é o double (alinhado em 8)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhRawCounter
    {
        public uint CStatus;
        public uint TimeStampLow;
        public uint TimeStampHigh;
        public long FirstValue;
        public long SecondValue;
        public uint MultiCount;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhAddEnglishCounterW(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PdhFmtCounterValue value);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhGetRawCounterValue(nint counter, out uint type, out PdhRawCounter value);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCloseQuery(nint query);
}
