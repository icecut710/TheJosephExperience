using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace JosephExperience.Utilities;

/// <summary>
/// Attached behavior that replaces abrupt mouse-wheel line jumps with a short,
/// cubic-eased glide. Enable it via the implicit ScrollViewer style in App.xaml
/// so every page (current and future) gets smooth scrolling automatically.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
        {
            sv.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            sv.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller || scroller.ScrollableHeight <= 0) return;

        var state = GetState(scroller);
        var step = Math.Max(48.0, scroller.ViewportHeight * 0.06);
        var target = scroller.VerticalOffset - (e.Delta > 0 ? step : -step);
        target = Math.Max(0, Math.Min(target, scroller.ScrollableHeight));

        if (Math.Abs(target - scroller.VerticalOffset) < 0.5) return;

        state.Start = scroller.VerticalOffset;
        state.Target = target;
        state.Elapsed = 0;
        state.Timer.Stop();
        state.Timer.Start();
        e.Handled = true;
    }

    private static void Tick(ScrollViewer scroller, ScrollState state)
    {
        state.Elapsed += 11;
        var t = Math.Min(1.0, state.Elapsed / 160.0);
        var eased = 1 - Math.Pow(1 - t, 3);
        scroller.ScrollToVerticalOffset(state.Start + (state.Target - state.Start) * eased);
        if (t >= 1.0)
        {
            state.Timer.Stop();
        }
    }

    private sealed class ScrollState
    {
        public double Start;
        public double Target;
        public int Elapsed;
        public DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(11) };
    }

    // Keyed by ScrollViewer instance so per-scrollview animation state is isolated
    // with no leak (entries are collected when the ScrollViewer is).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, ScrollState> _states = new();

    private static ScrollState GetState(ScrollViewer sv)
    {
        if (_states.TryGetValue(sv, out var state))
        {
            return state;
        }
        state = new ScrollState();
        state.Timer.Tick += (_, _) => Tick(sv, state);
        _states.Add(sv, state);
        return state;
    }
}