using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JosephExperience.Models;
using JosephExperience.Services;
using JosephExperience.Utilities;

namespace JosephExperience.Views;

public partial class OverlayWindow : Window
{
    private bool _animationRunning;
    private Action? _onCompleted;
    private DispatcherTimer? _holdTimer;
    private static readonly Random _rng = Random.Shared;
    private ExitStyle _activeExitStyle = ExitStyle.Fade;
    private AnimationStyle _entryStyle = AnimationStyle.Fade;
    private EasingStyle _easingStyle = EasingStyle.EaseOut;
    private ScaleTransform? _animScale;
    private TranslateTransform? _animTranslate;
    private RotateTransform? _animRotate;
    private AnimationStyle? _lastRandomStyle;

    // Cached layout inputs so a late-measured window (or a DPI/monitor change)
    // can re-fit the image instead of leaving the 1600x900 fallback in place.
    private AppSettings? _lateSettings;
    private BitmapSource? _lateSource;
    private double _lateScale;
    private Rect? _latePlacement;

    public OverlayWindow()
    {
        InitializeComponent();
        SizeChanged += OverlayWindow_SizeChanged;
    }

    public void ShowOverlay(BitmapSource? imageSource, AppSettings settings, Action? onCompleted, string? cacheKey = null)
        => ShowOverlay(imageSource, settings, null, null, null, onCompleted, cacheKey);

