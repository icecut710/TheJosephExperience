using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JosephExperience.Views
{
    using JosephExperience.Services;

    /// <summary>
    /// A lightweight vertical bar chart rendered via WPF Canvas (no external dependencies).
    /// Used for celebration activity and NADD price graphs.
    /// </summary>
    public class BarChartView : UserControl
    {
    private readonly Canvas _canvas;
    private readonly TextBlock _titleBlock;

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(BarChartView),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (BarChartView)d;
        ctrl._titleBlock.Text = (string)e.NewValue;
    }

    public BarChartView()
    {
        _canvas = new Canvas { Background = Brushes.Transparent };
        _titleBlock = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = SafeBrush("TextPrimaryBrush", Brushes.White)
        };

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(_titleBlock);
        panel.Children.Add(_canvas);
        Content = panel;

        _canvas.SizeChanged += (s, e) => Redraw();
    }

    private List<double> _values = new();
    private List<string> _labels = new();
    private double _maxValue;
    private bool _isPriceChart = false;

    public void SetData(List<double> values, List<string>? labels = null, bool isPriceChart = false)
    {
        _values = values ?? new();
        _labels = labels ?? new();
        _isPriceChart = isPriceChart;
        _maxValue = _values.Count > 0 ? _values.Max(v => v) : 1;
        if (_maxValue <= 0) _maxValue = 1;
        Redraw();
    }

    public void SetPriceData(List<double> closes, List<string>? timeLabels = null)
    {
        _values = closes ?? new();
        _labels = timeLabels ?? new();
        _maxValue = _values.Count > 0 ? _values.Max(v => v) : 1;
        if (_maxValue <= 0) _maxValue = 1;
        _isPriceChart = true;
        Redraw();
    }

    public void SetBarData(List<int> counts, List<string>? labels = null)
    {
        _values = counts.Select(c => (double)c).ToList();
        _labels = labels ?? new();
        _maxValue = _values.Count > 0 ? _values.Max(v => v) : 1;
        if (_maxValue <= 0) _maxValue = 1;
        _isPriceChart = false;
        Redraw();
    }

    /// <summary>Safely resolves a resource brush; falls back to <paramref name="fallback"/> when not found (e.g. in tests where Application.Current is null).</summary>
    private static Brush SafeBrush(string name, Brush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(name) is Brush b) return b;
        }
        catch { /* fall through */ }
        return fallback;
    }

    private void Redraw()
    {
        _canvas.Children.Clear();

        if (_values.Count == 0) return;

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var paddingTop = 12;
        var paddingBottom = _isPriceChart ? 18 : 20;
        var paddingLeft = _isPriceChart ? 36 : 24;
        var paddingRight = 16;

        var chartWidth = width - paddingLeft - paddingRight;
        var chartHeight = height - paddingTop - paddingBottom;
        if (chartWidth <= 0 || chartHeight <= 0) return;

        // Grid lines
        var gridLines = 4;
        for (int i = 0; i <= gridLines; i++)
        {
            var y = paddingTop + (chartHeight * i / gridLines);
            var line = new Line
            {
                X1 = paddingLeft,
                Y1 = y,
                X2 = width - paddingRight,
                Y2 = y,
                Stroke = SafeBrush("BorderBrush", Brushes.Gray),
                StrokeThickness = 1,
                Opacity = 0.3
            };
            _canvas.Children.Add(line);
        }

        if (_isPriceChart)
        {
            DrawPriceChart(chartWidth, chartHeight, paddingTop, paddingLeft, paddingRight, paddingBottom, width, height);
        }
        else
        {
            DrawBarChart(chartWidth, chartHeight, paddingTop, paddingLeft, paddingRight, paddingBottom, width, height);
        }
    }

    private void DrawBarChart(double chartWidth, double chartHeight, int paddingTop, double paddingLeft, double paddingRight, int paddingBottom, double width, double height)
    {
        var count = _values.Count;
        var slotW = chartWidth / Math.Max(1, count);
        var barW = Math.Max(2, slotW - 2);

        for (int i = 0; i < count; i++)
        {
            var barH = (_values[i] / _maxValue) * chartHeight;
            var x = paddingLeft + i * slotW;
            var y = height - paddingBottom - barH;

            var rect = new Rectangle
            {
                Width = barW,
                Height = Math.Max(1, barH),
                Fill = SafeBrush("AccentBrush", Brushes.Orange),
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            _canvas.Children.Add(rect);

            // Y-axis label (value)
            if (i % Math.Max(1, count / 6) == 0 || i == count - 1)
            {
                var valLabel = new TextBlock
                {
                    Text = _values[i].ToString("0"),
                    FontSize = 9,
                    Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(valLabel, x - 4);
                Canvas.SetTop(valLabel, y - 14);
                _canvas.Children.Add(valLabel);
            }

            // X-axis label (time)
            if (i < _labels.Count && _labels[i] is not null)
            {
                var timeLabel = new TextBlock
                {
                    Text = _labels[i],
                    FontSize = 8,
                    Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                    TextAlignment = TextAlignment.Center,
                    RenderTransform = new TranslateTransform(barW / 2 - 10, 0)
                };
                Canvas.SetLeft(timeLabel, x);
                Canvas.SetTop(timeLabel, height - paddingBottom + 4);
                _canvas.Children.Add(timeLabel);
            }
        }
    }

    private void DrawPriceChart(double chartWidth, double chartHeight, int paddingTop, double paddingLeft, double paddingRight, int paddingBottom, double width, double height)
    {
        var count = _values.Count;
        if (count < 2) return;

        var path = new Path();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var stepX = chartWidth / Math.Max(1, count - 1);
            var baseline = height - paddingBottom;

            // Area fill
            ctx.BeginFigure(
                new Point(paddingLeft, baseline - (_values[0] / _maxValue) * chartHeight),
                true,
                false);

            for (int i = 1; i < count; i++)
            {
                var x = paddingLeft + i * stepX;
                var y = baseline - (_values[i] / _maxValue) * chartHeight;
                ctx.LineTo(new Point(x, y), true, false);
            }

            // Close the area
            ctx.LineTo(new Point(width - paddingRight, baseline), true, false);
            ctx.LineTo(new Point(paddingLeft, baseline), true, false);
        }

        path.Data = geometry;
        path.Fill = new SolidColorBrush(Color.FromArgb(60, 0x91, 0x75, 0xFF));
        path.Stroke = SafeBrush("AccentBrush", Brushes.Orange);
        path.StrokeThickness = 1.5;
        _canvas.Children.Add(path);

        // Price labels
        for (int i = 0; i < count; i++)
        {
            if (i % Math.Max(1, count / 6) == 0 || i == count - 1)
            {
                var x = paddingLeft + i * (chartWidth / Math.Max(1, count - 1));
                var y = height - paddingBottom - (_values[i] / _maxValue) * chartHeight;

                var valLabel = new TextBlock
                {
                    Text = _isPriceChart ? RealNaddService.FormatPrice(_values[i]) : _values[i].ToString("0"),
                    FontSize = 8.5,
                    Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(valLabel, x - 20);
                Canvas.SetTop(valLabel, y - 12);
                _canvas.Children.Add(valLabel);
            }
        }

        // Y-axis price labels
        var priceFormatter = SafeBrush("TextMutedBrush", Brushes.Gray);
        for (int i = 0; i <= 4; i++)
        {
            var val = _maxValue * (1 - (double)i / 4);
            var y = paddingTop + (chartHeight * i / 4);

            var priceLabel = new TextBlock
            {
                Text = _isPriceChart ? RealNaddService.FormatPrice(val) : val.ToString("0"),
                FontSize = 8,
                Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(priceLabel, 2);
            Canvas.SetTop(priceLabel, y - 6);
            _canvas.Children.Add(priceLabel);
        }
    }
}
}
