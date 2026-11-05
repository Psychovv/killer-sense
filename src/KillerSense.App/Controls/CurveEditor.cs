using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KillerSense.App.Settings;
using KillerSense.Core;

namespace KillerSense.App.Controls;

/// <summary>
/// Editor de curva de fan: pontos arrastáveis (cada ponto fica entre os vizinhos, então a curva nunca
/// desce), faixa do Auto do EC abaixo da temperatura de entrada, faixa abaixo do piso de segurança,
/// temperatura atual e RPM estimado (modelo linear medido no teste A17).
/// </summary>
public sealed class CurveEditor : Control
{
    private const double MinT = 30;
    private const double MaxT = 100;
    private const double PadL = 40;
    private const double PadR = 12;
    private const double PadT = 12;
    private const double PadB = 26;
    private static readonly NumberFormatInfo Pt = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private static readonly Typeface Face = new("Segoe UI");
    private int _drag = -1;

    public static readonly StyledProperty<IReadOnlyList<CurvePoint>?> PointsProperty =
        AvaloniaProperty.Register<CurveEditor, IReadOnlyList<CurvePoint>?>(nameof(Points), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> EntryTemperatureProperty =
        AvaloniaProperty.Register<CurveEditor, double>(nameof(EntryTemperature), 60);

    public static readonly StyledProperty<double> FloorProperty =
        AvaloniaProperty.Register<CurveEditor, double>(nameof(Floor));

    public static readonly StyledProperty<double?> CurrentTemperatureProperty =
        AvaloniaProperty.Register<CurveEditor, double?>(nameof(CurrentTemperature));

    public static readonly StyledProperty<double> RpmBaseProperty =
        AvaloniaProperty.Register<CurveEditor, double>(nameof(RpmBase), 2200);

    public static readonly StyledProperty<double> RpmPerPercentProperty =
        AvaloniaProperty.Register<CurveEditor, double>(nameof(RpmPerPercent), 48);

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<CurveEditor, IBrush?>(nameof(CurveBrush));

    static CurveEditor() =>
        AffectsRender<CurveEditor>(PointsProperty, EntryTemperatureProperty, FloorProperty, CurrentTemperatureProperty, CurveBrushProperty);

    public IReadOnlyList<CurvePoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public double EntryTemperature
    {
        get => GetValue(EntryTemperatureProperty);
        set => SetValue(EntryTemperatureProperty, value);
    }

    /// <summary>Piso de segurança (%) do modo térmico atual.</summary>
    public double Floor
    {
        get => GetValue(FloorProperty);
        set => SetValue(FloorProperty, value);
    }

    public double? CurrentTemperature
    {
        get => GetValue(CurrentTemperatureProperty);
        set => SetValue(CurrentTemperatureProperty, value);
    }

    public double RpmBase
    {
        get => GetValue(RpmBaseProperty);
        set => SetValue(RpmBaseProperty, value);
    }

    public double RpmPerPercent
    {
        get => GetValue(RpmPerPercentProperty);
        set => SetValue(RpmPerPercentProperty, value);
    }

    public IBrush? CurveBrush
    {
        get => GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    private double X(double t) => PadL + ((t - MinT) / (MaxT - MinT) * (Bounds.Width - PadL - PadR));

    private double Y(double p) => Bounds.Height - PadB - (p / 100 * (Bounds.Height - PadT - PadB));

    private double T(double x) => MinT + ((x - PadL) / (Bounds.Width - PadL - PadR) * (MaxT - MinT));

    private double P(double y) => (Bounds.Height - PadB - y) / (Bounds.Height - PadT - PadB) * 100;

    public override void Render(DrawingContext context)
    {
        var ctx = context;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 120 || h < 80)
        {
            return;
        }

        var line = new Pen(new SolidColorBrush(Color.Parse("#34343D")), 1);
        var muted = new SolidColorBrush(Color.Parse("#9A9AA6"));
        var curve = CurveBrush ?? Brushes.MediumPurple;

        // Faixa do Auto do EC (abaixo da entrada) e faixa abaixo do piso.
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#2C2C34")), new Rect(new Point(X(MinT), Y(100)), new Point(X(Math.Clamp(EntryTemperature, MinT, MaxT)), Y(0))));
        if (Floor > 0)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#FF6B7A"), 0.12), new Rect(new Point(X(MinT), Y(Floor)), new Point(X(MaxT), Y(0))));
        }

        for (var t = 30; t <= 100; t += 10)
        {
            ctx.DrawLine(line, new Point(X(t), Y(100)), new Point(X(t), Y(0)));
            Text(ctx, Temperatures.Number(t) + "°", X(t), h - 18, muted, center: true);
        }

        for (var p = 0; p <= 100; p += 25)
        {
            ctx.DrawLine(line, new Point(X(MinT), Y(p)), new Point(X(MaxT), Y(p)));
            Text(ctx, $"{p}%", 4, Y(p) - 8, muted);
        }

        Text(ctx, "Auto do EC", X(MinT) + 6, Y(100) + 4, muted);

        var pts = Points;
        if (pts is { Count: >= 2 })
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(pts[0].Temperature), Y(pts[0].Percent)), false);
                foreach (var pt in pts.Skip(1))
                {
                    g.LineTo(new Point(X(pt.Temperature), Y(pt.Percent)));
                }

                g.EndFigure(false);
            }

            ctx.DrawGeometry(null, new Pen(curve, 3, lineJoin: PenLineJoin.Round), geo);
            for (var i = 0; i < pts.Count; i++)
            {
                var c = new Point(X(pts[i].Temperature), Y(pts[i].Percent));
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#1C1C20")), new Pen(curve, 3), c, i == _drag ? 9 : 7, i == _drag ? 9 : 7);
            }

            if (_drag >= 0 && _drag < pts.Count)
            {
                var d = pts[_drag];
                var rpm = RpmBase + (RpmPerPercent * d.Percent);
                Text(ctx, $"{Temperatures.Text(d.Temperature)} → {d.Percent:0}%  (~{rpm.ToString("N0", Pt)} rpm)", Math.Min(X(d.Temperature) + 12, w - 230), Y(d.Percent) - 26, Brushes.White);
            }
        }

        if (CurrentTemperature is { } now && now >= MinT && now <= MaxT)
        {
            var dash = new Pen(new SolidColorBrush(Color.Parse("#F0B450")), 1.5, new DashStyle([4, 4], 0));
            ctx.DrawLine(dash, new Point(X(now), Y(100)), new Point(X(now), Y(0)));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Points is not { } pts)
        {
            return;
        }

        var pos = e.GetPosition(this);
        for (var i = 0; i < pts.Count; i++)
        {
            var c = new Point(X(pts[i].Temperature), Y(pts[i].Percent));
            if (Math.Abs(c.X - pos.X) <= 12 && Math.Abs(c.Y - pos.Y) <= 12)
            {
                _drag = i;
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0 || Points is not { } pts)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var i = _drag;
        var lowT = i > 0 ? pts[i - 1].Temperature + 1 : MinT;
        var highT = i < pts.Count - 1 ? pts[i + 1].Temperature - 1 : MaxT;
        var lowP = i > 0 ? pts[i - 1].Percent : 0;
        var highP = i < pts.Count - 1 ? pts[i + 1].Percent : 100;
        var t = Math.Round(Math.Clamp(T(pos.X), lowT, highT));
        var p = Math.Round(Math.Clamp(P(pos.Y), lowP, highP));
        if (t != pts[i].Temperature || p != pts[i].Percent)
        {
            var copy = pts.ToArray();
            copy[i] = new CurvePoint(t, p);
            Points = copy; // nova lista ⇒ a ligação TwoWay avisa o ViewModel
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag >= 0)
        {
            _drag = -1;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
    }

    private static void Text(DrawingContext ctx, string s, double x, double y, IBrush brush, bool center = false)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, brush);
        ctx.DrawText(ft, new Point(center ? x - (ft.Width / 2) : x, y));
    }
}
