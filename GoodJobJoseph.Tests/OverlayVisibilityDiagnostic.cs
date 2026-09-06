using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JosephExperience.Models;
using JosephExperience.Services;

namespace JosephExperience.Tests;

public class OverlayVisibilityDiagnostic
{
    private static Image? FindJosephImage(OverlayService overlay)
    {
        var overlayField = typeof(OverlayService)
            .GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var w = overlayField?.GetValue(overlay) as Window;
        if (w is null) return null;
        var imgField = w.GetType().GetField("JosephImage",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return imgField?.GetValue(w) as Image;
    }

    private static bool GetAnimationRunning(OverlayService overlay)
    {
        var overlayField = typeof(OverlayService)
            .GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var w = overlayField?.GetValue(overlay) as Window;
        if (w is null) return false;
        var animField = w.GetType().GetField("_animationRunning",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return animField?.GetValue(w) is bool b && b;
    }

    private static BitmapSource MakeImage(int size)
    {
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var buffer = new byte[size * size * 4];
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = 0xA0;
            buffer[i + 1] = 0x10;
            buffer[i + 2] = 0x10;
            buffer[i + 3] = 0xFF;
        }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), buffer, size * 4, 0);
        return bmp;
    }

    private static void EnsureApplication()
    {
        if (Application.Current == null)
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/JosephExperience;component/Styles/Colors.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/JosephExperience;component/Styles/Buttons.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/JosephExperience;component/Styles/Typography.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/JosephExperience;component/Styles/Inputs.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("pack://application:,,,/JosephExperience;component/Styles/Navigation.xaml", UriKind.Absolute) });
        }
    }

    /// <summary>
    /// Verifies that deploying a real Joseph image sets up the overlay correctly:
    /// the Image control receives the source, becomes visible, the animation is
    /// armed, and the overlay completes within the timeout.
    /// </summary>
    [Theory]
    [InlineData(AnimationStyle.Pop)]
    [InlineData(AnimationStyle.ZoomIn)]
    [InlineData(AnimationStyle.ZoomOut)]
    [InlineData(AnimationStyle.Jumpscare)]
    [InlineData(AnimationStyle.Fade)]
    [InlineData(AnimationStyle.Bounce)]
    public void Overlay_WithRealImage_BecomesVisible(AnimationStyle animation)
    {
        Exception? error = null;
        bool completed = false;
        bool imageWasVisible = false;
        bool animationArmed = false;

        var thread = new Thread(() =>
        {
            var done = new ManualResetEvent(false);
            try
            {
                EnsureApplication();

                var overlay = new OverlayService();
                var source = MakeImage(256);
                var settings = new AppSettings
                {
                    OverlayDurationMs = 3000,
                    ImageOpacity = 1.0,
                    OverlayScale = 0.7,
                    AnimationStyle = animation,
                    ImagePosition = ImagePosition.Center,
                    MonitorMode = MonitorMode.Primary,
                    LowDistraction = false
                };

                overlay.ShowOverlay(source, settings, () => { completed = true; done.Set(); });

                // Give the dispatcher time to process the Show() and animation start.
                var timeout = DateTime.UtcNow.AddSeconds(10);
                while (!done.WaitOne(25) && DateTime.UtcNow < timeout)
                {
                    // Process dispatcher messages at render priority so WPF
                    // animation clocks can advance.
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    Thread.Sleep(5);

                    var img = FindJosephImage(overlay);
                    if (img is not null)
                    {
                        if (img.Visibility == Visibility.Visible && img.Source is not null)
                        {
                            imageWasVisible = true;
                        }
                    }

                    // Check animation state while running (before completion)
                    if (GetAnimationRunning(overlay))
                    {
                        animationArmed = true;
                    }
                }

                overlay.TriggerShutdownForTest();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(15000);

        if (error is not null) throw new Exception("Overlay diagnostic threw for " + animation + ": " + error, error);

        Assert.True(completed, "Overlay did not complete within the timeout for " + animation + ".");
        Assert.True(imageWasVisible,
            "JosephImage was never visible (Visibility != Visible or Source was null) for " + animation +
            ". The overlay setup did not correctly assign the source and visibility.");
        Assert.True(animationArmed,
            "The overlay animation was never armed (_animationRunning stayed false) for " + animation +
            ". RunAnimation was not invoked or did not set the flag.");
    }
}
