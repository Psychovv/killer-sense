using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Iluminação (PRÉVIA, sem integração): teclado com 4 zonas (<c>LightSetting\KeyBoardArea = 4</c>),
/// efeito, brilho e desligar após inatividade. Os métodos <c>SetGamingRgbKb</c>/<c>KBBacklight</c> ainda
/// precisam do teste A9; aqui nada é enviado ao notebook.
/// </summary>
public sealed partial class LightingViewModel : ObservableObject
{
    // Cores fixas usadas na prévia dos efeitos multicoloridos (Onda e Neon).
    private static readonly string[] PaletteHex = ["#FF4D5E", "#FF8A3D", "#FFC94D", "#4DFF8B", "#3DE0D0", "#5EC8FF", "#4D6BFF", "#8B7BFF", "#D46BFF", "#FFFFFF"];
    private static readonly Color KeyboardOff = Color.Parse("#141417");
    private static readonly Color[] Rainbow = [Color.Parse("#FF4D5E"), Color.Parse("#FFC94D"), Color.Parse("#4DFF8B"), Color.Parse("#4DA6FF")];

    public LightingViewModel()
    {
        Zones =
        [
            new(this, 1, Color.Parse("#8B7BFF")),
            new(this, 2, Color.Parse("#8B7BFF")),
            new(this, 3, Color.Parse("#5EC8FF")),
            new(this, 4, Color.Parse("#5EC8FF")),
        ];
        Palette = [.. PaletteHex.Select(hex => new SwatchViewModel(this, Color.Parse(hex)))];
        SelectedZone = Zones[0];
        RefreshPreview();
        IsDirty = false; // as zonas marcam "alterado" ao receber a cor inicial
    }

    public IReadOnlyList<LightZoneViewModel> Zones { get; }

    public IReadOnlyList<SwatchViewModel> Palette { get; }

    public IReadOnlyList<string> Effects { get; } = ["Estático", "Respiração", "Onda", "Neon", "Desligado"];

    public IReadOnlyList<string> Directions { get; } = ["Esquerda → direita", "Direita → esquerda"];

    [ObservableProperty] public partial LightZoneViewModel SelectedZone { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesZoneColors), nameof(UsesSpeed), nameof(UsesDirection), nameof(IsOff), nameof(EffectHint))]
    public partial int EffectIndex { get; set; }

    /// <summary>Brilho de 0 a 100 (campo <c>KBBP</c> do EC, SUPOSIÇÃO forte).</summary>
    [ObservableProperty] public partial double Brightness { get; set; } = 80;

    /// <summary>Velocidade dos efeitos animados, 1 (lento) a 5 (rápido).</summary>
    [ObservableProperty] public partial double Speed { get; set; } = 3;

    [ObservableProperty] public partial int DirectionIndex { get; set; }

    /// <summary>Apagar a luz após 30 s sem usar o teclado (opção do PredatorSense, teste A14).</summary>
    [ObservableProperty] public partial bool AutoOff { get; set; } = true;

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }

    /// <summary>Estático e Respiração usam a cor de cada zona; Onda e Neon passam por todas as cores.</summary>
    public bool UsesZoneColors => EffectIndex is 0 or 1;

    public bool UsesSpeed => EffectIndex is 1 or 2 or 3;

    public bool UsesDirection => EffectIndex == 2;

    public bool IsOff => EffectIndex == 4;

    public string EffectHint => EffectIndex switch
    {
        0 => "Cada zona fica acesa com a sua cor.",
        1 => "Cada zona acende e apaga devagar com a sua cor.",
        2 => "As cores passam de um lado para o outro do teclado.",
        3 => "O teclado inteiro troca de cor aos poucos.",
        _ => "Luz do teclado desligada.",
    };

    partial void OnSelectedZoneChanged(LightZoneViewModel oldValue, LightZoneViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;
    }

    partial void OnEffectIndexChanged(int value) => Changed();

    partial void OnBrightnessChanged(double value) => Changed();

    partial void OnSpeedChanged(double value) => Changed();

    partial void OnDirectionIndexChanged(int value) => Changed();

    partial void OnAutoOffChanged(bool value) => Changed();

    internal void Changed()
    {
        IsDirty = true;
        Status = null;
        RefreshPreview();
    }

    internal void ApplyColor(Color color) => SelectedZone.Color = color;

    [RelayCommand]
    private void ApplyToAll()
    {
        foreach (var zone in Zones)
        {
            zone.Color = SelectedZone.Color;
        }
    }

    [RelayCommand]
    private void Save()
    {
        IsDirty = false;
        Status = "Prévia: configuração guardada só nesta janela. Nada foi enviado ao teclado.";
    }

    private void RefreshPreview()
    {
        if (Zones is null)
        {
            return; // ainda construindo as zonas
        }

        // O brilho escurece a cor na prévia (nunca some de todo enquanto ligada).
        var level = 0.15 + (0.85 * Brightness / 100);
        for (var i = 0; i < Zones.Count; i++)
        {
            var zone = Zones[i];
            var color = UsesZoneColors ? zone.Color : Rainbow[UsesDirection && DirectionIndex == 1 ? Zones.Count - 1 - i : i];
            zone.PreviewColor = IsOff || !zone.IsOn ? KeyboardOff : Dim(color, level);
        }
    }

    private static Color Dim(Color c, double level) => Color.FromRgb(Mix(KeyboardOff.R, c.R, level), Mix(KeyboardOff.G, c.G, level), Mix(KeyboardOff.B, c.B, level));

    private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));
}

