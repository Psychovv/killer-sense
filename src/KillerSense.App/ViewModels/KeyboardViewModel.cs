using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Teclado e sistema (PRÉVIA, sem integração): tecla de modos (A12), trava da tecla Windows, tecla de
/// aderência e os extras do PredatorSense/Quick Access ainda não mapeados (A14). Nada é enviado ao notebook.
/// </summary>
public sealed partial class KeyboardViewModel : ObservableObject
{
    /// <summary>Tecla de modos: alterna Silencioso → Equilibrado → Desempenho → Turbo (na bateria, Equilibrado ↔ Eco).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeKeyHint))]
    public partial bool ModeKeyCycles { get; set; }

    /// <summary>Tecla de modos: cada toque liga ou desliga o Turbo (opção atual do notebook, teste A12).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeKeyHint))]
    public partial bool ModeKeyTurbo { get; set; } = true;

    [ObservableProperty] public partial bool WindowsKeyLocked { get; set; }

    /// <summary>Desliga o atalho da tecla de aderência (Shift 5 vezes), que atrapalha em jogos.</summary>
    [ObservableProperty] public partial bool StickyKeysShortcutOff { get; set; } = true;

    /// <summary>Carregar dispositivos pela USB com o notebook desligado (Acer Quick Access).</summary>
    [ObservableProperty] public partial bool UsbChargeWhenOff { get; set; }

    /// <summary>Overdrive do painel (menos rastro em imagens rápidas).</summary>
    [ObservableProperty] public partial bool LcdOverdrive { get; set; }

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }

    public string ModeKeyHint => ModeKeyCycles
        ? "Na tomada: Silencioso → Equilibrado → Desempenho → Turbo. Na bateria: Equilibrado ↔ Eco."
        : "Cada toque alterna entre Equilibrado e Turbo (só na tomada).";

    partial void OnModeKeyCyclesChanged(bool value) => Changed();

    partial void OnModeKeyTurboChanged(bool value) => Changed();

    partial void OnWindowsKeyLockedChanged(bool value) => Changed();

    partial void OnStickyKeysShortcutOffChanged(bool value) => Changed();

    partial void OnUsbChargeWhenOffChanged(bool value) => Changed();

    partial void OnLcdOverdriveChanged(bool value) => Changed();

    private void Changed()
    {
        IsDirty = true;
        Status = null;
    }

    [RelayCommand]
    private void Save()
    {
        IsDirty = false;
        Status = "Prévia: opções guardadas só nesta janela. Nada foi enviado ao notebook.";
    }
}
