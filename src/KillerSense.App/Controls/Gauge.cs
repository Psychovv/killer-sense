using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KillerSense.App.Controls;

/// <summary>Arco de 270° (estilo mostrador). Sem valor ⇒ só o trilho.</summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<Gauge, double?>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<Gauge, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<Gauge, IBrush?>(nameof(ValueBrush));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(Thickness), 12);

    static Gauge() =>
        AffectsRender<Gauge>(ValueProperty, MaximumProperty, TrackBrushProperty, ValueBrushProperty, ThicknessProperty);

    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? ValueBrush
    {
        get => GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        const double start = 135;  // graus, a partir do eixo X, sentido horário
        const double sweep = 270;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var r = (size - Thickness) / 2;
        if (r <= 0)
        {
            return;
        }

        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        DrawArc(context, c, r, start, sweep, TrackBrush);
        if (Value is { } v && Maximum > 0)
        {
            var frac = Math.Clamp(v / Maximum, 0, 1);
            if (frac > 0.001)
            {
                DrawArc(context, c, r, start, sweep * frac, ValueBrush);
            }
        }
    }

    private void DrawArc(DrawingContext ctx, Point c, double r, double startDeg, double sweepDeg, IBrush? brush)
    {
        if (brush is null)
        {
            return;
        }

        static Point At(Point c, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(c.X + (r * Math.Cos(rad)), c.Y + (r * Math.Sin(rad)));
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(c, r, startDeg), false);
            g.ArcTo(At(c, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, new Pen(brush, Thickness, lineCap: PenLineCap.Round), geo);
    }
}