    public void ShowOverlay(BitmapSource? imageSource, AppSettings settings, Rect? placement, string? quoteText, CelebrationResolver.ResolvedResult? resolved, Action? onCompleted, string? cacheKey = null)
    {
        _onCompleted = onCompleted;
        _animationRunning = false;
        StopHoldTimer();

        var imageOpacity = Math.Clamp(settings.ImageOpacity, 0.0, 1.0);
        var scale = Math.Clamp(settings.OverlayScale, 0.25, 3.0);

        JosephImage.RenderTransform = null;
        JosephImage.Opacity = 0;

        if (imageSource is not null)
        {
            if (settings.TrimTransparentBounds)
                imageSource = TrimTransparentBounds(imageSource, cacheKey);

            JosephImage.Source = imageSource;
            JosephImage.Visibility = Visibility.Visible;
            JosephPanel.Visibility = Visibility.Visible;
        }
        else
        {
            var cb = _onCompleted;
            _onCompleted = null;
            cb?.Invoke();
            return;
        }

        ApplySizeAndPosition(settings, imageSource, scale, placement);

        var effectiveTextMode = resolved?.TextMode ?? settings.TextMode;
        var effectiveQuote = quoteText ?? resolved?.ResolvedQuote ?? settings.CelebrationText;
        var showText = settings.ShowCelebrationText
                       && effectiveTextMode != TextMode.NoText
                       && !string.IsNullOrWhiteSpace(effectiveQuote)
                       && settings.TextPosition != TextPosition.Hidden;

        if (showText)
        {
            CelebrationTextBlock.Text = NormalizeQuoteText(effectiveQuote);
            CelebrationTextBlock.FontSize = ResolveFontSize(settings.TextFontSizePreset, settings.TextFontSize);
            CelebrationTextBlock.Opacity = Math.Clamp(settings.TextOpacity, 0.40, 1.0);
            CelebrationTextBlock.Foreground = new SolidColorBrush(settings.TextColor);
            CelebrationTextBlock.FontWeight = settings.TextWeight switch
            {
                Models.TextWeight.Normal => FontWeights.Normal,
                Models.TextWeight.SemiBold => FontWeights.SemiBold,
                Models.TextWeight.Bold => FontWeights.Bold,
                Models.TextWeight.ExtraBold => FontWeights.Black,
                _ => FontWeights.Bold
            };
            // One-line layout: text must NOT wrap. Font shrinks to fit instead (auto-fit below).
            CelebrationTextBlock.TextWrapping = TextWrapping.NoWrap;
            CelebrationTextBlock.TextAlignment = System.Windows.TextAlignment.Center;
            CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
            CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;
            // Constrain to 90% of the overlay viewport (5% safe margin each side).
            var maxTextWidth = Math.Max(200, ActualWidth * 0.9);
            CelebrationTextBlock.MaxWidth = settings.TextMaxWidth > 0 ? Math.Min(settings.TextMaxWidth, maxTextWidth) : maxTextWidth;
            // Auto-fit: shrink font until the full quote fits on one line, with a floor.
            AutoFitFontSize(settings, CelebrationTextBlock);
            CelebrationTextBlock.Effect = BuildTextEffect(settings);
            ApplyTextPosition(settings);
            CelebrationTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            CelebrationTextBlock.Visibility = Visibility.Collapsed;
        }

        Opacity = 1.0;

        var anim = resolved?.ImageAnimation ?? ResolveAnimation(settings);
        var duration = Math.Max(400, settings.OverlayDurationMs);
        var resolvedDuration = resolved?.DurationMs ?? duration;
        var speedMul = settings.EntrySpeed switch
        {
            EntrySpeed.Slow => 1.6,
            EntrySpeed.Normal => 1.0,
            EntrySpeed.Fast => 0.6,
            EntrySpeed.Instant => 0.1,
            _ => 1.0
        };
        var entryMs = settings.EntryDurationMs > 0 ? settings.EntryDurationMs : (int)(Math.Max(80, resolvedDuration / 5) * speedMul);
        var exitMs = settings.ExitDurationMs > 0 ? settings.ExitDurationMs : Math.Max(80, resolvedDuration / 4);

        if (entryMs + exitMs > resolvedDuration)
        {
            var cut = (int)Math.Ceiling((entryMs + exitMs - resolvedDuration) / 2.0);
            entryMs = Math.Max(0, entryMs - cut);
            exitMs = Math.Max(0, exitMs - cut);
        }

        LogResolved(settings, anim, resolvedDuration, entryMs, exitMs);

        _entryStyle = anim;
        _easingStyle = settings.EasingStyle;
        RunAnimation(anim, resolvedDuration, entryMs, exitMs, imageOpacity, scale, settings.EasingStyle, settings.ExitStyle);

        if (showText && resolved is not null)
        {
            var delay = (int)(resolved.TextDelayMs > 0 ? resolved.TextDelayMs : entryMs * 0.6);
            var textFx = resolved.TextFx;
            var txtScale = resolved.TextScale;
            var intensity = resolved.Intensity;
            var remaining = resolvedDuration - delay;
            if (remaining <= 0) remaining = exitMs > 0 ? exitMs : 400;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ApplyTextFx(textFx, intensity, remaining, txtScale);
            };
            timer.Start();
        }
    }

    /// <summary>Honours Random animation (with exclusions + anti-repeat), else the fixed style.</summary>
    private AnimationStyle ResolveAnimation(AppSettings settings)
    {
        if (!settings.RandomAnimation && settings.AnimationStyle != AnimationStyle.Random)
        {
            return settings.AnimationStyle;
        }
        return PickRandomAnimation(settings);
    }

    private AnimationStyle PickRandomAnimation(AppSettings settings)
    {
        var candidates = Enum.GetValues<AnimationStyle>()
            .Where(v => v != AnimationStyle.Random)
            .Where(v => v != AnimationStyle.None || !settings.ExcludeNoneFromRandom)
            .Where(v => v != AnimationStyle.Jumpscare || !settings.ExcludeJumpscareFromRandom)
            .ToArray();
        if (candidates.Length == 0)
        {
            return AnimationStyle.Fade;
        }
        if (settings.AvoidImmediateRepeats && _lastRandomStyle is { } last && candidates.Length > 1)
        {
            candidates = candidates.Where(v => v != last).ToArray();
        }
        var picked = candidates[_rng.Next(candidates.Length)];
        _lastRandomStyle = picked;
        return picked;
    }

    /// <summary>Single diagnostic line per trigger proving UI settings reached the runtime (Part 27).</summary>
    private static void LogResolved(AppSettings s, AnimationStyle resolved, int total, int entry, int exit)
    {
        AppLog.Info(
            $"Celebration: style={resolved} (configured={s.AnimationStyle}, random={s.RandomAnimation}), " +
            $"easing={s.EasingStyle}, speed={s.EntrySpeed}, entry={entry}ms, exit={s.ExitStyle}/{exit}ms, " +
            $"total={total}ms, opacity={s.ImageOpacity:0.00}, scale={s.OverlayScale:0.00}, " +
            $"position={s.ImagePosition}, monitor={s.MonitorMode}, selection={s.ImageMode}, text={s.ShowCelebrationText}");
    }

    private static readonly Dictionary<BitmapSource, BitmapSource> _trimCache = new();
    private static readonly Dictionary<string, BitmapSource> _trimKeyedCache = new(StringComparer.Ordinal);
    private const int TrimKeyedCacheMaxEntries = 64;

    /// <summary>Pixel formats that are guaranteed to carry no alpha channel (fully opaque).</summary>
    private static readonly HashSet<PixelFormat> NoAlphaFormats = new()
    {
        PixelFormats.Bgr24, PixelFormats.Bgr32, PixelFormats.Bgr555, PixelFormats.Bgr565,
        PixelFormats.Gray2, PixelFormats.Gray4, PixelFormats.Gray8, PixelFormats.Gray16,
        PixelFormats.Rgb24, PixelFormats.Rgb48, PixelFormats.Rgb128Float, PixelFormats.Cmyk32
    };

    private void ApplySizeAndPosition(AppSettings settings, BitmapSource? source, double scale, Rect? placement = null)
    {
        if (source is null) return;

        // Cache the exact layout inputs for re-fit on Late measurement / DPI change.
        _lateSettings = settings;
        _lateSource = source;
        _lateScale = scale;
        _latePlacement = placement;

        var useCenter = settings.ImagePosition == ImagePosition.Center
            || settings.ImagePosition == ImagePosition.ActiveMonitorCenter;

        var stretch = settings.FitMode switch
        {
            FitMode.Stretch => Stretch.Fill,
            FitMode.Natural => Stretch.None,
            FitMode.Contain => Stretch.Uniform,
            FitMode.FillScreen => Stretch.UniformToFill,
            FitMode.ActualSize => Stretch.None,
            FitMode.CustomSize => settings.LockAspectRatio ? Stretch.Uniform : Stretch.Fill,
            _ => Stretch.Uniform
        };
        JosephImage.Stretch = stretch;
        JosephImage.RenderTransformOrigin = new Point(0.5, 0.5);

        if (useCenter)
        {
            // Centered composition: let the layout system center the panel + image.
            JosephPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            JosephPanel.VerticalAlignment = VerticalAlignment.Stretch;
            JosephPanel.Margin = new Thickness(0);
            JosephPanel.Width = double.NaN;
            JosephPanel.Height = double.NaN;
            JosephPanel.ClipToBounds = true;

            if (settings.FitMode == FitMode.ActualSize)
            {
                JosephImage.Width = source.PixelWidth;
                JosephImage.Height = source.PixelHeight;
            }
            else if (settings.FitMode == FitMode.CustomSize)
            {
                var customScale = Math.Clamp(settings.CustomFitScale, 0.25, 3.0);
                JosephImage.Width = source.PixelWidth * customScale;
                JosephImage.Height = source.PixelHeight * customScale;
            }
            else if (settings.FitMode == FitMode.Natural)
            {
                // Natural must never get clipped by the screen edge. Keep the true
                // pixel size, but shrink it to the monitor whenever it would overflow,
                // so the overlay always "fits" the whole screen.
                double pw = source.PixelWidth;
                double ph = source.PixelHeight;
                var maxW = ActualWidth > 0 ? ActualWidth : 1600;
                var maxH = ActualHeight > 0 ? ActualHeight : 900;
                if (pw > maxW || ph > maxH)
                {
                    var fitScale = Math.Min(maxW / Math.Max(1, pw), maxH / Math.Max(1, ph));
                    pw *= fitScale;
                    ph *= fitScale;
                }
                JosephImage.Width = pw;
                JosephImage.Height = ph;
            }
            else
            {
                // FillScreen / Contain / Stretch: let Stretch fill the panel.
                JosephImage.Width = double.NaN;
                JosephImage.Height = double.NaN;
            }
            JosephImage.HorizontalAlignment = HorizontalAlignment.Center;
            JosephImage.VerticalAlignment = VerticalAlignment.Center;
        }
        else if (placement.HasValue && placement.Value.Width > 0 && placement.Value.Height > 0)
        {
            // Non-center positions: use the computed placement rect.
            JosephPanel.HorizontalAlignment = HorizontalAlignment.Left;
            JosephPanel.VerticalAlignment = VerticalAlignment.Top;
            JosephPanel.Margin = new Thickness(placement.Value.X, placement.Value.Y, 0, 0);
            JosephPanel.Width = placement.Value.Width;
            JosephPanel.Height = placement.Value.Height;
            JosephPanel.ClipToBounds = false;

            JosephImage.Width = placement.Value.Width;
            JosephImage.Height = placement.Value.Height;
            JosephImage.HorizontalAlignment = HorizontalAlignment.Center;
            JosephImage.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            scale = settings.FitMode == FitMode.CustomSize
                ? Math.Clamp(settings.CustomFitScale, 0.25, 3.0)
                : scale;

            var pw = source.PixelWidth * scale;
            var ph = source.PixelHeight * scale;

            if (!settings.LockAspectRatio)
            {
                pw = source.PixelWidth * scale;
                ph = source.PixelHeight * scale;
            }

            var maxW = ActualWidth > 0 ? ActualWidth * 0.95 : 1600;
            var maxH = ActualHeight > 0 ? ActualHeight * 0.95 : 900;

            if (pw > maxW || ph > maxH)
            {
                var fitScale = Math.Min(maxW / Math.Max(1, pw), maxH / Math.Max(1, ph));
                if (settings.LockAspectRatio)
                {
                    pw *= fitScale;
                    ph *= fitScale;
                }
                else
                {
                    pw = Math.Min(pw, maxW);
                    ph = Math.Min(ph, maxH);
                }
            }

            JosephImage.Width = pw;
            JosephImage.Height = ph;
        }
    }

    private BitmapSource TrimTransparentBounds(BitmapSource source, string? cacheKey)
    {
        if (source is null) return source;

        if (cacheKey is not null && _trimKeyedCache.TryGetValue(cacheKey, out var hit))
            return hit;
        if (_trimCache.TryGetValue(source, out var cached))
            return cached;

        // Fast path: formats with no alpha channel are guaranteed fully opaque.
        if (NoAlphaFormats.Contains(source.Format))
        {
            return CacheTrim(cacheKey, source, source);
        }

        try
        {
            if (source.Format != PixelFormats.Bgra32)
            {
                source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            }

            var w = source.PixelWidth;
            var h = source.PixelHeight;
            if (w == 0 || h == 0) return CacheTrim(cacheKey, source, source);

            var stride = w * 4;
            var pixels = new byte[h * stride];
            source.CopyPixels(pixels, stride, 0);

            var minX = w;
            var maxX = -1;
            var minY = h;
            var maxY = -1;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * stride + x * 4;
                    var a = pixels[i + 3];
                    if (a > 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY) return CacheTrim(cacheKey, source, source);

            var newW = maxX - minX + 1;
            var newH = maxY - minY + 1;
            var newStride = newW * 4;
            var cropped = new byte[newH * newStride];

            for (var y = 0; y < newH; y++)
            {
                Buffer.BlockCopy(
                    pixels, (minY + y) * stride + minX * 4,
                    cropped, y * newStride,
                    newStride);
            }

            var result = BitmapSource.Create(newW, newH, 96, 96, PixelFormats.Bgra32, null, cropped, newStride);
            return CacheTrim(cacheKey, source, result);
        }
        catch
        {
            return source;
        }
    }

    private static BitmapSource CacheTrim(string? cacheKey, BitmapSource source, BitmapSource result)
    {
        if (!ReferenceEquals(source, result))
        {
            _trimCache[source] = result;
        }
        if (cacheKey is not null)
        {
            if (_trimKeyedCache.Count >= TrimKeyedCacheMaxEntries)
            {
                _trimKeyedCache.Clear();
            }
            _trimKeyedCache[cacheKey] = result;
        }
        return result;
    }

    private void ApplyTextPosition(AppSettings settings)
    {
        var margin = new Thickness(0);
        CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
        CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;

        // Horizontal alignment from settings.
        switch (settings.TextHorizontalAlignment)
        {
            case JosephExperience.Models.TextAlignment.Left:
                CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            case JosephExperience.Models.TextAlignment.Right:
                CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Right;
                break;
            default:
                CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
                break;
        }

        switch (settings.TextPosition)
        {
            case TextPosition.AboveImage:
                margin = new Thickness(0, 0, 0, 32);
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;
                CelebrationTextBlock.Margin = new Thickness(0, 0, 0, 48);
                break;
            case TextPosition.BelowImage:
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;
                CelebrationTextBlock.Margin = new Thickness(0, 48, 0, 0);
                break;
            case TextPosition.OverlayTop:
                margin = new Thickness(0, 24, 0, 0);
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Top;
                break;
            case TextPosition.OverlayCenter:
                margin = new Thickness(0);
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;
                break;
            case TextPosition.OverlayBottom:
                margin = new Thickness(0, 0, 0, 24);
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Bottom;
                break;
            case TextPosition.Left:
                margin = new Thickness(24, 0, 0, 0);
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;
                CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            case TextPosition.Right:
                margin = new Thickness(0, 0, 24, 0);
                CelebrationTextBlock.VerticalAlignment = VerticalAlignment.Center;
                CelebrationTextBlock.HorizontalAlignment = HorizontalAlignment.Right;
                break;
            case TextPosition.Hidden:
            default:
                CelebrationTextBlock.Visibility = Visibility.Collapsed;
                break;
        }
        CelebrationTextBlock.Margin = margin;
    }

    /// <summary>
    /// Normalizes quote text: collapses whitespace, removes accidental newlines/carriage returns
    /// so built-in quotes always render as a single line.
    /// </summary>
    private static string NormalizeQuoteText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        // Replace any whitespace sequence (including newlines/tabs) with a single space.
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Auto-fits the font size of a TextBlock so the text fits within MaxWidth on one line.
    /// The font is shrunk before wrapping occurs. Minimum ~22 DIP.
    /// </summary>
    private void AutoFitFontSize(AppSettings settings, TextBlock textBlock)
    {
        var maxTextWidth = textBlock.MaxWidth;
        if (maxTextWidth <= 0 || maxTextWidth == double.PositiveInfinity) return;

        var fontFamily = textBlock.FontFamily;
        var fontWeight = textBlock.FontWeight;
        var fontSize = textBlock.FontSize;
        var minFontSize = 22.0;

        if (fontSize < minFontSize) fontSize = minFontSize;

        while (fontSize > minFontSize)
        {
            var ft = new FormattedText(
                textBlock.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal),
                fontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (ft.Width <= maxTextWidth) break;

            fontSize = Math.Max(minFontSize, fontSize - 2.0);
        }

        textBlock.FontSize = Math.Max(minFontSize, fontSize);
    }

    private static AnimationStyle PickRandomAnimation()
    {
        var values = Enum.GetValues<AnimationStyle>();
        var candidates = values.Where(v => v != AnimationStyle.Random && v != AnimationStyle.None).ToArray();
        return candidates[_rng.Next(candidates.Length)];
    }

    private static IEasingFunction BuildEasing(EasingStyle style, EasingMode mode)
    {
        return style switch
        {
            EasingStyle.Linear => new LinearEaseProvider(),
            EasingStyle.EaseOut => new CubicEase { EasingMode = mode },
            EasingStyle.EaseIn => new QuadraticEase { EasingMode = mode },
            EasingStyle.EaseInOut => new QuadraticEase { EasingMode = mode },
            EasingStyle.Back => new BackEase { Amplitude = 0.4, EasingMode = mode },
            EasingStyle.Bounce => new BounceEase { Bounces = 3, Bounciness = 0.6, EasingMode = mode },
            EasingStyle.Elastic => new ElasticEase { Oscillations = 3, Springiness = 3, EasingMode = mode },
            _ => new CubicEase { EasingMode = mode }
        };
    }

    private void RunAnimation(AnimationStyle style, int durationMs, int entryMs, int exitMs, double maxOpacity, double scale, EasingStyle easingStyle, ExitStyle exitStyle)
    {
        _animationRunning = true;
        _activeExitStyle = exitStyle == ExitStyle.Random
            ? (ExitStyle)(1 + _rng.Next(3)) // NONE(0)/Fade(1)/Shrink(2)/Slide(3)
            : exitStyle;

        JosephImage.RenderTransformOrigin = new Point(0.5, 0.5);

        var baseScale = new ScaleTransform(scale, scale);
        var animScale = _animScale = new ScaleTransform(1, 1);
        var animTranslate = _animTranslate = new TranslateTransform(0, 0);
        var animRotate = _animRotate = new RotateTransform(0);

        var group = new TransformGroup();
        group.Children.Add(baseScale);
        group.Children.Add(animScale);
        group.Children.Add(animTranslate);
        group.Children.Add(animRotate);
        JosephImage.RenderTransform = group;

        IEasingFunction EaseOutFn() => BuildEasing(easingStyle, EasingMode.EaseOut);
        IEasingFunction EaseInFn() => BuildEasing(easingStyle, EasingMode.EaseIn);

        var easeOut = EaseOutFn();
        var easeIn = EaseInFn();
        var easeInOut = BuildEasing(easingStyle, EasingMode.EaseInOut);
        if (easeInOut is CubicEase ce) ce.EasingMode = EasingMode.EaseInOut;

        switch (style)
        {
            case AnimationStyle.None:
                JosephImage.Opacity = maxOpacity;
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Fade:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Pop:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.7, 1.0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.7, 1.0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.ScaleIn:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.1, 1.0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.1, 1.0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Bounce:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.YProperty, 80, 0, entryMs, new BounceEase { Bounces = 3, Bounciness = 0.6 });
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.SlideLeft:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.XProperty, -300, 0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.SlideRight:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.XProperty, 300, 0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.SlideUp:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.YProperty, 80, 0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.SlideDown:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.YProperty, -80, 0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.SpinIn:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animRotate, RotateTransform.AngleProperty, 360, 0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.3, 1.0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.3, 1.0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Jumpscare:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, Math.Min(80, entryMs), easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.2, 1.15, Math.Min(200, entryMs), easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.2, 1.15, Math.Min(200, entryMs), easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - Math.Min(200, entryMs) - exitMs)), exitMs);
                break;

            case AnimationStyle.Pulse:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                var pulse1 = new DoubleAnimation(1.0, 1.08, TimeSpan.FromMilliseconds(entryMs * 0.4))
                { EasingFunction = easeInOut, AutoReverse = true };
                animScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse1);
                var pulse1Y = new DoubleAnimation(1.0, 1.08, TimeSpan.FromMilliseconds(entryMs * 0.4))
                { EasingFunction = easeInOut, AutoReverse = true };
                animScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse1Y);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.ZoomOut:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 2.0, 1.0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 2.0, 1.0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.DropIn:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.YProperty, -200, 0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 1.4, 1.0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 1.4, 1.0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.SpinOut:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animRotate, RotateTransform.AngleProperty, -360, 0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 1.6, 1.0, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 1.6, 1.0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.RiseIn:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animTranslate, TranslateTransform.YProperty, 200, 0, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.ZoomIn:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.1, 1.2, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.1, 1.2, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Wobble:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                var wob = new DoubleAnimation(-12, 12, TimeSpan.FromMilliseconds(entryMs))
                { EasingFunction = easeInOut, AutoReverse = true };
                animRotate.BeginAnimation(RotateTransform.AngleProperty, wob);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Shake:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                var sh = new DoubleAnimation(-18, 18, TimeSpan.FromMilliseconds(entryMs))
                { EasingFunction = easeInOut, AutoReverse = true };
                animTranslate.BeginAnimation(TranslateTransform.XProperty, sh);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Elastic:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.5, 1.0, entryMs, new ElasticEase { Oscillations = 3, Springiness = 3 });
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.5, 1.0, entryMs, new ElasticEase { Oscillations = 3, Springiness = 3 });
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            case AnimationStyle.Overshoot:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                AnimateDouble(animScale, ScaleTransform.ScaleXProperty, 0.4, 1.12, entryMs, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
                AnimateDouble(animScale, ScaleTransform.ScaleYProperty, 0.4, 1.12, entryMs, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;

            default:
                AnimateDouble(JosephImage, OpacityProperty, 0, maxOpacity, entryMs, easeOut);
                StartHoldTimer(TimeSpan.FromMilliseconds(Math.Max(100, durationMs - entryMs - exitMs)), exitMs);
                break;
        }
    }

    private static void AnimateDouble(DependencyObject target, DependencyProperty property,
        double from, double to, int ms, IEasingFunction? easing)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
        if (easing != null) anim.EasingFunction = easing;
        TryBeginAnimation(target, property, anim);
    }

    /// <summary>
    /// Starts an animation on a target that is either a WPF UI element (e.g. the
    /// JosephImage, which derives from FrameworkElement/UIElement) or a
    /// System.Windows.Media type such as ScaleTransform/TranslateTransform.
    /// UI elements are NOT Animatable, so they must be animated via UIElement.BeginAnimation.
    /// </summary>
    private static void TryBeginAnimation(DependencyObject target, DependencyProperty property, DoubleAnimation animation)
    {
        switch (target)
        {
            case System.Windows.Media.Animation.Animatable animatable:
                animatable.BeginAnimation(property, animation);
                break;
            case UIElement uiElement:
                uiElement.BeginAnimation(property, animation);
                break;
        }
    }

    private void StartHoldTimer(TimeSpan hold, int exitMs)
    {
        if (hold <= TimeSpan.Zero)
        {
            FadeOut(exitMs);
            return;
        }
        StopHoldTimer();
        _holdTimer = new DispatcherTimer { Interval = hold };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            FadeOut(exitMs);
        };
        _holdTimer.Start();
    }

    private void FadeOut(int exitMs)
    {
        if (!_animationRunning) return;

        // Exit animations use the user's selected easing (EaseIn phase).
        var exitEase = BuildEasing(_easingStyle, EasingMode.EaseIn);

        if (_activeExitStyle == ExitStyle.None)
        {
            Complete();
            return;
        }

        // Text always fades out with the celebration.
        CelebrationTextBlock.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(exitMs)) { EasingFunction = exitEase });

        // The image always fades; exit styles add the matching motion on top.
        AnimateToCompletion(JosephImage, OpacityProperty, 0, exitMs, exitEase);

        switch (_activeExitStyle)
        {
            case ExitStyle.Shrink:
                AnimateToCompletion(_animScale, ScaleTransform.ScaleXProperty, 0.1, exitMs, exitEase);
                AnimateToCompletion(_animScale, ScaleTransform.ScaleYProperty, 0.1, exitMs, exitEase);
                break;
            case ExitStyle.Slide:
                AnimateFromCurrent(_animTranslate, TranslateTransform.YProperty, 60, exitMs, exitEase);
                break;
            case ExitStyle.ReverseEntry:
                AnimateReverseEntry(exitMs, exitEase);
                break;
            default:
            case ExitStyle.Fade:
                break; // opacity fade already started above
        }
    }

    /// <summary>
    /// True reverse of the resolved entry style: slides return, scales invert,
    /// spins unwind. Computed from _entryStyle, never hard-coded to Fade.
    /// </summary>
    private void AnimateReverseEntry(int exitMs, IEasingFunction ease)
    {
        switch (_entryStyle)
        {
            case AnimationStyle.SlideLeft:
                AnimateFromCurrent(_animTranslate, TranslateTransform.XProperty, 300, exitMs, ease);
                break;
            case AnimationStyle.SlideRight:
                AnimateFromCurrent(_animTranslate, TranslateTransform.XProperty, -300, exitMs, ease);
                break;
            case AnimationStyle.SlideUp:
            case AnimationStyle.RiseIn:
            case AnimationStyle.Bounce:
                AnimateFromCurrent(_animTranslate, TranslateTransform.YProperty, 140, exitMs, ease);
                break;
            case AnimationStyle.SlideDown:
            case AnimationStyle.DropIn:
                AnimateFromCurrent(_animTranslate, TranslateTransform.YProperty, -140, exitMs, ease);
                break;
            case AnimationStyle.SpinIn:
            case AnimationStyle.SpinOut:
                AnimateToCompletion(_animRotate, RotateTransform.AngleProperty, 180, exitMs, ease);
                AnimateToCompletion(_animScale, ScaleTransform.ScaleXProperty, 0.3, exitMs, ease);
                AnimateToCompletion(_animScale, ScaleTransform.ScaleYProperty, 0.3, exitMs, ease);
                break;
            case AnimationStyle.ZoomIn:
                AnimateToCompletion(_animScale, ScaleTransform.ScaleXProperty, 0.2, exitMs, ease);
                AnimateToCompletion(_animScale, ScaleTransform.ScaleYProperty, 0.2, exitMs, ease);
                break;
            case AnimationStyle.ZoomOut:
            case AnimationStyle.Pop:
            case AnimationStyle.ScaleIn:
            case AnimationStyle.Elastic:
            case AnimationStyle.Overshoot:
            case AnimationStyle.Pulse:
                AnimateToCompletion(_animScale, ScaleTransform.ScaleXProperty, 1.6, exitMs, ease);
                AnimateToCompletion(_animScale, ScaleTransform.ScaleYProperty, 1.6, exitMs, ease);
                break;
            case AnimationStyle.Shake:
                AnimateFromCurrent(_animTranslate, TranslateTransform.XProperty, 48, exitMs, ease);
                break;
            case AnimationStyle.Wobble:
                AnimateToCompletion(_animRotate, RotateTransform.AngleProperty, 12, exitMs, ease);
                break;
            case AnimationStyle.Jumpscare:
            case AnimationStyle.Fade:
            case AnimationStyle.None:
            default:
                break; // quick fade/shrink of a dramatic entry: opacity fade suffices
        }
    }

    private void AnimateToCompletion(DependencyObject? target, DependencyProperty property, double to, int exitMs, IEasingFunction? easing = null)
    {
        if (target is null) return;
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(exitMs))
        {
            EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => Complete();
        TryBeginAnimation(target, property, anim);
    }

    private void AnimateFromCurrent(DependencyObject? target, DependencyProperty property, double offset, int exitMs, IEasingFunction? easing = null)
    {
        if (target is null) return;
        var current = (double)target.GetValue(property);
        var anim = new DoubleAnimation(current + offset, TimeSpan.FromMilliseconds(exitMs))
        {
            EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => Complete();
        TryBeginAnimation(target, property, anim);
    }

    private void StopHoldTimer()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
    }

    private void Complete()
    {
        if (!_animationRunning) return;
        _animationRunning = false;
        JosephImage.BeginAnimation(OpacityProperty, null);
        JosephImage.RenderTransform = null;
        CelebrationTextBlock.BeginAnimation(OpacityProperty, null);
        Hide();
        var callback = _onCompleted;
        _onCompleted = null;
        callback?.Invoke();
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            style |= NativeMethods.WS_EX_LAYERED;
            style |= NativeMethods.WS_EX_TRANSPARENT;
            style |= NativeMethods.WS_EX_NOACTIVATE;
            style |= NativeMethods.WS_EX_TOOLWINDOW;
            style |= NativeMethods.WS_EX_TOPMOST;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        }

        // Guarantee the overlay fills the monitor: once the window is actually
        // measured (or after any resize/DPI event), re-fit so it never sits on
        // the 1600x900 fallback or resizes into a partial/overflowing frame.
        RefitIfNeeded();
    }

    private void OverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefitIfNeeded();
    }

    /// <summary>
    /// Re-applies the image layout once the overlay is measured to its real size,
    /// and whenever its size changes (multi-monitor moves, DPI scaling shifts).
    /// This guarantees the overlay always fills the whole monitor and never ends
    /// up "just not fitting" — the image is re-fit to the actual window bounds.
    /// </summary>
    private void RefitIfNeeded()
    {
        if (!IsVisible || _lateSource is null || _lateSettings is null) return;
        if (ActualWidth <= 1 || ActualHeight <= 1) return;
        ApplySizeAndPosition(_lateSettings, _lateSource, _lateScale, _latePlacement);
    }

    public void ForceReapplySizeAndPosition()
    {
        if (_lateSource is null || _lateSettings is null) return;
        ApplySizeAndPosition(_lateSettings, _lateSource, _lateScale, _latePlacement);
    }

    public void ForceComplete()
    {
        StopHoldTimer();
        if (_animationRunning)
        {
            _animationRunning = false;
            JosephImage.BeginAnimation(OpacityProperty, null);
            JosephImage.RenderTransform = null;
            CelebrationTextBlock.BeginAnimation(OpacityProperty, null);
            Hide();
            var callback = _onCompleted;
            _onCompleted = null;
            callback?.Invoke();
        }
    }

    /// <summary>True while this overlay window is on screen.</summary>
    public bool IsOverlayVisible => IsVisible && _animationRunning;

    /// <summary>Immediately hides the overlay without completing the animation (priority preemption).</summary>
    public void ForceHide()
    {
        StopHoldTimer();
        _animationRunning = false;
        JosephImage.BeginAnimation(OpacityProperty, null);
        JosephImage.RenderTransform = null;
        CelebrationTextBlock.BeginAnimation(OpacityProperty, null);
        _onCompleted = null;
        Hide();
    }

    private static double ResolveFontSize(FontSizePreset preset, double custom) => preset switch
    {
        FontSizePreset.Small => 28,
        FontSizePreset.Medium => 40,
        FontSizePreset.Large => 56,
        FontSizePreset.Huge => 72,
        FontSizePreset.Absurd => 96,
        _ => custom
    };

    private static System.Windows.Media.Effects.Effect? BuildTextEffect(AppSettings settings)
    {
        if (settings.TextOutline != Models.TextOutline.None)
        {
            return settings.TextOutline switch
            {
                Models.TextOutline.Thin => new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 0.5, ShadowDepth = 0.3, Opacity = 1.0 },
                Models.TextOutline.Medium => new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 1.2, ShadowDepth = 0.6, Opacity = 1.0 },
                Models.TextOutline.Thick => new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 2.5, ShadowDepth = 1.2, Opacity = 1.0 },
                _ => null
            };
        }

        return settings.TextShadow switch
        {
            Models.TextShadowStyle.SoftShadow => new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.5 },
            Models.TextShadowStyle.StrongShadow => new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Opacity = 0.8 },
            _ => null
        };
    }

    private void ApplyTextFx(Models.TextFxStyle fx, Models.FxIntensity intensity, int durationMs, double textScale)
    {
        var amp = IntensityMul(intensity);
        var text = CelebrationTextBlock;
        if (text is null || text.Visibility != Visibility.Visible) return;
        switch (fx)
        {
            case Models.TextFxStyle.Impact:
                var impactEntry = (int)(150 * amp);
                var impactScale = 1.8 * amp;
                text.RenderTransformOrigin = new Point(0.5, 0.5);
                var impactGroup = new TransformGroup();
                var impactBase = new ScaleTransform(textScale, textScale);
                var impactAnim = new ScaleTransform(1, 1);
                impactGroup.Children.Add(impactBase);
                impactGroup.Children.Add(impactAnim);
                text.RenderTransform = impactGroup;
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(impactEntry)));
                AnimateDouble(impactAnim, ScaleTransform.ScaleXProperty, impactScale, 1.0, impactEntry, new CubicEase { EasingMode = EasingMode.EaseOut });
                AnimateDouble(impactAnim, ScaleTransform.ScaleYProperty, impactScale, 1.0, impactEntry, new CubicEase { EasingMode = EasingMode.EaseOut });
                break;
            case Models.TextFxStyle.Pop:
                text.RenderTransformOrigin = new Point(0.5, 0.5);
                var popGroup = new TransformGroup();
                var popBase = new ScaleTransform(textScale, textScale);
                var popAnim = new ScaleTransform(0.5, 0.5);
                popGroup.Children.Add(popBase);
                popGroup.Children.Add(popAnim);
                text.RenderTransform = popGroup;
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds((int)(180 * amp))));
                AnimateDouble(popAnim, ScaleTransform.ScaleXProperty, 0.5, 1.0, (int)(200 * amp), new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut });
                AnimateDouble(popAnim, ScaleTransform.ScaleYProperty, 0.5, 1.0, (int)(200 * amp), new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut });
                break;
            case Models.TextFxStyle.Bounce:
                text.RenderTransformOrigin = new Point(0.5, 0.5);
                var bounceBase = new ScaleTransform(textScale, textScale);
                var bounceTrans = new TranslateTransform(0, 0);
                var bounceGroup = new TransformGroup();
                bounceGroup.Children.Add(bounceBase);
                bounceGroup.Children.Add(bounceTrans);
                text.RenderTransform = bounceGroup;
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds((int)(120 * amp))));
                AnimateDouble(bounceTrans, TranslateTransform.YProperty, 60 * amp, 0, (int)(400 * amp), new BounceEase { Bounces = 3, Bounciness = 0.6 });
                break;
            case Models.TextFxStyle.Shake:
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds((int)(100 * amp))));
                var shk = new DoubleAnimation(-14 * amp, 14 * amp, TimeSpan.FromMilliseconds((int)(300 * amp)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, AutoReverse = true };
                var shkTrans = new TranslateTransform();
                text.RenderTransform = shkTrans;
                shkTrans.BeginAnimation(TranslateTransform.XProperty, shk);
                break;
            case Models.TextFxStyle.Typewriter:
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(60)));
                var full = text.Text ?? "";
                text.Text = "";
                var idx = 0;
                var twTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(25, 80 - (int)amp * 15)) };
                twTimer.Tick += (_, _) =>
                {
                    if (idx < full.Length) { text.Text += full[idx++]; }
                    else { twTimer.Stop(); }
                };
                twTimer.Start();
                break;
            case Models.TextFxStyle.Stamp:
                text.RenderTransformOrigin = new Point(0.5, 0.5);
                var stampBase = new ScaleTransform(textScale, textScale);
                var stampRot = new RotateTransform(0);
                var stampScale = new ScaleTransform(1, 1);
                var stampGroup = new TransformGroup();
                stampGroup.Children.Add(stampBase);
                stampGroup.Children.Add(stampScale);
                stampGroup.Children.Add(stampRot);
                text.RenderTransform = stampGroup;
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(50)));
                AnimateDouble(stampScale, ScaleTransform.ScaleXProperty, 1.5 * amp, 1.0, (int)(280 * amp), new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
                AnimateDouble(stampScale, ScaleTransform.ScaleYProperty, 1.5 * amp, 1.0, (int)(280 * amp), new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
                AnimateDouble(stampRot, RotateTransform.AngleProperty, -6 * amp, 0, (int)(280 * amp), new CubicEase { EasingMode = EasingMode.EaseOut });
                break;
            case Models.TextFxStyle.Fade:
            default:
                text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds((int)(350 * amp))));
                break;
        }
    }

    private static double IntensityMul(FxIntensity intensity) => intensity switch
    {
        FxIntensity.Subtle => 0.6,
        FxIntensity.Strong => 1.5,
        FxIntensity.Unhinged => 2.2,
        _ => 1.0
    };
}

/// <summary>Identity/timing-based easing that yields a true linear tween.</summary>
public sealed class LinearEaseProvider : EasingFunctionBase
{
    protected override Freezable CreateInstanceCore() => new LinearEaseProvider();

    protected override double EaseInCore(double normalizedTime) => normalizedTime;
}

