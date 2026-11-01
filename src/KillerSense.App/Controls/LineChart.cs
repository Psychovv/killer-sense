using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KillerSense.App.Controls;

/// <summary>Uma série do gráfico: valores (NaN = sem leitura) e cor.</summary>
public sealed record ChartSeries(IReadOnlyList<double> Values, IBrush Brush);

/// <summary>Gráfico de linhas simples, desenhado direto (sem biblioteca), com escala fixa Min–Max.</summary>
public sealed class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LineChart, double>(nameof(Minimum), 30);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LineChart, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<LineChart, int>(nameof(Capacity), 120);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<LineChart, IBrush?>(nameof(GridBrush));

    static LineChart() =>
        AffectsRender<LineChart>(SeriesProperty, MinimumProperty, MaximumProperty, CapacityProperty, GridBrushProperty);

    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Quantos pontos cabem na largura (o mais novo fica à direita).</summary>
    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0 || Maximum <= Minimum || Capacity < 2)
        {
            return;
        }

        if (GridBrush is { } grid)
        {
            var pen = new Pen(grid, 1);
            for (var i = 0; i <= 3; i++)
            {
                var y = Math.Round(h * i / 3) + 0.5;
                context.DrawLine(pen, new Point(0, y), new Point(w, y));
            }
        }

        if (Series is null)
        {
            return;
        }

        var step = w / (Capacity - 1);
        foreach (var s in Series)
        {
            var values = s.Values;
            var offset = Capacity - values.Count; // alinha o mais novo à direita
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var open = false;
                for (var i = 0; i < values.Count; i++)
                {
                    var v = values[i];
                    if (double.IsNaN(v))
                    {
                        if (open)
                        {
                            g.EndFigure(false);
                            open = false;
                        }

                        continue;
                    }

                    var p = new Point((offset + i) * step, h - (Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1) * h));
                    if (!open)
                    {
                        g.BeginFigure(p, false);
                        open = true;
                    }
                    else
                    {
                        g.LineTo(p);
                    }
                }

                if (open)
                {
                    g.EndFigure(false);
                }
            }

            context.DrawGeometry(null, new Pen(s.Brush, 2, lineJoin: PenLineJoin.Round), geo);
        }
    }
}
