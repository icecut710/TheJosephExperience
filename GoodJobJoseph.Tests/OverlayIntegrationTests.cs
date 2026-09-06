using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Services;
using JosephExperience.Utilities;

namespace JosephExperience.Tests;

public class OverlayIntegrationTests
{
    [Fact]
    public void OverlayService_ShowsAndCompletes_OnStaThread()
    {
        var completed = false;
        var thread = new Thread(() =>
        {
            var done = new ManualResetEvent(false);
            var overlay = new OverlayService();
            var settings = new AppSettings
            {
                OverlayDurationMs = 600,
                ImageOpacity = 1.0,
                OverlayScale = 0.7,
                AnimationStyle = AnimationStyle.Pop,
                ImagePosition = ImagePosition.Center,
                MonitorMode = MonitorMode.Primary
            };

            Exception? error = null;
            overlay.ShowOverlay(null, settings, () =>
            {
                completed = true;
                done.Set();
            });

            // Pump the dispatcher until the overlay completes, or timeout.
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (!done.WaitOne(50) && DateTime.UtcNow < timeout)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }

            overlay.TriggerShutdownForTest();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
            Console.WriteLine($"completed={completed} error={error}");
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(8000), "Overlay ST thread timed out.");

        Assert.True(completed, "Overlay did not complete within timeout.");
    }

    [Fact]
    public void OverlayService_RapidReplaces_DoNotStack()
    {
        var completions = 0;
        var thread = new Thread(() =>
        {
            var done = new ManualResetEvent(false);
            var overlay = new OverlayService();
            var settings = new AppSettings
            {
                OverlayDurationMs = 600,
                ImageOpacity = 1.0,
                OverlayScale = 0.7,
                AnimationStyle = AnimationStyle.Fade,
                ImagePosition = ImagePosition.Center,
                MonitorMode = MonitorMode.Primary
            };

            // Trigger twice rapidly; the second should replace the first (single lifecycle).
            overlay.ShowOverlay(null, settings, () => Interlocked.Increment(ref completions));
            overlay.ShowOverlay(null, settings, () => Interlocked.Increment(ref completions));

            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref completions) < 2 && DateTime.UtcNow < timeout)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(40);
            }

            overlay.TriggerShutdownForTest();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
            done.Set();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(8000), "Rapid-replace ST thread timed out.");
        Assert.Equal(2, completions);
    }
}