/// <summary>Uma zona do teclado.</summary>
public sealed partial class LightZoneViewModel : ObservableObject
{
    private readonly LightingViewModel _owner;
    private bool _syncing;

    public LightZoneViewModel(LightingViewModel owner, int number, Color color)
    {
        _owner = owner;
        Number = number;
        Color = color;
        Hex = ToHex(color);
    }

    public int Number { get; }

    /// <summary>Teclas desenhadas na prévia (5 fileiras × 4).</summary>
    public IReadOnlyList<int> KeySlots { get; } = [.. Enumerable.Range(0, 20)];

    public string Name => "Zona " + Number.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty] public partial Color Color { get; set; }

    /// <summary>Cor em texto (#RRGGBB) para digitar uma cor própria.</summary>
    [ObservableProperty] public partial string Hex { get; set; } = "";

    [ObservableProperty] public partial bool HexIsInvalid { get; set; }

    [ObservableProperty] public partial bool IsOn { get; set; } = true;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>Cor mostrada no desenho do teclado (depende do efeito e do liga/desliga).</summary>
    [ObservableProperty] public partial Color PreviewColor { get; set; }

    partial void OnColorChanged(Color value)
    {
        if (!_syncing)
        {
            _syncing = true;
            Hex = ToHex(value);
            HexIsInvalid = false;
            _syncing = false;
        }

        _owner.Changed();
    }

    partial void OnHexChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        var text = value.Trim();
        if (text.Length == 6 && !text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (text.Length == 7 && Color.TryParse(text, out var color))
        {
            HexIsInvalid = false;
            _syncing = true;
            Color = color;
            _syncing = false;
        }
        else
        {
            HexIsInvalid = true;
        }
    }

    partial void OnIsOnChanged(bool value) => _owner.Changed();

    [RelayCommand]
    private void Select() => _owner.SelectedZone = this;

    private static string ToHex(Color c) => "#" + c.R.ToString("X2", CultureInfo.InvariantCulture)
        + c.G.ToString("X2", CultureInfo.InvariantCulture)
        + c.B.ToString("X2", CultureInfo.InvariantCulture);
}

/// <summary>Uma cor pronta da paleta.</summary>
public sealed partial class SwatchViewModel : ObservableObject
{
    private readonly LightingViewModel _owner;

    public SwatchViewModel(LightingViewModel owner, Color color)
    {
        _owner = owner;
        Color = color;
    }

    public Color Color { get; }

    [RelayCommand]
    private void Apply() => _owner.ApplyColor(Color);
}
