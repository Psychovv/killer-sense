using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela GPU (PRÉVIA, sem integração): offset de clock (núcleo e memória) por modo. Valores padrão da Acer
/// lidos do <c>.ini</c> do modelo (PHN16-72): só o Turbo tem offset (+100 núcleo / +200 memória). O
/// PredatorSense aplica pela NVAPI (<c>SetPstates20</c>, fora do SDK público): risco a avaliar antes de integrar.
/// </summary>
public sealed partial class GpuViewModel : ObservableObject
{
    public GpuViewModel()
    {
        Rows =
        [
            new(this, "Silencioso", 0, 0),
            new(this, "Equilibrado", 0, 0),
            new(this, "Desempenho", 0, 0),
            new(this, "Turbo", 100, 200),
        ];
        IsDirty = false;
    }

    public IReadOnlyList<GpuOffsetRowViewModel> Rows { get; }

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }

    internal void Changed()
    {
        IsDirty = true;
        Status = null;
    }

    [RelayCommand]
    private void RestoreAcerDefaults()
    {
        foreach (var row in Rows)
        {
            row.Core = row.AcerCore;
            row.Memory = row.AcerMemory;
        }

        Status = "Valores padrão da Acer restaurados (ainda não salvos).";
    }

    [RelayCommand]
    private void Save()
    {
        IsDirty = false;
        Status = "Prévia: offsets guardados só nesta janela. Nada foi enviado à GPU.";
    }
}

/// <summary>Offsets de um modo.</summary>
public sealed partial class GpuOffsetRowViewModel : ObservableObject
{
    private readonly GpuViewModel _owner;

    public GpuOffsetRowViewModel(GpuViewModel owner, string mode, int acerCore, int acerMemory)
    {
        _owner = owner;
        Mode = mode;
        AcerCore = acerCore;
        AcerMemory = acerMemory;
        Core = acerCore;
        Memory = acerMemory;
    }

    public string Mode { get; }

    public decimal AcerCore { get; }

    public decimal AcerMemory { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAcerDefault))]
    public partial decimal Core { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAcerDefault))]
    public partial decimal Memory { get; set; }

    public bool IsAcerDefault => Core == AcerCore && Memory == AcerMemory;

    partial void OnCoreChanged(decimal value) => _owner.Changed();

    partial void OnMemoryChanged(decimal value) => _owner.Changed();
}
