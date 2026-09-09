using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using JosephExperience.Models;
using JosephExperience.Utilities;
using JosephExperience.Views;

namespace JosephExperience.Services;

public class OverlayService
{
    private readonly OverlayWindow? _overlay;
    private readonly ConcurrentDictionary<string, Rect> _transparentBoundsCache = new();

    /// <summary>True while an overlay window is on screen.</summary>
    public bool IsVisible => _overlay is not null && (_overlay.Dispatcher.CheckAccess()
        ? _overlay.IsOverlayVisible : _overlay.Dispatcher.Invoke(() => _overlay.IsOverlayVisible));

    /// <summary>Immediately hides the current overlay (priority preemption).</summary>
    public void HideOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.Dispatcher.CheckAccess()) _overlay.ForceHide();
        else _overlay.Dispatcher.Invoke(_overlay.ForceHide);
    }
    private readonly object _lock = new();
    private bool _showing;

    public OverlayService()
    {
        try
        {
            _overlay = new OverlayWindow();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to create OverlayWindow — overlays will be disabled.", ex);
            _overlay = null;
        }
    }

    public bool IsShowing
    {
        get
        {
            lock (_lock)
            {
                return _showing;
            }
        }
    }

    public void TriggerShutdownForTest()
    {
        try
        {
            _overlay?.ForceComplete();
            _overlay?.Close();
        }
        catch (Exception ex)
        {
            AppLog.Error("TriggerShutdownForTest failed.", ex);
        }
    }

    public void ShowOverlay(BitmapSource? imageSource, AppSettings settings, string? quoteText, CelebrationResolver.ResolvedResult? resolved, Action? onCompleted, string? cacheKey = null)
        => ShowOverlay(imageSource, settings, null, quoteText, resolved, onCompleted, cacheKey);

    public void ShowOverlay(BitmapSource? imageSource, AppSettings settings, Action? onCompleted, string? cacheKey = null)
    {
        lock (_lock)
        {
            if (_overlay is null)
            {
                AppLog.Warn("ShowOverlay called but no OverlayWindow is available (creation failed).");
                onCompleted?.Invoke();
                return;
            }

            try
            {
                var monitorRect = ResolveMonitorRect(settings.MonitorMode);

                if (_showing && _overlay.IsVisible)
                {
                    _overlay.ForceComplete();
                }

                var (winX, winY, winW, winH) = ComputePlacement(settings, monitorRect, imageSource);

                _overlay.Left = monitorRect.Left;
                _overlay.Top = monitorRect.Top;
                _overlay.Width = monitorRect.Width;
                _overlay.Height = monitorRect.Height;

                var relRect = new Rect(
                    Math.Max(0, winX - monitorRect.Left),
                    Math.Max(0, winY - monitorRect.Top),
                    winW, winH);

                _overlay.Show();

                _showing = true;
                _overlay.ShowOverlay(imageSource, settings, relRect, null, null, () =>
                {
                    lock (_lock)
                    {
                        _showing = false;
                    }
                    onCompleted?.Invoke();
                }, cacheKey);
            }
            catch (Exception ex)
            {
                var imgInfo = imageSource is not null
                    ? $"src={imageSource.PixelWidth}x{imageSource.PixelHeight} fmt={imageSource.Format}"
                    : "src=null";
                AppLog.Error($"ShowOverlay (2-arg) failed: cacheKey=\"{cacheKey}\", {imgInfo}, monitor={settings.MonitorMode}, position={settings.ImagePosition}, fitMode={settings.FitMode}", ex);
                _showing = false;
                _overlay.ForceHide();
                onCompleted?.Invoke();
            }
        }
    }

    public void ShowOverlay(BitmapSource? imageSource, AppSettings settings, Rect? placement, string? quoteText, CelebrationResolver.ResolvedResult? resolved, Action? onCompleted, string? cacheKey = null)
    {
        lock (_lock)
        {
            if (_overlay is null)
            {
                AppLog.Warn("ShowOverlay called but no OverlayWindow is available (creation failed).");
                onCompleted?.Invoke();
                return;
            }

            try
            {
                var monitorRect = ResolveMonitorRect(settings.MonitorMode);

                if (_showing && _overlay.IsVisible)
                {
                    _overlay.ForceComplete();
                }

                var (winX, winY, winW, winH) = ComputePlacement(settings, monitorRect, imageSource);

                _overlay.Left = monitorRect.Left;
                _overlay.Top = monitorRect.Top;
                _overlay.Width = monitorRect.Width;
                _overlay.Height = monitorRect.Height;

                var relRect = new Rect(
                    Math.Max(0, winX - monitorRect.Left),
                    Math.Max(0, winY - monitorRect.Top),
                    winW, winH);

                _overlay.Show();

                _showing = true;
                _overlay.ShowOverlay(imageSource, settings, relRect, quoteText, resolved, () =>
                {
                    lock (_lock)
                    {
                        _showing = false;
                    }
                    onCompleted?.Invoke();
                }, cacheKey);
            }
            catch (Exception ex)
            {
                var imgInfo = imageSource is not null
                    ? $"src={imageSource.PixelWidth}x{imageSource.PixelHeight} fmt={imageSource.Format}"
                    : "src=null";
                AppLog.Error($"ShowOverlay failed: quote=\"{quoteText}\", cacheKey=\"{cacheKey}\", {imgInfo}, monitor={settings.MonitorMode}, position={settings.ImagePosition}, fitMode={settings.FitMode}", ex);
                _showing = false;
                _overlay.ForceHide();
                onCompleted?.Invoke();
            }
        }
    }

    private static double SizePresetScale(SizePreset preset)
    {
        return preset switch
        {
            SizePreset.Tiny => 0.5,
            SizePreset.Small => 0.75,
            SizePreset.Medium => 1.0,
            SizePreset.Large => 1.25,
            SizePreset.Huge => 1.5,
            SizePreset.Massive => 2.0,
            _ => 1.0
        };
    }

    private static double SafeMarginFor(ImagePosition pos, SafeMarginPreset preset, double pixels)
    {
        if (pos == ImagePosition.Center || pos == ImagePosition.ActiveMonitorCenter) return 0;
        return preset switch
        {
            SafeMarginPreset.None => 0,
            SafeMarginPreset.Small => 12,
            SafeMarginPreset.Medium => 40,
            SafeMarginPreset.Large => 100,
            _ => pixels
        };
    }

    private (double X, double Y, double W, double H) ComputePlacement(
        AppSettings settings, Rect monitor, BitmapSource? image)
    {
        var mw = Math.Max(10, monitor.Width);
        var mh = Math.Max(10, monitor.Height);
        var mx = monitor.Left;
        var my = monitor.Top;

        var scale = settings.SizePreset == SizePreset.Custom
            ? settings.CustomScalePercent / 100.0
            : SizePresetScale(settings.SizePreset);
        scale = Math.Clamp(scale, 0.25, 3.0);

        // Cursor-offset: avoid rendering directly under the cursor.
        var cursorOffset = settings.ImagePosition == ImagePosition.CursorPosition ? 40 : 0;

        var safe = SafeMarginFor(settings.ImagePosition, settings.SafeMarginPreset, settings.SafeMarginPixels);
        if (settings.DebugOverlayBounds)
        {
            safe = 0;
        }

        var safeL = mx + safe;
        var safeT = my + safe;
        var safeW = Math.Max(1, mw - 2 * safe);
        var safeH = Math.Max(1, mh - 2 * safe);

        double imgW, imgH;

        if (settings.FitMode == FitMode.FillScreen)
        {
            // Cover mode: scale = MAX(viewportWidth/assetWidth, viewportHeight/assetHeight)
            var assetW = image is not null ? image.PixelWidth : 100;
            var assetH = image is not null ? image.PixelHeight : 100;
            var scaleX = safeW / assetW;
            var scaleY = safeH / assetH;
            var coverScale = Math.Max(scaleX, scaleY);
            imgW = assetW * coverScale;
            imgH = assetH * coverScale;
        }
        else if (settings.FitMode == FitMode.Contain)
        {
            var assetW = image is not null ? image.PixelWidth : 100;
            var assetH = image is not null ? image.PixelHeight : 100;
            var scaleX = safeW / assetW;
            var scaleY = safeH / assetH;
            var containScale = Math.Min(scaleX, scaleY);
            imgW = assetW * containScale;
            imgH = assetH * containScale;
        }
        else if (settings.FitMode == FitMode.Stretch)
        {
            imgW = safeW;
            imgH = safeH;
        }
        else if (settings.FitMode == FitMode.ActualSize)
        {
            var imgW2 = image is not null ? image.PixelWidth : mw * 0.5;
            var imgH2 = image is not null ? image.PixelHeight : mh * 0.5;
            var centerX = safeL + safeW / 2;
            var centerY = safeT + safeH / 2;
            var winX2 = centerX - imgW2 / 2;
            var winY2 = centerY - imgH2 / 2;
            return (winX2, winY2, imgW2, imgH2);
        }
        else if (settings.FitMode == FitMode.CustomSize)
        {
            var customScale = Math.Clamp(settings.CustomFitScale, 0.25, 3.0);
            imgW = (image is not null ? image.PixelWidth : 100) * customScale;
            imgH = (image is not null ? image.PixelHeight : 100) * customScale;
            var maxW = safeW;
            var maxH = safeH;
            if (imgW > maxW || imgH > maxH)
            {
                var fitScale = Math.Min(maxW / Math.Max(1, imgW), maxH / Math.Max(1, imgH));
                imgW *= fitScale;
                imgH *= fitScale;
            }
        }
        else // Natural
        {
            imgW = image is not null ? image.PixelWidth * scale : mw * 0.5;
            imgH = image is not null ? image.PixelHeight * scale : mh * 0.5;

            var maxFit = Math.Min(
                (mw - 2 * safe) / Math.Max(1, imgW),
                (mh - 2 * safe) / Math.Max(1, imgH));
            if (maxFit < 1.0)
            {
                imgW *= maxFit;
                imgH *= maxFit;
            }
        }

        double cx, cy;

        switch (settings.ImagePosition)
        {
            case ImagePosition.Custom:
                cx = safeL + safeW * Math.Clamp(settings.CustomPositionX, 0.0, 1.0);
                cy = safeT + safeH * Math.Clamp(settings.CustomPositionY, 0.0, 1.0);
                break;
            case ImagePosition.CursorPosition:
                NativeMethods.GetCursorPos(out var pt);
                cx = pt.X + cursorOffset;
                cy = pt.Y + cursorOffset;
                break;
            case ImagePosition.ActiveMonitorCenter:
                cx = safeL + safeW / 2;
                cy = safeT + safeH / 2;
                break;
            case ImagePosition.TopLeft:
                cx = safeL + imgW / 2;
                cy = safeT + imgH / 2;
                break;
            case ImagePosition.TopCenter:
                cx = safeL + safeW / 2;
                cy = safeT + imgH / 2;
                break;
            case ImagePosition.TopRight:
                cx = safeL + safeW - imgW / 2;
                cy = safeT + imgH / 2;
                break;
            case ImagePosition.CenterLeft:
                cx = safeL + imgW / 2;
                cy = safeT + safeH / 2;
                break;
            case ImagePosition.CenterRight:
                cx = safeL + safeW - imgW / 2;
                cy = safeT + safeH / 2;
                break;
            case ImagePosition.BottomLeft:
                cx = safeL + imgW / 2;
                cy = safeT + safeH - imgH / 2;
                break;
            case ImagePosition.BottomCenter:
                cx = safeL + safeW / 2;
                cy = safeT + safeH - imgH / 2;
                break;
            case ImagePosition.BottomRight:
                cx = safeL + safeW - imgW / 2;
                cy = safeT + safeH - imgH / 2;
                break;
            default:
                cx = safeL + safeW / 2;
                cy = safeT + safeH / 2;
                break;
        }

        var winXMin = mx;
        var winXMax = mx + mw - imgW;
        if (winXMax < winXMin) winXMax = winXMin;
        var winYMin = my;
        var winYMax = my + mh - imgH;
        if (winYMax < winYMin) winYMax = winYMin;
        var winX = Math.Clamp(cx - imgW / 2, winXMin, winXMax);
        var winY = Math.Clamp(cy - imgH / 2, winYMin, winYMax);

        return (winX, winY, imgW, imgH);
    }

    private Rect ResolveMonitorRect(MonitorMode mode)
    {
        IntPtr monitor;
        switch (mode)
        {
            case MonitorMode.MonitorUnderCursor:
                NativeMethods.GetCursorPos(out var pt);
                monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
                break;
            case MonitorMode.ActiveWindowMonitor:
                var hwnd = NativeMethods.GetForegroundWindow();
                monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                break;
            case MonitorMode.Monitor1:
            case MonitorMode.Monitor2:
            case MonitorMode.Monitor3:
                monitor = ResolveIndexMonitor(mode);
                break;
            case MonitorMode.Primary:
                // Resolve the REAL primary monitor so the overlay window spans the
                // full physical screen even when DPI scaling differs across monitors.
                monitor = NativeMethods.MonitorFromPoint(
                    new NativeMethods.POINT
                    {
                        X = (int)(SystemParameters.PrimaryScreenWidth / 2),
                        Y = (int)(SystemParameters.PrimaryScreenHeight / 2)
                    },
                    NativeMethods.MONITOR_DEFAULTTONEAREST);
                break;
            default:
                monitor = IntPtr.Zero;
                break;
        }

        if (monitor != IntPtr.Zero)
        {
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top);
            }
        }

        AppLog.Warn("Could not resolve monitor info — falling back to primary screen dimensions.");
        return new Rect(0, 0,
            SystemParameters.PrimaryScreenWidth > 0 ? SystemParameters.PrimaryScreenWidth : 1920,
            SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 1080);
    }

    private static IntPtr ResolveIndexMonitor(MonitorMode mode)
    {
        if (NativeMethods.EnumerateMonitors() is not { } monitors || monitors.Count == 0)
        {
            return IntPtr.Zero;
        }
        var idx = mode switch
        {
            MonitorMode.Monitor1 => 0,
            MonitorMode.Monitor2 => 1,
            MonitorMode.Monitor3 => 2,
            _ => 0
        };
        return idx < monitors.Count ? monitors[idx] : monitors[0];
    }
}