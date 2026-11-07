using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Iluminação: teclado de 4 zonas, como o PredatorSense (estático com cor por zona e os efeitos animados
/// da Acer). O serviço guarda a configuração e só grava no teclado com "Controlar a luz" ligado — vem
/// DESLIGADO até o teste no notebook (docs/testes-hardware.md, T16).
/// </summary>
public sealed partial class LightingViewModel : ObservableObject
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    // Paleta pronta; a primeira é o ciano de fábrica da Acer.
    private static readonly string[] PaletteHex = ["#00AEC7", "#FF4D5E", "#FF8A3D", "#FFC94D", "#4DFF8B", "#5EC8FF", "#4D6BFF", "#8B7BFF", "#D46BFF", "#FFFFFF"];
    private static readonly Color KeyboardOff = Color.Parse("#141417");
    private static readonly Color[] Rainbow = [Color.Parse("#FF4D5E"), Color.Parse("#FFC94D"), Color.Parse("#4DFF8B"), Color.Parse("#4DA6FF")];

    private readonly ServiceClient? _client;
    private readonly DashboardViewModel? _dashboard;
    private KeyboardLighting? _current;
    private bool _applying;

    /// <summary>Só para o designer/prévia.</summary>
    public LightingViewModel()
        : this(null, null)
    {
    }

    public LightingViewModel(ServiceClient? client, DashboardViewModel? dashboard)
    {
        _client = client;
        _dashboard = dashboard;
        var cyan = ToAvalonia(KeyboardLighting.AcerCyan);
        Zones = [new(this, 1, cyan), new(this, 2, cyan), new(this, 3, cyan), new(this, 4, cyan)];
        EffectColor = new LightZoneViewModel(this, 0, cyan);
        Palette = [.. PaletteHex.Select(hex => new SwatchViewModel(this, Color.Parse(hex)))];
        SelectedZone = Zones[0];
        if (_dashboard is not null)
        {
            _dashboard.PropertyChanged += OnDashboardChanged;
        }

        RefreshPreview();
        IsDirty = false; // as zonas marcam "alterado" ao receber a cor inicial
    }

    public IReadOnlyList<LightZoneViewModel> Zones { get; }

    /// <summary>Cor dos efeitos de uma cor só (respiração, deslocamento, zoom, meteoro, cintilante).</summary>
    public LightZoneViewModel EffectColor { get; }

    public IReadOnlyList<SwatchViewModel> Palette { get; }

    public static IReadOnlyList<EffectOption> Effects { get; } =
    [
        new(KeyboardEffect.Static, "Estático", "Cada zona fica acesa com a sua cor. Clique numa zona para escolher a cor ou apagá-la."),
        new(KeyboardEffect.Breathing, "Respiração", "O teclado acende e apaga devagar, na cor escolhida."),
        new(KeyboardEffect.Neon, "Neon", "O teclado inteiro passa por todas as cores."),
        new(KeyboardEffect.Wave, "Onda", "As cores andam de um lado para o outro do teclado."),
        new(KeyboardEffect.Shifting, "Deslocamento", "A cor escolhida anda de um lado para o outro."),
        new(KeyboardEffect.Zoom, "Zoom", "Efeito Zoom da Acer, na cor escolhida."),
        new(KeyboardEffect.Meteor, "Meteoro", "Efeito Meteoro da Acer, na cor escolhida."),
        new(KeyboardEffect.Twinkling, "Cintilante", "Efeito Cintilante da Acer, na cor escolhida."),
    ];

    public static IReadOnlyList<string> Directions { get; } = ["Para a direita", "Para a esquerda"];

    [ObservableProperty] public partial LightZoneViewModel SelectedZone { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effect), nameof(UsesZoneColors), nameof(UsesEffectColor), nameof(UsesColor), nameof(UsesSpeed), nameof(UsesDirection), nameof(EffectHint), nameof(EditingColor), nameof(ColorTitle))]
    public partial int EffectIndex { get; set; }

    /// <summary>Brilho de 0 a 100 (<c>EC.KBBP</c>); 0 apaga o teclado.</summary>
    [ObservableProperty] public partial double Brightness { get; set; } = 100;

    /// <summary>Velocidade dos efeitos animados, 1 (lento) a 9 (rápido), como na DLL da Acer.</summary>
    [ObservableProperty] public partial double Speed { get; set; } = KeyboardLighting.DefaultSpeed;

    [ObservableProperty] public partial int DirectionIndex { get; set; }

    /// <summary>O killer-sense grava a luz no teclado (sem isso, só guarda a configuração).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReadKeyboard))]
    public partial bool Enabled { get; set; }

    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }

    /// <summary>Resumo do que o teclado tem agora (só com o controle ligado).</summary>
    [ObservableProperty] public partial string CurrentText { get; set; } = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReadKeyboard))]
    public partial bool HasCurrent { get; set; }

    public bool CanReadKeyboard => HasCurrent;

    public KeyboardEffect Effect => Effects[Math.Clamp(EffectIndex, 0, Effects.Count - 1)].Effect;

    public bool UsesZoneColors => Effect == KeyboardEffect.Static;

    public bool UsesEffectColor => KeyboardLighting.UsesColor(Effect);

    /// <summary>O efeito usa alguma cor escolhida (por zona ou única).</summary>
    public bool UsesColor => UsesZoneColors || UsesEffectColor;

    public bool UsesSpeed => KeyboardLighting.UsesSpeed(Effect);

    public bool UsesDirection => KeyboardLighting.UsesDirection(Effect);

    public string EffectHint => Effects[Math.Clamp(EffectIndex, 0, Effects.Count - 1)].Hint;

    /// <summary>A cor que a paleta e o campo hex editam agora: a zona escolhida (estático) ou a do efeito.</summary>
    public LightZoneViewModel EditingColor => UsesZoneColors ? SelectedZone : EffectColor;

    public string ColorTitle => UsesZoneColors ? $"Cor: {SelectedZone.Name}" : "Cor do efeito";

    partial void OnSelectedZoneChanged(LightZoneViewModel oldValue, LightZoneViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;
        OnPropertyChanged(nameof(EditingColor));
        OnPropertyChanged(nameof(ColorTitle));
    }

    partial void OnEffectIndexChanged(int value) => Changed();

    partial void OnBrightnessChanged(double value) => Changed();

    partial void OnSpeedChanged(double value) => Changed();

    partial void OnDirectionIndexChanged(int value) => Changed();

    partial void OnEnabledChanged(bool value) => Changed();

    internal void Changed()
    {
        RefreshPreview();
        if (_applying)
        {
            return;
        }

        IsDirty = true;
        Status = null;
    }

    internal void ApplyColor(Color color) => EditingColor.Color = color;

    /// <summary>Configuração que a tela mostra, no formato do serviço.</summary>
    public KeyboardLighting ToLighting()
    {
        var zones = default(ZoneColors);
        byte on = 0;
        for (var z = 0; z < Zones.Count; z++)
        {
            zones = zones.With(z, FromAvalonia(Zones[z].Color));
            if (Zones[z].IsOn)
            {
                on |= (byte)(1 << z);
            }
        }

        return new KeyboardLighting(
            Effect,
            (int)Math.Round(Brightness),
            Math.Clamp((int)Math.Round(Speed), KeyboardLighting.MinSpeed, KeyboardLighting.MaxSpeed),
            DirectionIndex == 1 ? LightDirection.Left : LightDirection.Right,
            FromAvalonia(EffectColor.Color),
            zones,
            on);
    }

    /// <summary>Mostra uma configuração na tela (sem marcar "alterado").</summary>
    public void Show(LightingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _applying = true;
        try
        {
            Enabled = settings.Enabled;
            ShowLighting(settings.Lighting);
        }
        finally
        {
            _applying = false;
        }

        IsDirty = false;
        RefreshPreview();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            var reply = await _client.RequestAsync(new IpcRequest(IpcRequestType.GetLighting), Timeout).ConfigureAwait(true);
            if (reply.Lighting?.TryToSettings(out _) is not { } settings)
            {
                SetStatus(reply.Error ?? "O serviço não devolveu a configuração da luz (versão antiga instalada?).", true);
                return;
            }

            Show(settings);
            IsLoaded = true;
            ShowCurrent(reply.LightingCurrent?.TryToLighting(out _), settings.Enabled, reply.Error);
            SetStatus(reply.Error, reply.Error is not null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível carregar: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private Task DiscardAsync() => LoadAsync();

    /// <summary>Traz para a tela o que o teclado tem agora (útil para partir do que o PredatorSense deixou).</summary>
    [RelayCommand]
    private void ReadKeyboard()
    {
        if (_current is null)
        {
            return;
        }

        ShowLighting(_current);
        SetStatus("Configuração lida do teclado (ainda não salva).", false);
    }

    [RelayCommand]
    private void ApplyToAll()
    {
        foreach (var zone in Zones)
        {
            zone.Color = SelectedZone.Color;
            zone.IsOn = true;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_client is null)
        {
            return;
        }

        var dto = new LightingSettingsDto(Enabled, KeyboardLightingDto.From(ToLighting()));
        if (dto.TryToSettings(out var error) is null)
        {
            SetStatus(error, true);
            return;
        }

        try
        {
            var ack = await _client.RequestAsync(new IpcRequest(IpcRequestType.SetLighting, Lighting: dto), Timeout).ConfigureAwait(true);
            if (ack.Ok == true)
            {
                IsDirty = false;
                SetStatus(ack.Error ?? (Enabled
                    ? "Salvo e gravado no teclado (conferido lendo de volta)."
                    : "Salvo. O controle da luz está desligado: nada foi enviado ao teclado."), false);
                await RefreshCurrentAsync().ConfigureAwait(true);
            }
            else
            {
                SetStatus(ack.Error ?? "O serviço recusou a configuração.", true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível salvar: {ex.Message}", true);
        }
    }

    /// <summary>Atualiza só o "no teclado agora", sem mexer no que está sendo editado.</summary>
    private async Task RefreshCurrentAsync()
    {
        try
        {
            var reply = await _client!.RequestAsync(new IpcRequest(IpcRequestType.GetLighting), Timeout).ConfigureAwait(true);
            ShowCurrent(reply.LightingCurrent?.TryToLighting(out _), reply.Lighting?.Enabled == true, reply.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            ShowCurrent(null, Enabled, ex.Message);
        }
    }

    private void ShowCurrent(KeyboardLighting? current, bool enabled, string? error)
    {
        _current = current;
        HasCurrent = current is not null;
        CurrentText = current is not null ? Describe(current)
            : !enabled ? "não lido (controle da luz desligado)"
            : error is not null ? "leitura falhou"
            : "—";
    }

    private void ShowLighting(KeyboardLighting l)
    {
        var wasApplying = _applying;
        _applying = true;
        try
        {
            EffectIndex = Math.Max(0, Effects.ToList().FindIndex(o => o.Effect == l.Effect));
            Brightness = l.Brightness;
            Speed = l.Speed;
            DirectionIndex = l.Direction == LightDirection.Left ? 1 : 0;
            EffectColor.Color = ToAvalonia(l.Color);
            for (var z = 0; z < Zones.Count; z++)
            {
                Zones[z].Color = ToAvalonia(l.Zones[z]);
                Zones[z].IsOn = (l.ZonesOn & (1 << z)) != 0;
            }
        }
        finally
        {
            _applying = wasApplying;
        }

        if (!wasApplying)
        {
            IsDirty = true;
        }

        RefreshPreview();
    }

    private static string Describe(KeyboardLighting l)
    {
        var name = Effects.FirstOrDefault(o => o.Effect == l.Effect)?.Name ?? l.Effect.ToString();
        var parts = new List<string> { name, $"brilho {l.Brightness}%" };
        if (KeyboardLighting.UsesSpeed(l.Effect))
        {
            parts.Add($"velocidade {l.Speed}");
        }

        if (KeyboardLighting.UsesDirection(l.Effect))
        {
            parts.Add(l.Direction == LightDirection.Left ? "para a esquerda" : "para a direita");
        }

        if (KeyboardLighting.UsesColor(l.Effect))
        {
            parts.Add(l.Color.ToHex());
        }

        if (l.Effect == KeyboardEffect.Static)
        {
            parts.Add(string.Join(" ", Enumerable.Range(0, ZoneColors.Count).Select(z => (l.ZonesOn & (1 << z)) != 0 ? l.Zones[z].ToHex() : "apagada")));
        }

        return string.Join(" · ", parts);
    }

    private void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.IsConnected) && _dashboard!.IsConnected && !IsLoaded)
        {
            _ = LoadAsync();
        }
    }

    private void RefreshPreview()
    {
        if (Zones is null || EffectColor is null)
        {
            return; // ainda construindo as zonas
        }

        // O brilho escurece a cor na prévia; 0 apaga.
        var level = Brightness <= 0 ? 0 : 0.15 + (0.85 * Brightness / 100);
        for (var i = 0; i < Zones.Count; i++)
        {
            var zone = Zones[i];
            var color = UsesZoneColors ? zone.Color
                : UsesEffectColor ? EffectColor.Color
                : Rainbow[UsesDirection && DirectionIndex == 1 ? Zones.Count - 1 - i : i];
            zone.PreviewColor = (UsesZoneColors && !zone.IsOn) || level == 0 ? KeyboardOff : Dim(color, level);
        }
    }

    private static Color ToAvalonia(RgbColor c) => Color.FromRgb(c.R, c.G, c.B);

    private static RgbColor FromAvalonia(Color c) => new(c.R, c.G, c.B);

    private static Color Dim(Color c, double level) => Color.FromRgb(Mix(KeyboardOff.R, c.R, level), Mix(KeyboardOff.G, c.G, level), Mix(KeyboardOff.B, c.B, level));

    private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));
}

/// <summary>Um efeito na lista.</summary>
public sealed record EffectOption(KeyboardEffect Effect, string Name, string Hint)
{
    public override string ToString() => Name;
}

/// <summary>Uma zona do teclado (ou a cor única dos efeitos, com número 0).</summary>
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

    public string Name => Number == 0 ? "Efeito" : "Zona " + Number.ToString(CultureInfo.InvariantCulture);

    /// <summary>Só as zonas do estático podem ser apagadas uma a uma.</summary>
    public bool CanTurnOff => Number > 0;

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

        _owner?.Changed();
    }

    partial void OnHexChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        if (RgbColor.TryParse(value, out var c))
        {
            HexIsInvalid = false;
            _syncing = true;
            Color = Color.FromRgb(c.R, c.G, c.B);
            _syncing = false;
        }
        else
        {
            HexIsInvalid = true;
        }
    }

    partial void OnIsOnChanged(bool value) => _owner?.Changed();

    [RelayCommand]
    private void Select() => _owner.SelectedZone = this;

    private static string ToHex(Color c) => new RgbColor(c.R, c.G, c.B).ToHex();
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
