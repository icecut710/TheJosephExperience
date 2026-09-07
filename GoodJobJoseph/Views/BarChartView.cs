using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace JosephExperience.Views
{
    using JosephExperience.Services;

    /// <summary>
    /// A lightweight vertical bar / price-area chart rendered via WPF Canvas (no external dependencies).
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
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = SafeBrush("TextPrimaryBrush", Brushes.White)
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
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

        /// <summary>Safely resolves a resource brush; falls back to <paramref name="fallback"/> when not found.</summary>
        private static Brush SafeBrush(string name, Brush fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(name) is Brush b) return b;
            }
            catch { /* fall through */ }
            return fallback;
        }

        /// <summary>Measures rendered width of <paramref name="text"/> at <paramref name="fontSize"/>.</summary>
        private double MeasureTextWidth(string text, double fontSize)
        {
            try
            {
                var ft = new FormattedText(
                    text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    fontSize,
                    Brushes.Black,
                    VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
                return ft.Width;
            }
            catch
            {
                return text.Length * fontSize * 0.55;
            }
        }

        private string FormatValue(double val) =>
            _isPriceChart ? RealNaddService.FormatPrice(val) : val.ToString("0");

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
                    StrokeThickness = i == 0 ? 1 : 1,
                    Opacity = i == 0 ? 0.4 : 0.25
                };
                _canvas.Children.Add(line);

                // Tick label on left axis
                if (i > 0)
                {
                    var tickVal = _maxValue * (1 - (double)i / gridLines);
                    var tickLabel = new TextBlock
                    {
                        Text = FormatValue(tickVal),
                        FontSize = 8.5,
                        FontWeight = FontWeights.Medium,
                        Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                        TextAlignment = TextAlignment.Right
                    };
                    tickLabel.Measure(new Size(60, 20));
                    Canvas.SetRight(tickLabel, width - paddingLeft);
                    Canvas.SetTop(tickLabel, y - tickLabel.DesiredSize.Height / 2);
                    _canvas.Children.Add(tickLabel);
                }
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

            // Find peak bar index for highlighting
            var peakValue = _values.Count > 0 ? _values.Max() : 0;
            var total = _values.Sum();
            var avg = count > 0 ? total / count : 0;

                        // Empty state — friendly note instead of a forest of 1px slivers.
            if (total <= 0)
            {
                var empty = new TextBlock
                {
                    Text = "No celebrations in this range yet — go trigger Joseph!",
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                    TextAlignment = TextAlignment.Center
                };
                empty.Measure(new Size(width, 40));
                Canvas.SetLeft(empty, (width - empty.DesiredSize.Width) / 2);
                Canvas.SetTop(empty, height / 2 - 10);
                _canvas.Children.Add(empty);
                return;
            }

            // Dashed average line for quick "above/below typical" reading.
            if (avg > 0 && avg < peakValue)
            {
                var avgY = height - paddingBottom - (avg / _maxValue) * chartHeight;
                var avgLine = new Line
                {
                    X1 = paddingLeft,
                    Y1 = avgY,
                    X2 = width - paddingRight,
                    Y2 = avgY,
                    Stroke = SafeBrush("TextMutedBrush", Brushes.Gray),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Opacity = 0.55
                };
                avgLine.ToolTip = new ToolTip { Content = $"Average: {avg:0.0} per bucket" };
                _canvas.Children.Add(avgLine);
            }

            var accentGlow = ((SolidColorBrush)SafeBrush("AccentBrush", Brushes.Orange)).Color;

            for (int i = 0; i < count; i++)
            {
                var barH = (_values[i] / _maxValue) * chartHeight;
                var x = paddingLeft + i * slotW;
                var y = height - paddingBottom - barH;

                var isPeak = _values[i] >= peakValue && peakValue > 0;
                var intensity = _values[i] / _maxValue;  // 0.0 to 1.0

                // Zero days render as a subtle stub rather than a 1px sliver.
                var isZero = _values[i] <= 0;
                FrameworkElement bar;
                if (isZero)
                {
                    bar = new Rectangle
                    {
                        Width = Math.Min(barW, 6),
                        Height = 2,
                        Fill = SafeBrush("BorderBrush", Brushes.DimGray),
                        RadiusX = 1,
                        RadiusY = 1,
                        Opacity = 0.8
                    };
                }
                else
                {
                    bar = new Rectangle
                    {
                        Width = barW,
                        Height = Math.Max(3, barH),
                        Fill = BarGradient(intensity, isPeak),
                        RadiusX = 3,
                        RadiusY = 3
                    };
                }
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, isZero ? height - paddingBottom - 2 : y);
                _canvas.Children.Add(bar);

                // Glow effect on peak bar (accent, on-palette)
                if (isPeak && !isZero)
                {
                    bar.Effect = new DropShadowEffect
                    {
                        BlurRadius = 10,
                        Color = accentGlow,
                        Opacity = 0.55,
                        ShadowDepth = 0
                    };
                }

                // Hover tooltip
                var tooltip = new ToolTip
                {
                    Content = $"{_labels.ElementAtOrDefault(i) ?? ""}: {_values[i]}"
                };
                bar.ToolTip = tooltip;

                // Y-axis label (value) — right-aligned to bar center
                if (!isZero && (i % Math.Max(1, count / 6) == 0 || i == count - 1))
                {
                    var valStr = _values[i].ToString("0");
                    var valW = MeasureTextWidth(valStr, 10);
                    var valLabel = new TextBlock
                    {
                        Text = valStr,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                        TextAlignment = TextAlignment.Center
                    };
                    valLabel.Measure(new Size(200, 30));
                    Canvas.SetLeft(valLabel, x + barW / 2 - valW / 2);
                    Canvas.SetTop(valLabel, y - 14);
                    _canvas.Children.Add(valLabel);
                }

                // X-axis label (time) — centered under bar
                if (i < _labels.Count && _labels[i] is not null)
                {
                    var labelStr = _labels[i];
                    var labelW = MeasureTextWidth(labelStr, 9);
                    var timeLabel = new TextBlock
                    {
                        Text = labelStr,
                        FontSize = 9,
                        FontWeight = FontWeights.Medium,
                        Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                        TextAlignment = TextAlignment.Center
                    };
                    timeLabel.Measure(new Size(200, 30));
                    Canvas.SetLeft(timeLabel, x + barW / 2 - labelW / 2);
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

            // Gradient area fill (accent → success by intensity)
            var areaColor = ((SolidColorBrush)SafeBrush("AccentBrush", Brushes.Orange)).Color;
            var successColor = ((SolidColorBrush)SafeBrush("SuccessBrush", Brushes.Green)).Color;
            var gradient = new LinearGradientBrush(areaColor, successColor, 90);
            path.Fill = new SolidColorBrush(areaColor) { Opacity = 0.18 };
            path.Opacity = 0.9;
            path.Stroke = SafeBrush("AccentBrush", Brushes.Orange);
            path.StrokeThickness = 1.8;
            path.StrokeLineJoin = PenLineJoin.Round;

            // Glow effect on the price line
            path.Effect = new DropShadowEffect
            {
                BlurRadius = 6,
                Color = areaColor,
                Opacity = 0.35,
                ShadowDepth = 0
            };

            _canvas.Children.Add(path);

            // Data point markers + tooltips
            var peakIdx = Array.IndexOf(_values.ToArray(), _values.Max());
            for (int i = 0; i < count; i++)
            {
                var x = paddingLeft + i * (chartWidth / Math.Max(1, count - 1));
                var y = height - paddingBottom - (_values[i] / _maxValue) * chartHeight;

                var isPeak = i == peakIdx;
                var pt = new Ellipse
                {
                    Width = isPeak ? 7 : 4,
                    Height = isPeak ? 7 : 4,
                    Fill = isPeak ? SafeBrush("SuccessBrush", Brushes.Green) : SafeBrush("AccentBrush", Brushes.Orange),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Opacity = 0.9
                };
                Canvas.SetLeft(pt, x - pt.Width / 2);
                Canvas.SetTop(pt, y - pt.Height / 2);
                pt.ToolTip = new ToolTip { Content = $"{_labels.ElementAtOrDefault(i) ?? ""}\n{FormatValue(_values[i])}" };
                _canvas.Children.Add(pt);

                // Green glow on peak
                if (isPeak)
                {
                    pt.Effect = new DropShadowEffect
                    {
                        BlurRadius = 10,
                        Color = Colors.Green,
                        Opacity = 0.5,
                        ShadowDepth = 0
                    };
                }
            }

            // Price labels above data points
            for (int i = 0; i < count; i++)
            {
                if (i % Math.Max(1, count / 6) == 0 || i == count - 1)
                {
                    var x = paddingLeft + i * (chartWidth / Math.Max(1, count - 1));
                    var y = height - paddingBottom - (_values[i] / _maxValue) * chartHeight;

                    var priceStr = FormatValue(_values[i]);
                    var priceW = MeasureTextWidth(priceStr, 8.5);
                    var valLabel = new TextBlock
                    {
                        Text = priceStr,
                        FontSize = 8.5,
                        Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                        TextAlignment = TextAlignment.Right
                    };
                    valLabel.Measure(new Size(200, 30));
                    Canvas.SetLeft(valLabel, x - priceW - 2);
                    Canvas.SetTop(valLabel, y - 12);
                    _canvas.Children.Add(valLabel);
                }
            }

            // Y-axis price labels
            for (int i = 0; i <= 4; i++)
            {
                var val = _maxValue * (1 - (double)i / 4);
                var y = paddingTop + (chartHeight * i / 4);

                var labelStr = FormatValue(val);
                var labelW = MeasureTextWidth(labelStr, 8);
                var priceLabel = new TextBlock
                {
                    Text = labelStr,
                    FontSize = 8,
                    Foreground = SafeBrush("TextMutedBrush", Brushes.Gray),
                    TextAlignment = TextAlignment.Right
                };
                priceLabel.Measure(new Size(200, 30));
                Canvas.SetLeft(priceLabel, paddingLeft - 4 - labelW);
                Canvas.SetTop(priceLabel, y - 6);
                _canvas.Children.Add(priceLabel);
            }
        }

        /// <summary>Creates a vertical gradient brush from accent to success based on bar intensity.</summary>
        private Brush BarGradient(double intensity, bool isPeak)
        {
            try
            {
                var accent = SafeBrush("AccentBrush", Brushes.Orange);
                var success = SafeBrush("SuccessBrush", Brushes.Green);

                if (accent is SolidColorBrush accentScb && success is SolidColorBrush successScb)
                {
                    var c1 = accentScb.Color;
                    var c2 = successScb.Color;

                    if (isPeak)
                    {
                        c1 = Color.FromArgb(220, c1.R, c1.G, c1.B);
                        c2 = Color.FromArgb(220, c2.R, c2.G, c2.B);
                    }
                    else
                    {
                        var alpha = (byte)(80 + intensity * 120);
                        c1 = Color.FromArgb(alpha, c1.R, c1.G, c1.B);
                        c2 = Color.FromArgb(alpha, c2.R, c2.G, c2.B);
                    }

                    return new LinearGradientBrush(c1, c2, 90);
                }
            }
            catch { /* fall through to solid */ }
            return SafeBrush("AccentBrush", Brushes.Orange);
        }
    }
}
