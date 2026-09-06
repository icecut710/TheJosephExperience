using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using JosephExperience;
using JosephExperience.Models;
using JosephExperience.Services;
using JosephExperience.Services.CounterStrike;
using JosephExperience.Models.CounterStrike;
using JosephExperience.Utilities;

namespace JosephExperience.Views;

public partial class MainWindow : Window
{
    private readonly App _app;
    private AppSettingsService Services => App.Services ?? throw new InvalidOperationException("Services not initialized.");

    // Library state
    private string _searchText = "";
    private string _filter = "All";

    // Settings sub-navigation
    private string _settingsSubPage = "General";
    private StackPanel? _settingsContentHost;
    private ScrollViewer? _settingsScroll;

    // Update check result for release notes access
    private UpdateCheckResultData? _lastUpdateCheckResult;

    // Transient toast (F8 feedback etc.)
    private readonly DispatcherTimer _toastHideTimer = new() { Interval = TimeSpan.FromMilliseconds(2000) };

    // Games page live state
    private string? _currentPageTag;
    private int _selectedGameEventIndex;
    private readonly DispatcherTimer _gamesFeedTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private StackPanel? _gamesFeedPanel;
    private string? _gamesFeedSignature;

    // Market page live state
    private Grid? _marketChartContainer;
    private Action? _naddChartHandler;

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;
        DataContext = this;

        _toastHideTimer.Tick += ToastHideTimer_Tick;

        SizeChanged += (_, _) => UpdateStatusBarVisibility();

        // Live CS2 status updates
        var cs2 = Services.GameIntegration?.Invoke();
        if (cs2 is not null)
        {
            cs2.StateChanged += _ => Dispatcher.Invoke(UpdateStatusBar);
        }

        ShowCelebrationView();
    }

    private void UpdateStatusBarVisibility()
    {
        if (StatusNaddBadge is not null)
            StatusNaddBadge.Visibility = ActualWidth < 720 ? Visibility.Collapsed : Visibility.Visible;
        if (StatusAudio is not null)
            StatusAudio.Visibility = ActualWidth < 640 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void UpdateNaddPrice()
    {
        Dispatcher.Invoke(() =>
        {
            var nadd = App.Services?.NaddService?.CurrentData;
            if (nadd is null)
            {
                StatusNaddPrice.Text = "NADD: ...";
                StatusNaddBadge.ToolTip = "NADD/SOL price loading...";
            }
            else
            {
                StatusNaddPrice.Text = "NADD: " + RealNaddService.FormatPrice(nadd.PriceUsd);
                var tip = $"NADD/SOL real market data\nPrice: {RealNaddService.FormatPrice(nadd.PriceUsd)}\n24h Change: {nadd.Change24hPercent:+0.00;-0.00;0.00}%\nLiquidity: ${nadd.LiquidityUsd:N0}\nVolume 24h: ${nadd.Volume24hUsd:N0}\nLast updated: {nadd.LastUpdated:HH:mm:ss} UTC";
                StatusNaddBadge.ToolTip = tip;
            }
        });
    }

    internal void RefreshState()
    {
        ShowCelebrationView();
        UpdateStatusBar();
    }

    internal void ShowHotkeyError(string message, Models.HotkeyBinding? binding = null)
    {
        SetStatus("HOTKEY UNAVAILABLE", new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x79)));
        MessageBox.Show(message, "The Joseph Experience 2.0",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal void ShowHotkeySuccess(string hotkey)
    {
        SetStatus("READY", new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)));
        UpdateStatusBar();
    }

    /// <summary>Live feedback for the F8 audio-toggle hotkey (visible in the status bar).</summary>
    internal void OnSoundToggleChanged(bool on)
    {
        SetStatus(on ? "SOUND ON" : "SOUND OFF",
            on ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A))
               : new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)));
        UpdateStatusBar();
        ShowToast(on ? "Sound ON" : "Sound OFF",
            on ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A))
               : new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)));
    }

    /// <summary>Transient toast in the bottom-right corner; auto-hides and fades out.</summary>
    internal void ShowToast(string message, Brush? foreground = null)
    {
        ToastText.Text = message;
        ToastText.Foreground = foreground ?? (Brush)FindResource("TextPrimaryBrush");
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(UIElement.OpacityProperty, null);
        Toast.Opacity = 1.0;
        _toastHideTimer.Stop();
        _toastHideTimer.Start();
    }

    private void ToastHideTimer_Tick(object? sender, EventArgs e)
    {
        _toastHideTimer.Stop();
        var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(120)
        };
        fade.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
        Toast.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // =================================================================
    // TITLE BAR
    // =================================================================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        try
        {
            DragMove();
        }
        catch
        {
            // ignore drag errors
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var settings = Services.Settings!.Current;
        if (settings.MinimizeToTray)
        {
            _app.HideMainWindowToTray();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // =================================================================
    // NAVIGATION
    // =================================================================

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
        {
            return;
        }
        var tag = btn.Tag?.ToString();
        if (tag == "Celebration")
        {
            ShowCelebrationView();
        }
        else if (tag == "Library")
        {
            ShowLibraryView();
        }
        else if (tag == "Games")
        {
            ShowGamesView();
        }
        else if (tag == "Settings")
        {
            ShowSettingsView();
        }
        else if (tag == "Market")
        {
            ShowMarketView();
        }
    }

    private void SetPrimaryNav(string tag)
    {
        _currentPageTag = tag;
        if (tag != "Games" && _gamesFeedTimer.IsEnabled) _gamesFeedTimer.Stop();
        if (tag != "Market" && _naddChartHandler is not null)
        {
            var naddSvc = Services.NaddService;
            if (naddSvc is not null) naddSvc.DataUpdated -= _naddChartHandler;
            _naddChartHandler = null;
            _marketChartContainer = null;
        }
        var active = TryFindResource("NavButtonActive") as Style ?? new Style(typeof(Button));
        var normal = TryFindResource("NavButton") as Style ?? new Style(typeof(Button));
        NavCelebrate.Style = tag == "Celebration" ? active : normal;
        NavLibrary.Style = tag == "Library" ? active : normal;
        NavGames.Style = tag == "Games" ? active : normal;
        NavSettings.Style = tag == "Settings" ? active : normal;
        NavMarket.Style = tag == "Market" ? active : normal;
    }

    private static FrameworkElement CreatePageHeader(string title, string? subtitle = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush")
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 13,
                Foreground = (Brush)Application.Current.FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 3, 0, 0)
            });
        }
        return panel;
    }

    private void CrossFade(FrameworkElement content)
    {
        ContentHost.Content = content;
        content.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        content.BeginAnimation(OpacityProperty, anim);
    }

    // =================================================================
    // STATUS BAR
    // =================================================================

    private void SetStatus(string text, Brush? dot = null)
    {
        StatusText.Text = text;
        if (dot is not null)
        {
            StatusDot.Fill = dot;
        }
    }

    private void UpdateStatusBar()
    {
        var settings = Services.Settings!.Current;
        var enabled = settings.Enabled;
        if (!enabled)
        {
            SetStatus("DISABLED", new SolidColorBrush(Color.FromRgb(0x66, 0x6B, 0x75)));
        }
        else
        {
                        SetStatus("READY", new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)));
        }

        // Cloud status dot (truthful): green=connected, blue=syncing,
        // amber=offline, red=error, gray=not configured.
        var supabase = Services.Supabase;
        Brush? cloudDot = null;
        if (supabase is null || !supabase.Config.IsConfigured)
        {
            cloudDot = (Brush)FindResource("TextMutedBrush");
        }
        else if (supabase.IsConnected)
        {
            cloudDot = new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A));
        }
        else if (supabase.State == SupabaseState.Syncing)
        {
            cloudDot = new SolidColorBrush(Color.FromRgb(0x72, 0x81, 0xFF));
        }
        else if (supabase.State == SupabaseState.Offline || supabase.State == SupabaseState.Error)
        {
            cloudDot = new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x79));
        }

        if (cloudDot is not null)
        {
            CloudDot.Fill = cloudDot;
        }

        StatusHotkey.Text = CurrentHotkeyText(settings);
        UpdateCoinDisplay();
        UpdateCs2Status();

        // Sidebar STATUS section — compact, canonical
        NavStatus.Text = "Ready";
        NavStatusCloud.Text = supabase is null || !supabase.Config.IsConfigured
            ? "Cloud: Not configured"
            : supabase.IsConnected
                ? "Cloud: Connected"
                : supabase.State == SupabaseState.Syncing
                    ? "Cloud: Syncing"
                    : "Cloud: Offline";
        NavStatusCs2.Text = StatusCs2.Text;
        NavStatusAudio.Text = settings.PlaySound ? "Audio: On" : "Audio: Off";
        NavStatusHotkey.Text = "Hotkeys: F2 · F8";
        StatusVersion.Text = "2.0.2";
        StatusAudio.Text = settings.PlaySound ? "Audio: On (Press F8 to mute)" : "Audio: Off (Press F8 to enable)";
    }

    private void UpdateCs2Status()
    {
        var settings = Services.Settings!.Current;
        var cs2 = Services.GameIntegration?.Invoke();
        var phase = cs2?.State.Phase ?? CounterStrikeConnectionPhase.Disabled;

        if (phase is CounterStrikeConnectionPhase.Disabled or CounterStrikeConnectionPhase.Error)
        {
            StatusCs2.Text = "CS2: Disconnected";
            StatusCs2.Foreground = (Brush)FindResource("TextMutedBrush");
        }
        else
        {
            var detail = cs2?.State.Detail ?? "";
            var canonical = cs2?.State.ToString() ?? "Disconnected";
            // Canonical one-line status derived from CounterStrikeConnectionState.
            StatusCs2.Text = $"CS2: {canonical}";
            StatusCs2.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }
    }

    private static string CurrentHotkeyText(AppSettings settings)
    {
        return HotkeyConverter.Resolve(settings).DisplayName;
    }

    private static string FormatTriggerType(string? triggerType)
    {
        if (string.IsNullOrEmpty(triggerType)) return "Manual";

        if (triggerType.Equals("hotkey", StringComparison.OrdinalIgnoreCase))
            return "Hotkey (F2)";
        if (triggerType.Equals("button", StringComparison.OrdinalIgnoreCase))
            return "Celebrate Button";
        if (triggerType.Equals("preview", StringComparison.OrdinalIgnoreCase))
            return "Preview";
        if (triggerType.Equals("menu", StringComparison.OrdinalIgnoreCase))
            return "Menu";
        if (triggerType.Equals("details", StringComparison.OrdinalIgnoreCase))
            return "Details View";
        if (triggerType.Equals("tray", StringComparison.OrdinalIgnoreCase))
            return "Tray Icon";
        if (triggerType.Equals("manual", StringComparison.OrdinalIgnoreCase))
            return "Manual";

        if (triggerType.StartsWith("CounterStrike:Test:", StringComparison.Ordinal))
        {
            var evt = triggerType.Substring("CounterStrike:Test:".Length);
            return $"Test: {FormatGameEventName(evt)}";
        }
        if (triggerType.StartsWith("CounterStrike:", StringComparison.Ordinal))
        {
            var evt = triggerType.Substring("CounterStrike:".Length);
            return $"Game: {FormatGameEventName(evt)}";
        }

        return "Manual";
    }

    private static string FormatGameEventName(string raw)
    {
        return raw switch
        {
            "Kill" => "Kill",
            "Headshot" => "Headshot",
            "DoubleKill" => "Double Kill",
            "TripleKill" => "Triple Kill",
            "QuadKill" => "Quad Kill",
            "Ace" => "Ace",
            "RoundWin" => "Round Win",
            "RoundLoss" => "Round Loss",
            "MatchWin" => "Match Win",
            "BombPlanted" => "Bomb Plant",
            "BombDefused" => "Bomb Defuse",
            "BombExploded" => "Bomb Explode",
            "Clutch" => "Clutch (1vN)",
            "Mvp" => "MVP",
            "Death" => "Death",
            "RoundStart" => "Round Start",
            "MatchStart" => "Match Start",
            "LowHealthSurvival" => "Low Health Survival",
            _ => raw
        };
    }

    private void UpdateCoinDisplay()
    {
        try
        {
            var stats = Services.Library?.GetStats();
            // Coins feature removed - always display 0
            StatusCoinCount.Text = "0";
            StatusJosephCoins.Text = "Coins: 0";
        }
        catch
        {
            StatusCoinCount.Text = "?";
            StatusJosephCoins.Text = "Coins: ?";
        }
    }

      // =================================================================
      // CELEBRATE VIEW
      // =================================================================

    private void ShowCelebrationView()
    {
        SetPrimaryNav("Celebration");
        UpdateStatusBar();

        var settings = Services.Settings!.Current;
        var stats = Services.Library!.GetStats();

        var page = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        page.Children.Add(CreatePageHeader("Celebrate", "Choose a Joseph and deploy the next celebration."));

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

        // Enabled status row
        var statusRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusLeft = new StackPanel();
        var statusTitleRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusTitleRow.Children.Add(new Ellipse
        {
            Width = 8, Height = 8,
            Fill = settings.Enabled ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)) : (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0)
        });
        statusTitleRow.Children.Add(new TextBlock
        {
            Text = "Celebrations Enabled",
            FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        statusLeft.Children.Add(statusTitleRow);
        statusLeft.Children.Add(new TextBlock
        {
            Text = $"{CurrentHotkeyText(settings)} to deploy Joseph",
            FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush"), Margin = new Thickness(15, 1, 0, 0)
        });
        Grid.SetColumn(statusLeft, 0);
        statusRow.Children.Add(statusLeft);

        var enabledToggle = new CheckBox
        {
            Style = (Style)FindResource("ToggleSwitch"),
            IsChecked = settings.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        enabledToggle.Checked += EnableToggle_Changed;
        enabledToggle.Unchecked += EnableToggle_Changed;
        Grid.SetColumn(enabledToggle, 1);
        statusRow.Children.Add(enabledToggle);
        panel.Children.Add(statusRow);

        // Joseph preview (compact)
        var previewFrame = new Border
        {
            Background = (Brush)FindResource("BgAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = 132, Height = 132,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 6)
        };
        var previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var previewLabel = new TextBlock
        {
            Text = "No Joseph available",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var eligible = Services.Library!.GetAllImages().Where(i => i.Enabled).ToList();
        if (eligible.Count > 0)
        {
            var img = eligible[new Random().Next(eligible.Count)];
            previewImage.Source = ImageLibraryService.LoadImageSource(img.FilePath) as BitmapSource;
            previewLabel.Text = img.DisplayName;
        }
        previewFrame.Child = previewImage;
        panel.Children.Add(previewFrame);
        panel.Children.Add(previewLabel);

        // Primary button
        var celebrateBtn = new Button
        {
            Content = "The Joseph Experience 2.0",
            Style = (Style)FindResource("PrimaryButton"),
            FontSize = 14,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 14)
        };
        celebrateBtn.Click += (_, _) => Services.Celebration!.Trigger("button");
        panel.Children.Add(celebrateBtn);

        // Quick controls 2x2
        panel.Children.Add(CreateQuickGrid(settings));

        // Deployed stat
        var deployedRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        deployedRow.Children.Add(new TextBlock { Text = "JOSEPHS DEPLOYED  ", FontSize = 10, Foreground = (Brush)FindResource("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        deployedRow.Children.Add(new TextBlock { Text = stats.TotalCelebrations.ToString("N0"), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush"), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(deployedRow);

// Statistics card
var statsCard = new Border
{
    Margin = new Thickness(0, 6, 0, 6),
    Background = (Brush)FindResource("ElevatedBrush"),
    CornerRadius = new CornerRadius(6)
};
var statsRow = new Grid();
statsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
statsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
var statsLeft = new StackPanel { Margin = new Thickness(10, 8, 0, 8) };
statsLeft.Children.Add(new TextBlock
{
    Text = "Total Josephs",
    FontSize = 9,
    Foreground = (Brush)FindResource("TextMutedBrush"),
    VerticalAlignment = VerticalAlignment.Center
});
statsLeft.Children.Add(new TextBlock
{
    Text = stats.ImageCount.ToString("N0"),
    FontSize = 18,
    FontWeight = FontWeights.Bold,
    Foreground = (Brush)FindResource("AccentBrush"),
    VerticalAlignment = VerticalAlignment.Center
});
statsLeft.Children.Add(new TextBlock
{
    Text = $"Enabled: {stats.EnabledImageCount}",
    FontSize = 9,
    Foreground = (Brush)FindResource("TextMutedBrush"),
    Margin = new Thickness(0, 4, 0, 0),
    VerticalAlignment = VerticalAlignment.Center
});
Grid.SetColumn(statsLeft, 0);
statsRow.Children.Add(statsLeft);

var statsRight = new StackPanel { Margin = new Thickness(0, 8, 10, 8), VerticalAlignment = VerticalAlignment.Center };
statsRight.Children.Add(new TextBlock
{
    Text = "Most Used",
    FontSize = 9,
    Foreground = (Brush)FindResource("TextMutedBrush"),
    VerticalAlignment = VerticalAlignment.Center
});
statsRight.Children.Add(new TextBlock
{
    Text = stats.MostCelebratedJoseph ?? "None yet",
    FontSize = 14,
    FontWeight = FontWeights.SemiBold,
    Foreground = (Brush)FindResource("AccentBrush"),
    VerticalAlignment = VerticalAlignment.Center,
    TextTrimming = TextTrimming.CharacterEllipsis,
    Margin = new Thickness(0, 4, 0, 0),
    ToolTip = stats.MostCelebratedJoseph ?? "None yet"
});
        if (stats.LastCelebration.HasValue)
        {
            statsRight.Children.Add(new TextBlock
            {
                Text = $"Last: {stats.LastCelebration.Value.ToString("MM/dd HH:mm")}",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        Grid.SetColumn(statsRight, 1);
        statsRow.Children.Add(statsRight);

        // NADD coins row
        var coinsRow = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        coinsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var coinsLeft = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        coinsLeft.Children.Add(new TextBlock
        {
            Text = "Joseph Coins",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        coinsLeft.Children.Add(new TextBlock
        {
            Text = "0",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(coinsLeft, 0);
        coinsRow.Children.Add(coinsLeft);

        var coinsRight = new StackPanel { Margin = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        coinsRight.Children.Add(new TextBlock
        {
            Text = "Sounds Played",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = System.Windows.TextAlignment.Right
        });
        coinsRight.Children.Add(new TextBlock
        {
            Text = stats.SoundsPlayed.ToString("N0"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = System.Windows.TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(coinsRight, 1);
        coinsRow.Children.Add(coinsRight);

        var statsPanel = new StackPanel();
        statsPanel.Children.Add(statsRow);
        statsPanel.Children.Add(coinsRow);
        statsCard.Child = statsPanel;
        panel.Children.Add(statsCard);

        page.Children.Add(panel);
        CrossFade(page);
    }

    private FrameworkElement CreateQuickGrid(AppSettings s)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        for (int i = 0; i < 2; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (int i = 0; i < 2; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });

        var hotkeyCtrl = CreateQuickField("HOTKEY", CreateHotkeyBubble(s), 0, 0);
        var selectedAnim = DisplayNames.GetFriendlyName(s.AnimationStyle);
        var animCtrl = CreateQuickField("ANIMATION", CreateSmallCombo(selectedAnim, DisplayNames.GetMembers<AnimationStyle>().Select(m => m.Friendly), v =>
        {
            var members = DisplayNames.GetMembers<AnimationStyle>();
            var idx = members.FindIndex(m => m.Friendly == v);
            if (idx >= 0)
            {
                s.AnimationStyle = Enum.Parse<AnimationStyle>(members[idx].Raw);
                Services.Settings!.Save();
            }
        }), 1, 0);
        var durCtrl = CreateQuickField("DURATION", CreateSmallCombo(s.OverlayDurationMs.ToString(), new[] { "1200", "1800", "2500", "3500", "5000" }, v =>
        {
            s.OverlayDurationMs = int.Parse(v);
            Services.Settings!.Save();
        }), 0, 2);
        var imgCtrl = CreateQuickField("IMAGE", CreateSmallCombo(s.ImageMode == ImageMode.Specific ? "Specific" : "Carousel", new[] { "Carousel", "Specific" }, v =>
        {
            s.ImageMode = v == "Specific" ? ImageMode.Specific : ImageMode.Random;
            Services.Settings!.Save();
        }), 1, 2);

        AddAt(grid, hotkeyCtrl, 0, 0);
        AddAt(grid, animCtrl, 1, 0);
        AddAt(grid, durCtrl, 0, 2);
        AddAt(grid, imgCtrl, 1, 2);
        return grid;
    }

    private static void AddAt(Grid grid, FrameworkElement el, int col, int row)
    {
        Grid.SetColumn(el, col);
        Grid.SetRow(el, row);
        grid.Children.Add(el);
    }

    private FrameworkElement CreateQuickField(string label, FrameworkElement control, int col, int row)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 9.5, Foreground = (Brush)FindResource("TextMutedBrush"), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(control);
        return stack;
    }

    private static FrameworkElement CreateSmallCombo(string selected, IEnumerable<string> items, Action<string> onChange)
    {
        var combo = new ComboBox { SelectedIndex = -1, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0) };
        var list = items.ToList();
        foreach (var it in list)
        {
            combo.Items.Add(it);
        }
        var idx = list.FindIndex(i => i == selected);
        if (idx >= 0)
        {
            combo.SelectedIndex = idx;
        }
        combo.SelectionChanged += (_, e) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < list.Count)
            {
                onChange(list[combo.SelectedIndex]);
            }
        };
        return combo;
    }

    private FrameworkElement CreateHotkeyBubble(AppSettings s, HotkeyAction action = HotkeyAction.Celebration)
    {
        var binding = s.GetBinding(action);
        var displayText = binding.IsEmpty
            ? HotkeyConverter.GetDefaultBinding(action).DisplayName
            : binding.DisplayName;

        var border = new Border
        {
            Background = (Brush)FindResource("AccentSoftBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 30,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0)
        };
        var text = new TextBlock
        {
            Text = displayText,
            Foreground = (Brush)FindResource("AccentBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Child = text;
        border.MouseLeftButtonDown += (_, _) => OpenHotkeyDialog(text, action, binding);
        return border;
    }

    private void EnableToggle_Changed(object sender, RoutedEventArgs e)
    {
        var settings = Services.Settings!.Current;
        settings.Enabled = ((CheckBox)sender).IsChecked == true;
        Services.Settings.Save();
        UpdateStatusBar();
    }

    // =================================================================
    // GAMES VIEW
    // =================================================================

/// <summary>UI model for one configurable game event on the Games page.</summary>
    private sealed class GameEventPresetEntry
    {
        public string Key = "";
        public GameEventType Type;
        public string Name = "";
        public string Description = "";
        public bool Verified;
        public Func<AppSettings, bool> EnabledGet = _ => false;
        public Action<AppSettings, bool> EnabledSet = (_, _) => { };
    }

    private static readonly IReadOnlyList<GameEventPresetEntry> GameEventPresets = new List<GameEventPresetEntry>
    {
        new()
        {
            Key = "Kill", Type = GameEventType.Kill, Name = "Kill", Verified = true,
            Description = "Any single kill by the local player, detected from the kills counter in GSI.",
            EnabledGet = s => s.GameEvent_OnKill, EnabledSet = (s, v) => s.GameEvent_OnKill = v
        },
        new()
        {
            Key = "MultiKill", Type = GameEventType.DoubleKill, Name = "Multi-Kill (Double \u00b7 Triple \u00b7 Quad)", Verified = true,
            Description = "Two or more kills inside the rolling window escalate through Double \u2192 Triple \u2192 Quad.",
            EnabledGet = s => s.GameEvent_OnMultiKill, EnabledSet = (s, v) => s.GameEvent_OnMultiKill = v
        },
        new()
        {
            Key = "Ace", Type = GameEventType.Ace, Name = "Ace (5+ kills)", Verified = true,
            Description = "Five kills in the rolling window during a single round.",
            EnabledGet = s => s.GameEvent_OnAce, EnabledSet = (s, v) => s.GameEvent_OnAce = v
        },
        new()
        {
            Key = "Death", Type = GameEventType.Death, Name = "Death", Verified = true,
            Description = "The local player's alive flag flips from true to false.",
            EnabledGet = s => s.GameEvent_OnDeath, EnabledSet = (s, v) => s.GameEvent_OnDeath = v
        },
        new()
        {
            Key = "RoundWin", Type = GameEventType.RoundWin, Name = "Round Win", Verified = true,
            Description = "The round ends with the win team matching the local team.",
            EnabledGet = s => s.GameEvent_OnRoundWin, EnabledSet = (s, v) => s.GameEvent_OnRoundWin = v
        },
        new()
        {
            Key = "Mvp", Type = GameEventType.Mvp, Name = "MVP", Verified = false,
            Description = "CS2 only sends match_stats.mvp some of the time, so this event can stay silent.",
            EnabledGet = s => s.GameEvent_OnMvpAceClutch, EnabledSet = (s, v) => s.GameEvent_OnMvpAceClutch = v
        },
        new()
        {
            Key = "MatchWin", Type = GameEventType.MatchWin, Name = "Match Win", Verified = true,
            Description = "The map phase becomes \u201cgameover\u201d after the final round.",
            EnabledGet = s => s.GameEvent_OnMatchWin, EnabledSet = (s, v) => s.GameEvent_OnMatchWin = v
        },
        new()
        {
            Key = "BombPlanted", Type = GameEventType.BombPlanted, Name = "Bomb Plant", Verified = true,
            Description = "The bomb state in the GSI payload becomes \u201cplanted\u201d.",
            EnabledGet = s => s.GameEvent_OnBombPlant, EnabledSet = (s, v) => s.GameEvent_OnBombPlant = v
        },
        new()
        {
            Key = "BombDefused", Type = GameEventType.BombDefused, Name = "Bomb Defuse", Verified = true,
            Description = "The bomb state becomes \u201cdefused\u201d.",
            EnabledGet = s => s.GameEvent_OnBombDefuse, EnabledSet = (s, v) => s.GameEvent_OnBombDefuse = v
        },
        new()
        {
            Key = "BombExploded", Type = GameEventType.BombExploded, Name = "Bomb Explode", Verified = true,
            Description = "The bomb state becomes \u201cexplode\u201d.",
            EnabledGet = s => s.GameEvent_OnBombExplode, EnabledSet = (s, v) => s.GameEvent_OnBombExplode = v
        }
    };

    private void ShowGamesView()
    {
        SetPrimaryNav("Games");
        UpdateStatusBar();

        var settings = Services.Settings!.Current;
        var cs2 = Services.GameIntegration?.Invoke();
        var phase = cs2?.State.Phase ?? CounterStrikeConnectionPhase.Disabled;
        var attached = cs2 is not null && _app.IsCs2Attached();

        var (statusText, statusFg, statusBg) = ComputeGameStatus(phase, settings);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        panel.Children.Add(CreatePageHeader("Games", "Counter-Strike 2 integration and event configuration."));

        // =====================================================================
        // STATUS & CONNECTION CARD
        // =====================================================================
        var statusCard = CreateCard(1);
        var statusContent = new StackPanel();

        var hero = new Grid { Margin = new Thickness(12, 10, 12, 8) };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cs2Active = phase is not CounterStrikeConnectionPhase.Disabled
            and not CounterStrikeConnectionPhase.Error
            and not CounterStrikeConnectionPhase.ConfigurationMissing
            and not CounterStrikeConnectionPhase.ConfigurationInvalid
            and not CounterStrikeConnectionPhase.PortConflict;

        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 4,
                Color = Color.FromRgb(0, 0, 0),
                Opacity = 0.1,
                ShadowDepth = 1
            },
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = cs2Active ? "\uE8FB" : "\uE937",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 22,
                Foreground = cs2Active ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(icon, 0);
        hero.Children.Add(icon);

        var heroText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heroText.Children.Add(new TextBlock
        {
            Text = "Counter-Strike 2",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        heroText.Children.Add(new TextBlock
        {
            Text = phase switch
            {
                CounterStrikeConnectionPhase.ReceivingGameState => "Live game state is flowing in.",
                CounterStrikeConnectionPhase.WaitingForGsi => "Listener active \u2014 waiting for CS2 to connect.",
                CounterStrikeConnectionPhase.Cs2Running => "CS2 is running but no game state yet.",
                CounterStrikeConnectionPhase.WaitingForCs2 => "Waiting for CS2 to start.",
                _ => "Set up the listener below to go live."
            },
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(heroText, 1);
        hero.Children.Add(heroText);

        statusContent.Children.Add(hero);
        statusContent.Children.Add(CreateStatusPill(statusText, statusFg, statusBg));

        if (!attached)
        {
            var warnSoft = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x17));
            var banner = new Border
            {
                Background = warnSoft,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(12, 4, 12, 6)
            };
            var bannerContent = new StackPanel();
            bannerContent.Children.Add(new TextBlock
            {
                Text = "Games are off until you activate.",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            });
            bannerContent.Children.Add(new TextBlock
            {
                Text = "Click \u201cActivate & Install CS2\u201d below to write the GSI config into your CS2 folder and start the listener. It stays off until you do.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            banner.Child = bannerContent;
            statusContent.Children.Add(banner);
        }

        // Visual separator under the hero/pill area
        var statusSep = new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderBrush"),
            Opacity = 0.2,
            Margin = new Thickness(12, 4, 12, 6)
        };
        statusContent.Children.Add(statusSep);

        // Stat tiles
        var tiles = new WrapPanel { Margin = new Thickness(12, 4, 12, 10) };
        var port = Math.Clamp(settings.GameIntegrationPort, 1, 65535);
        tiles.Children.Add(CreateStatTile("LISTENER", $"http://127.0.0.1:{port}", cs2Active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextMutedBrush")));
        var freshness = cs2?.Freshness ?? PayloadFreshness.NeverReceived;
        tiles.Children.Add(CreateStatTile("FRESHNESS", freshness switch
        {
            PayloadFreshness.Receiving => "Live \u00b7 now",
            PayloadFreshness.Idle => "Paused \u00b7 <3s",
            PayloadFreshness.Stale => "Stale \u00b7 >10s",
            _ => "No payload yet"
        }, freshness == PayloadFreshness.Receiving ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush")));
        var cs2Running = cs2 is not null && System.Diagnostics.Process.GetProcessesByName("cs2").Length > 0;
        tiles.Children.Add(CreateStatTile("CS2 PROCESS", cs2Running ? "Running" : "Not running", cs2Running ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("TextSecondaryBrush")));
        tiles.Children.Add(CreateStatTile("GSI CONFIG", attached ? "Installed" : "Not installed", attached ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush")));
        statusContent.Children.Add(tiles);

        statusContent.Children.Add(CreateToggleRow("Enable game integration", settings.GameIntegrationEnabled, v =>
        {
            settings.GameIntegrationEnabled = v;
            Services.Settings.Save();
            _app.StartGameIntegration();
            UpdateStatusBar();
            RefreshGamesView();
        }));

        statusContent.Children.Add(CreateComboRow("GSI Port", new[] { "3000", "3001", "3002", "3500", "4000", "8080" }, settings.GameIntegrationPort.ToString(), v =>
        {
            if (int.TryParse(v, out var parsed) && parsed >= 1 && parsed <= 65535 && parsed != settings.GameIntegrationPort)
            {
                settings.GameIntegrationPort = parsed;
                Services.Settings.Save();
                _app.AttachCs2();
                UpdateStatusBar();
                RefreshGamesView();
            }
        }));

        // Auth token (debounced so we don't rewrite the cfg per keystroke)
        var authGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        authGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        authGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        authGrid.Children.Add(CreateFieldLabel("Auth Token (optional)"));
        var authBox = new TextBox
        {
            Text = settings.GameIntegrationAuthToken ?? "",
            ToolTip = "Optional auth token. Leave blank for no authentication.",
            FontSize = 11.5
        };
        var tokenApply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        void ApplyToken()
        {
            tokenApply.Stop();
            settings.GameIntegrationAuthToken = authBox.Text;
            Services.Settings.Save();
            _app.AttachCs2();
        }
        tokenApply.Tick += (_, _) => ApplyToken();
        authBox.TextChanged += (_, _) =>
        {
            tokenApply.Stop();
            tokenApply.Start();
        };
        authBox.LostFocus += (_, _) => ApplyToken();
        Grid.SetColumn(authBox, 1);
        authGrid.Children.Add(authBox);
        statusContent.Children.Add(authGrid);

        statusContent.Children.Add(CreateButtonRow(attached ? "Repair GSI Configuration" : "Activate & Install CS2", () =>
        {
            var result = _app.AttachCs2();
            MessageBox.Show(
                result.Message,
                result.Success ? "Counter-Strike Integration" : "Configuration failed",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            RefreshGamesView();
        }, 12, 8));

        statusContent.Children.Add(CreateInfoRow("The GSI listener receives game events (kills, deaths, round wins, MVPs, aces) and triggers Joseph celebrations automatically. Changing the port or token re-writes the game's cfg and restarts the listener immediately."));
statusCard.Child = statusContent;

        // Subtle depth beneath the status card
        var cardShadow = new DropShadowEffect
        {
            BlurRadius = 8,
            Color = Color.FromRgb(0, 0, 0),
            Opacity = 0.15,
            ShadowDepth = 2
        };
        statusCard.Effect = cardShadow;

        panel.Children.Add(statusCard);

        // =====================================================================
        // EVENT CELEBRATIONS — per-event dropdown editor
        // =====================================================================
        var eventsCard = CreateCard(1);
        var eventsContent = new StackPanel();
        eventsContent.Children.Add(CreateSectionHeader("EVENT CELEBRATIONS"));

        if (!settings.GameIntegrationEnabled)
        {
            eventsContent.Children.Add(new TextBlock
            {
                Text = "Integration is off \u2014 enable it above for live events. You can still tune each event now; it all applies when you go live.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("WarningBrush"),
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        var eventPickerRow = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        eventPickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        eventPickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        eventPickerRow.Children.Add(CreateFieldLabel("Event"));
        var eventPicker = new ComboBox();
        foreach (var p in GameEventPresets)
        {
            eventPicker.Items.Add(p.Name);
        }
        eventPicker.SelectedIndex = Math.Clamp(_selectedGameEventIndex, 0, GameEventPresets.Count - 1);
        Grid.SetColumn(eventPicker, 1);
        eventPickerRow.Children.Add(eventPicker);
        eventsContent.Children.Add(eventPickerRow);

        var eventEditorHost = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        eventsContent.Children.Add(eventEditorHost);

        var defaultsCache = GameEventDefaults.Create();
        GameEventCelebrationConfig? currentCfg = null;

        void PersistGameConfigs()
        {
            if (currentCfg is null) return;
            settings.GameEventConfigs[GameEventPresets[_selectedGameEventIndex].Type.ToString()] = currentCfg;
            Services.Settings.Save();
            _app.RefreshGameEventConfigs();
        }

        void ReloadCurrentConfig()
        {
            var preset = GameEventPresets[_selectedGameEventIndex];
            var key = preset.Type.ToString();
            if (settings.GameEventConfigs.TryGetValue(key, out var existing))
            {
                currentCfg = existing;
            }
            else
            {
                var seed = defaultsCache.TryGetValue(preset.Type, out var d)
                    ? new GameEventCelebrationConfig
                    {
                        Source = d.Source,
                        TextSource = d.TextSource,
                        SpecificImageId = d.SpecificImageId,
                        SpecificCategory = d.SpecificCategory,
                        SpecificPreset = d.SpecificPreset,
                        CustomText = d.CustomText,
                        CooldownMs = d.CooldownMs,
                        Priority = d.Priority
                    }
                    : new GameEventCelebrationConfig();
                currentCfg = seed;
            }
        }

        void RenderEventEditor()
        {
            eventEditorHost.Children.Clear();
            var preset = GameEventPresets[_selectedGameEventIndex];
            if (currentCfg is null) return;

            eventEditorHost.Children.Add(CreateToggleRow("Celebrate this event", preset.EnabledGet(settings), v =>
            {
                preset.EnabledSet(settings, v);
                Services.Settings.Save();
                RefreshGamesView();
            }, 160));

            var sources = new[]
            {
                (GameEventCelebrationSource.DefaultCelebration, "Use Default Celebration"),
                (GameEventCelebrationSource.RandomJoseph, "Random Joseph"),
                (GameEventCelebrationSource.SpecificCategory, "From a Category\u2026"),
                (GameEventCelebrationSource.SpecificJoseph, "Specific Joseph\u2026"),
                (GameEventCelebrationSource.NoCelebration, "No Celebration")
            };
            var sourceDisplay = sources.First(x => x.Item1 == currentCfg.Source).Item2;
            eventEditorHost.Children.Add(CreateComboRow("Celebration", sources.Select(x => x.Item2), sourceDisplay, label =>
            {
                var idx = sources.ToList().FindIndex(x => x.Item2 == label);
                if (idx < 0) return;
                currentCfg.Source = sources[idx].Item1;
                PersistGameConfigs();
                RenderEventEditor();
            }));

            var allImages = Services.Library!.GetAllImages();

            if (currentCfg.Source == GameEventCelebrationSource.SpecificCategory)
            {
                var categories = allImages.Where(i => !string.IsNullOrWhiteSpace(i.Category))
                    .Select(i => i.Category!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (categories.Count == 0)
                {
                    eventEditorHost.Children.Add(CreateEditorNote("No categories yet \u2014 tag Josephs with a category in the Library, then pick one here."));
                }
                else
                {
                    var effective = !string.IsNullOrEmpty(currentCfg.SpecificCategory) &&
                                    categories.Contains(currentCfg.SpecificCategory, StringComparer.OrdinalIgnoreCase)
                        ? currentCfg.SpecificCategory
                        : categories[0];
                    if (currentCfg.SpecificCategory != effective)
                    {
                        currentCfg.SpecificCategory = effective;
                        PersistGameConfigs();
                    }
                    eventEditorHost.Children.Add(CreateComboRow("Category", categories, effective, v =>
                    {
                        currentCfg.SpecificCategory = v;
                        PersistGameConfigs();
                    }));
                }
            }
            else if (currentCfg.Source == GameEventCelebrationSource.SpecificJoseph)
            {
                var josephs = allImages.Where(i => i.Enabled)
                    .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (josephs.Count == 0)
                {
                    eventEditorHost.Children.Add(CreateEditorNote("No enabled Josephs yet \u2014 add one in the Library, then pick it here."));
                }
                else
                {
                    var names = josephs.Select(i => i.DisplayName).ToList();
                    var selectedName = josephs.FirstOrDefault(j => j.Id == currentCfg.SpecificImageId)?.DisplayName
                                       ?? names[0];
                    if (currentCfg.SpecificImageId != josephs[names.IndexOf(selectedName)].Id)
                    {
                        currentCfg.SpecificImageId = josephs[names.IndexOf(selectedName)].Id;
                        PersistGameConfigs();
                    }
                    eventEditorHost.Children.Add(CreateComboRow("Joseph", names, selectedName, v =>
                    {
                        var joe = josephs[names.IndexOf(v)];
                        currentCfg.SpecificImageId = joe.Id;
                        PersistGameConfigs();
                    }));
                }
            }

            var texts = new[]
            {
                (GameEventTextSource.GameEventQuote, "Game phrase (built-in)"),
                (GameEventTextSource.JosephAssignedQuote, "Joseph's assigned text"),
                (GameEventTextSource.RandomLoreQuote, "Random lore quote"),
                (GameEventTextSource.Custom, "Custom text\u2026"),
                (GameEventTextSource.None, "No text")
            };
            var textDisplay = texts.First(x => x.Item1 == currentCfg.TextSource).Item2;
            eventEditorHost.Children.Add(CreateComboRow("Text", texts.Select(x => x.Item2), textDisplay, label =>
            {
                var idx = texts.ToList().FindIndex(x => x.Item2 == label);
                if (idx < 0) return;
                currentCfg.TextSource = texts[idx].Item1;
                PersistGameConfigs();
                RenderEventEditor();
            }));

            if (currentCfg.TextSource == GameEventTextSource.Custom)
            {
                eventEditorHost.Children.Add(CreateCustomTextBox("Custom text", currentCfg.CustomText ?? "", v =>
                {
                    currentCfg.CustomText = v;
                    PersistGameConfigs();
                }));
            }

            var star = preset.Verified ? "\u2605 " : "";
            eventEditorHost.Children.Add(CreateEditorNote($"{star}{preset.Description}  \u00b7  Priority {currentCfg.Priority}  \u00b7  Cooldown {currentCfg.CooldownMs} ms"));

            var resetBtn = new Button
            {
                Content = "Reset this event to defaults",
                Style = (Style)FindResource("GhostButton"),
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0)
            };
            resetBtn.Click += (_, _) =>
            {
                settings.GameEventConfigs.Remove(preset.Type.ToString());
                Services.Settings.Save();
                _app.RefreshGameEventConfigs();
                RefreshGamesView();
            };
            eventEditorHost.Children.Add(resetBtn);
        }

        eventPicker.SelectionChanged += (_, _) =>
        {
            _selectedGameEventIndex = eventPicker.SelectedIndex;
            ReloadCurrentConfig();
            RenderEventEditor();
        };

        ReloadCurrentConfig();
        RenderEventEditor();

        eventsCard.Child = eventsContent;
        panel.Children.Add(eventsCard);

        // =====================================================================
        // GAME FEEL CARD
        // =====================================================================
        var feelCard = CreateCard(1);
        var feelContent = new StackPanel();
        feelContent.Children.Add(CreateSectionHeader("GAME FEEL"));
        feelContent.Children.Add(new TextBlock
        {
            Text = "Global tuning shared by every event. Cooldown takes effect instantly; window and behavior restart the listener.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        feelContent.Children.Add(CreateSliderRow("Cooldown", 200, 3000, settings.GameEventCooldownMs, v =>
        {
            settings.GameEventCooldownMs = (int)v;
            Services.Settings.Save();
        }, 160, 4, v => $"{v:0} ms"));
        feelContent.Children.Add(CreateSliderRow("Multi-kill window", 3, 8, settings.GameEventMultiKillWindowSeconds, v =>
        {
            settings.GameEventMultiKillWindowSeconds = (int)v;
            Services.Settings.Save();
            Services.GameIntegration?.Invoke()?.Restart();
        }, 160));

        var behaviors = new[]
        {
            (MultiKillBehavior.HighestOnly, "Highest only \u2014 1 \u2192 Double \u2192 Triple"),
            (MultiKillBehavior.Stack, "Stack \u2014 every kill fires"),
            (MultiKillBehavior.ReplaceCurrent, "Replace \u2014 tier replaces the base kill")
        };
        var behaviorLabel = behaviors.First(x => x.Item1 == settings.MultiKillBehaviorParsed).Item2;
        feelContent.Children.Add(CreateComboRow("Multi-kill", behaviors.Select(x => x.Item2), behaviorLabel, v =>
        {
            var behavior = behaviors.First(x => x.Item2 == v).Item1;
            settings.GameEventMultiKillBehavior = behavior.ToString();
            Services.Settings.Save();
            Services.GameIntegration?.Invoke()?.Restart();
        }));

        feelContent.Children.Add(CreateToggleRow("Short flash while playing", settings.GameEventsLowDistraction, v =>
        {
            settings.GameEventsLowDistraction = v;
            Services.Settings.Save();
        }, 160));
        feelContent.Children.Add(new TextBlock
        {
            Text = "Low-distraction mode uses a rapid flash so you can keep playing. The F2 manual celebration always uses the full effect.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 4, 0, 8)
        });

        feelCard.Child = feelContent;
        panel.Children.Add(feelCard);

        // =====================================================================
        // TEST THE PIPELINE CARD
        // =====================================================================
        var testCard = CreateCard(1);
        var testContent = new StackPanel();
        testContent.Children.Add(CreateSectionHeader("TEST THE PIPELINE"));
        testContent.Children.Add(new TextBlock
        {
            Text = "Fire a sample event through the real celebration pipeline \u2014 a quick way to verify your setup without touching CS2.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var testGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var c = 0; c < 3; c++) testGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var testRowIndex = 0;

        void AddTest(string label, GameEventType type)
        {
            var btn = new Button
            {
                Content = "Test " + label,
                Style = (Style)FindResource("GhostButton"),
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Height = 26,
                Margin = new Thickness(2, 2, 2, 2),
                ToolTip = $"Fires a TEST {label} through the real celebration pipeline"
            };
            btn.Click += (_, _) =>
            {
                var svc = Services.GameIntegration?.Invoke();
                if (svc is null) { MessageBox.Show("Game integration is not running.", "Test", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                svc.TriggerTestEvent(type);
            };
            var col = testRowIndex % 3;
            var row = testRowIndex / 3;
            Grid.SetColumn(btn, col);
            Grid.SetRow(btn, row);
            if (testGrid.RowDefinitions.Count <= row) testGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            testGrid.Children.Add(btn);
            testRowIndex++;
        }

        AddTest("Kill", GameEventType.Kill);
        AddTest("Double Kill", GameEventType.DoubleKill);
        AddTest("Triple Kill", GameEventType.TripleKill);
        AddTest("Ace", GameEventType.Ace);
        AddTest("Round Win", GameEventType.RoundWin);
        AddTest("Match Win", GameEventType.MatchWin);
        AddTest("Bomb Plant", GameEventType.BombPlanted);
        AddTest("Bomb Defuse", GameEventType.BombDefused);
        AddTest("Death", GameEventType.Death);

        testContent.Children.Add(testGrid);
        testCard.Child = testContent;
        panel.Children.Add(testCard);

        // =====================================================================
        // RECENT EVENTS — live diagnostics feed
        // =====================================================================
        var feedCard = CreateCard(1);
        var feedContent = new StackPanel();
        feedContent.Children.Add(CreateSectionHeader("RECENT EVENTS"));
        _gamesFeedPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        feedContent.Children.Add(_gamesFeedPanel);
        feedCard.Child = feedContent;
        panel.Children.Add(feedCard);

        _gamesFeedTimer.Stop();
        _gamesFeedTimer.Tick -= UpdateGamesFeed;
        _gamesFeedTimer.Tick += UpdateGamesFeed;
        _gamesFeedTimer.Start();
        _gamesFeedSignature = null;
        UpdateGamesFeed(null, EventArgs.Empty);

        // =====================================================================
        // HOW IT WORKS CARD
        // =====================================================================
        var infoCard = CreateCard(1);
        var infoContent = new StackPanel();
        infoContent.Children.Add(new TextBlock
        {
            Text = "How it works",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        infoContent.Children.Add(new TextBlock
        {
            Text = "CS2's Game-State Integration posts JSON to a localhost listener on the port above \u2014 no injection, no memory access, no hooks. State transitions are turned into celebrations through the normal Joseph pipeline.\n\nVerifiably reliable today: kills, multi-kills, aces, deaths, round wins, match wins, and bomb events. Headshot and Clutch triggers are NOT produced (GSI doesn't reliably expose that data), so they've been removed from this page. MVP depends on CS2 sending match_stats.mvp, which it doesn't always do.\n\nEvery event has its own Celebration and Text dropdowns on the Event Celebrations card, and every choice saves instantly.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            LineHeight = 16
        });
        infoCard.Child = infoContent;
        panel.Children.Add(infoCard);

        scroll.Content = panel;
        CrossFade(scroll);
    }

    /// <summary>
    /// Rebuilds the Games page while restoring the outer scroll offset so a toggle,
    /// port change, or repair doesn't yank the user back to the top.
    /// </summary>
    private void RefreshGamesView()
    {
        var offset = ContentScroll.VerticalOffset;
        ShowGamesView();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (offset <= ContentScroll.ScrollableHeight)
            {
                ContentScroll.ScrollToVerticalOffset(offset);
            }
        }));
    }

    /// <summary>Small stat tile used in the Games status hero.</summary>
    private FrameworkElement CreateStatTile(string label, string value, Brush accent)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("SecondarySurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 120,
            MaxWidth = 280
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextMutedBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        border.Child = stack;
        return border;
    }

    /// <summary>Muted helper note used inside the per-event editor.</summary>
    private FrameworkElement CreateEditorNote(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            LineHeight = 14,
            Margin = new Thickness(0, 8, 0, 2)
        };
    }

    /// <summary>Label + textbox row (used for per-event custom text).</summary>
    private FrameworkElement CreateCustomTextBox(string label, string text, Action<string> onChange)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(CreateFieldLabel(label));
        var box = new TextBox { Text = text };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            onChange(box.Text);
        };
        box.TextChanged += (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return grid;
    }

    /// <summary>Refreshes the Games "Recent Events" diagnostics panel on a timer.</summary>
    private void UpdateGamesFeed(object? sender, EventArgs e)
    {
        if (_currentPageTag != "Games" || _gamesFeedPanel is null) return;
        var cs2 = Services.GameIntegration?.Invoke();
        var history = cs2?.EventHistory ?? Array.Empty<string>();
        var tail = history.TakeLast(8).ToList();
        var signature = string.Join('\n', tail);
        if (signature == _gamesFeedSignature) return;
        _gamesFeedSignature = signature;

        _gamesFeedPanel.Children.Clear();
        if (tail.Count == 0)
        {
            _gamesFeedPanel.Children.Add(new TextBlock
            {
                Text = "No events yet \u2014 fire a Test button above or load into a match to see events land here.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextMutedBrush")
            });
            return;
        }

        foreach (var line in tail)
        {
            var isTest = line.Contains("[TEST]", StringComparison.OrdinalIgnoreCase);
            var row = new Border
            {
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 4, 2, 4)
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "\u25CF",
                FontSize = 8,
                Foreground = isTest ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SuccessBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
            row.Child = stack;
            _gamesFeedPanel.Children.Add(row);
        }
    }

    // =================================================================
    // LIBRARY VIEW
    // =================================================================

    private void ShowLibraryView()
    {
        var images = Services.Library!.GetAllImages();

        var page = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        page.Children.Add(CreatePageHeader("Library", "Browse, search, and organize your Joseph collection."));

        var root = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top row: buttons only (title is in page header)
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var uploadBtn = new Button
        {
            Content = "Upload to Cloud",
            Style = (Style)FindResource("GhostButton"),
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Height = 28,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Upload an image to the shared cloud library"
        };
        uploadBtn.Click += async (_, _) => await UploadToCloudAsync();
        Grid.SetColumn(uploadBtn, 1);
        topRow.Children.Add(uploadBtn);
        var audioBtn = new Button
        {
            Content = "Upload Audio",
            Style = (Style)FindResource("GhostButton"),
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Height = 28,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Import a WAV or MP3 sound clip for celebrations"
        };
        audioBtn.Click += (_, _) => ImportSoundClip();
        Grid.SetColumn(audioBtn, 2);
        topRow.Children.Add(audioBtn);
        var addBtn = new Button
        {
            Content = "+ Add",
            Style = (Style)FindResource("PrimaryButton"),
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Height = 28
        };
        addBtn.Click += (_, _) => ImportSingle();
        Grid.SetColumn(addBtn, 3);
        topRow.Children.Add(addBtn);
        Grid.SetRow(topRow, 0);
        root.Children.Add(topRow);

        // Search
        var searchBox = new TextBox
        {
            Text = _searchText,
            ToolTip = "Search Josephs..."
        };
        searchBox.TextChanged += (_, _) => { _searchText = searchBox.Text; RebuildLibraryGrid(root); };
        Grid.SetRow(searchBox, 2);
        root.Children.Add(searchBox);

        // Filter chips
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        AddChip(chips, "All");
        AddChip(chips, "Favorites");
        AddChip(chips, "Enabled");
        AddChip(chips, "Disabled");
        Grid.SetRow(chips, 4);
        root.Children.Add(chips);

        // Grid area
        var gridHost = new Grid();
        Grid.SetRow(gridHost, 5);
        root.Children.Add(gridHost);

        _libraryRoot = root;
        _libraryGridHost = gridHost;
        RebuildLibraryGrid(root);

        page.Children.Add(root);
        CrossFade(page);
    }

    private async Task UploadToCloudAsync()
    {
        var supabase = Services.Supabase;
        if (supabase is null || !supabase.Config.IsConfigured)
        {
            MessageBox.Show(this,
                "Supabase is not configured. Add a .env file next to the app (see .env.example) to enable cloud upload.",
                "Upload to Cloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Upload to Cloud",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        SetStatus("Uploading to cloud...", new SolidColorBrush(Color.FromRgb(0x72, 0x81, 0xFF)));
        var result = await supabase.UploadFileAsync(dlg.FileName).ConfigureAwait(true);
        if (result.Success)
        {
            SetStatus("Uploaded to cloud", new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)));
            MessageBox.Show(this, "Image uploaded to the cloud library.", "Upload complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
            ShowLibraryView();
        }
        else
        {
            SetStatus("Upload failed", new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x79)));
            var detail = string.IsNullOrWhiteSpace(result.Error)
                ? "The image could not be uploaded. Please try again."
                : result.Error;
            MessageBox.Show(this, detail, "Upload failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Grid? _libraryRoot;
    private Grid? _libraryGridHost;
    private readonly List<Button> _chips = new();

    private void AddChip(StackPanel host, string name)
    {
        var chip = new Button
        {
            Content = name,
            Style = (Style)FindResource(name == _filter ? "FilterChipActive" : "FilterChip"),
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(11, 4, 11, 4)
        };
        chip.Click += (_, _) =>
        {
            _filter = name;
            foreach (var c in _chips)
            {
                c.Style = (Style)FindResource(c.Tag?.ToString() == name ? "FilterChipActive" : "FilterChip");
            }
            if (_libraryRoot is not null)
            {
                RebuildLibraryGrid(_libraryRoot);
            }
        };
        chip.Tag = name;
        _chips.Add(chip);
        host.Children.Add(chip);
    }

    private void RebuildLibraryGrid(Grid root)
    {
        if (_libraryGridHost is null)
        {
            return;
        }
        var all = Services.Library!.GetAllImages();

        IEnumerable<CelebrationImage> filtered = all;
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            filtered = filtered.Where(i =>
                (i.DisplayName ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                (i.Tags ?? "").Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }
        filtered = _filter switch
        {
            "Favorites" => filtered.Where(i => i.Favorite),
            "Enabled" => filtered.Where(i => i.Enabled),
            "Disabled" => filtered.Where(i => !i.Enabled),
            _ => filtered
        };
        var list = filtered.ToList();

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        
        var host = new StackPanel();

        if (list.Count == 0)
        {
            var empty = new Border
            {
                Background = (Brush)FindResource("BgAltBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 10, 0, 0)
            };
            var es = new StackPanel();
            es.Children.Add(new TextBlock
            {
                Text = all.Count == 0 ? "No Josephs." : "Nothing matches.",
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4)
            });
            es.Children.Add(new TextBlock
            {
                Text = all.Count == 0 ? "The deployment reserves are depleted." : "Try a different search or filter.",
                FontSize = 11, Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12)
            });
            var importBtn = new Button
            {
                Content = "Import Joseph",
                Style = (Style)FindResource("PrimaryButton"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(16, 6, 16, 6)
            };
            importBtn.Click += (_, _) => ImportSingle();
            es.Children.Add(importBtn);
            empty.Child = es;
            host.Children.Add(empty);
        }
        else
        {
            var wrap = new WrapPanel();
            foreach (var img in list)
            {
                wrap.Children.Add(CreateCompactCard(img, () => ShowLibraryView()));
            }
            host.Children.Add(wrap);
        }

        // Bottom status + sync
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var statusText = new TextBlock
        {
            Text = $"{all.Count} Joseph{(all.Count == 1 ? "" : "s")}",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(statusText, Dock.Left);
        bottom.Children.Add(statusText);
        var syncBtn = new Button
        {
            Content = "Sync",
            Style = (Style)FindResource("GhostButton"),
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Padding = new Thickness(8, 3, 8, 3),
            ToolTip = "Sync Josephs"
        };
        syncBtn.Click += async (_, _) => await _app.SyncNowAsync(() => ShowLibraryView());
        DockPanel.SetDock(syncBtn, Dock.Right);
        bottom.Children.Add(syncBtn);
        host.Children.Add(bottom);

        scroll.Content = host;
        _libraryGridHost.Children.Clear();
        _libraryGridHost.Children.Add(scroll);
    }

    private FrameworkElement CreateCompactCard(CelebrationImage image, Action onChanged)
    {
        var card = new Border
        {
            Width = 100,
            Height = 118,
            Margin = new Thickness(0, 0, 8, 8),
            Background = (Brush)FindResource("BgAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Cursor = Cursors.Hand,
            Padding = new Thickness(4)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var thumb = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            Margin = new Thickness(3, 3, 3, 0),
            CornerRadius = new CornerRadius(5),
            Child = new Image
            {
                Source = Services.Library!.LoadThumbnail(image.Id, image.FilePath),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(2)
            }
        };
        Grid.SetRow(thumb, 0);
        grid.Children.Add(thumb);

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 3, 6, 4) };
        nameRow.Children.Add(new TextBlock
        {
            Text = image.DisplayName,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        });
        if (image.Favorite)
        {
            nameRow.Children.Add(new TextBlock
            {
                Text = " ★",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0x42)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        Grid.SetRow(nameRow, 1);
        grid.Children.Add(nameRow);

        card.Child = grid;

        card.RenderTransform = new ScaleTransform(1, 1);
        card.MouseEnter += (_, _) =>
        {
            card.BorderBrush = (Brush)FindResource("BorderStrongBrush");
            var scale = card.RenderTransform as ScaleTransform;
            if (scale is not null)
            {
                scale.ScaleX = 1.02;
                scale.ScaleY = 1.02;
            }
        };
        card.MouseLeave += (_, _) =>
        {
            card.BorderBrush = (Brush)FindResource("BorderBrush");
            var scale = card.RenderTransform as ScaleTransform;
            if (scale is not null)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        };

        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                // Double click triggers preview celebration
                var oldMode = Services.Settings!.Current.ImageMode;
                var oldSpec = Services.Settings.Current.SpecificImageId;
                Services.Settings.Current.ImageMode = ImageMode.Specific;
                Services.Settings.Current.SpecificImageId = Guid.Parse(image.Id);
                Services.Celebration!.Trigger("preview");
                Services.Settings.Current.ImageMode = oldMode;
                Services.Settings.Current.SpecificImageId = oldSpec;
                Services.Settings.Save();
            }
            else
            {
                OpenImageDetails(image, onChanged);
            }
        };

        var menu = new ContextMenu();
        var celebrate = new MenuItem { Header = "Celebrate" };
        celebrate.Click += (_, _) =>
        {
            var oldMode = Services.Settings!.Current.ImageMode;
            var oldSpec = Services.Settings.Current.SpecificImageId;
            Services.Settings.Current.ImageMode = ImageMode.Specific;
            Services.Settings.Current.SpecificImageId = Guid.Parse(image.Id);
            Services.Celebration!.Trigger("menu");
            Services.Settings.Current.ImageMode = oldMode;
            Services.Settings.Current.SpecificImageId = oldSpec;
            Services.Settings.Save();
        };
        var fav = new MenuItem { Header = image.Favorite ? "Unfavorite" : "Favorite" };
        fav.Click += (_, _) =>
        {
            image.Favorite = !image.Favorite;
            Services.Library!.UpdateImage(image);
            onChanged();
        };
        var enable = new MenuItem { Header = image.Enabled ? "Disable" : "Enable" };
        enable.Click += (_, _) =>
        {
            image.Enabled = !image.Enabled;
            Services.Library!.UpdateImage(image);
            onChanged();
        };
        var details = new MenuItem { Header = "Details..." };
        details.Click += (_, _) => OpenImageDetails(image, onChanged);
        var del = new MenuItem { Header = "Delete" };
        del.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(this,
                $"Delete '{image.DisplayName}' from the library?",
                "Delete Joseph",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                Services.Library!.MarkDeleted(image.Id);
                onChanged();
            }
        };
        menu.Items.Add(celebrate);
        menu.Items.Add(fav);
        menu.Items.Add(enable);
        menu.Items.Add(details);
        menu.Items.Add(new Separator());
        menu.Items.Add(del);
        card.ContextMenu = menu;

        return card;
    }

    // =================================================================
    // IMAGE DETAILS (compact modal)
    // =================================================================

    private void OpenImageDetails(CelebrationImage image, Action onChanged)
    {
        var win = new Window
        {
            Title = image.DisplayName,
            Width = 380,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("BgBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        };
        var panel = new StackPanel { Margin = new Thickness(18) };

        var frame = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Height = 180,
            Margin = new Thickness(0, 0, 0, 12),
            Child = new Image
            {
                Source = ImageLibraryService.LoadImageSource(image.FilePath) as BitmapSource,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(6)
            }
        };
        panel.Children.Add(frame);

        panel.Children.Add(CreateDetailLabel("Display name"));
        var nameBox = new TextBox { Text = image.DisplayName, Margin = new Thickness(0, 3, 0, 10) };
        panel.Children.Add(nameBox);

        panel.Children.Add(CreateDetailLabel("Category"));
        var categoryBox = new TextBox { Text = image.Category ?? "", Margin = new Thickness(0, 3, 0, 10) };
        panel.Children.Add(categoryBox);

        panel.Children.Add(CreateDetailLabel("Tags"));
        var tagsBox = new TextBox { Text = image.Tags ?? "", Margin = new Thickness(0, 3, 0, 10) };
        panel.Children.Add(tagsBox);

        panel.Children.Add(CreateDetailLabel("Weight"));
        var weightBox = new TextBox { Text = image.Weight.ToString(), Margin = new Thickness(0, 3, 0, 10) };
        panel.Children.Add(weightBox);

        // Assign a sound clip to this specific Joseph (used by Assigned Sound mode).
        panel.Children.Add(CreateDetailLabel("Assigned Sound"));
        var soundCombo = new ComboBox { Margin = new Thickness(0, 3, 0, 10) };
        soundCombo.Items.Add("(none)");
        var soundLibrary = Services.SoundLibrary;
        var soundClips = soundLibrary?.GetAllSounds() ?? new List<SoundClip>();
        foreach (var sc in soundClips)
        {
            soundCombo.Items.Add(sc.DisplayName);
        }
        var selectedSoundIndex = 0;
        if (image.SoundId is not null && soundClips.Any(sc => sc.Id == image.SoundId))
        {
            var idx = soundClips.FindIndex(sc => sc.Id == image.SoundId);
            selectedSoundIndex = idx + 1; // offset by "(none)"
        }
        soundCombo.SelectedIndex = selectedSoundIndex;
        soundCombo.ToolTip = "Played when a celebration uses Assigned Sound mode for this Joseph.";
        panel.Children.Add(soundCombo);

        var favRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var favToggle = new CheckBox { Style = (Style)FindResource("ToggleSwitch"), IsChecked = image.Favorite, Padding = new Thickness(0) };
        favRow.Children.Add(favToggle);
        favRow.Children.Add(new TextBlock { Text = "  Favorite", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(4, 0, 0, 0) });
        panel.Children.Add(favRow);

        panel.Children.Add(new TextBlock
        {
            Text = $"Shown {image.TimesShown} time{(image.TimesShown == 1 ? "" : "s")}",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var celebrateBtn = new Button
        {
            Content = "CELEBRATE",
            Style = (Style)FindResource("PrimaryButton"),
            Height = 34,
            Margin = new Thickness(0, 0, 0, 8)
        };
        celebrateBtn.Click += (_, _) =>
        {
            var oldMode = Services.Settings!.Current.ImageMode;
            var oldSpec = Services.Settings.Current.SpecificImageId;
            Services.Settings.Current.ImageMode = ImageMode.Specific;
            Services.Settings.Current.SpecificImageId = Guid.Parse(image.Id);
            Services.Celebration!.Trigger("details");
            Services.Settings.Current.ImageMode = oldMode;
            Services.Settings.Current.SpecificImageId = oldSpec;
            Services.Settings.Save();
        };
        panel.Children.Add(celebrateBtn);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        var saveBtn = new Button { Content = "Save", Style = (Style)FindResource("PrimaryButton"), MinWidth = 90, Height = 32 };
        saveBtn.Click += (_, _) =>
        {
            image.DisplayName = string.IsNullOrWhiteSpace(nameBox.Text) ? image.DisplayName : nameBox.Text;
            image.Category = string.IsNullOrWhiteSpace(categoryBox.Text) ? null : categoryBox.Text;
            image.Tags = string.IsNullOrWhiteSpace(tagsBox.Text) ? null : tagsBox.Text;
            if (int.TryParse(weightBox.Text, out var w) && w > 0)
            {
                image.Weight = w;
            }
            image.Favorite = favToggle.IsChecked == true;
            // Persist the assigned sound clip for this Joseph.
            var scIndex = soundCombo.SelectedIndex;
            image.SoundId = scIndex > 0 && scIndex <= soundClips.Count ? soundClips[scIndex - 1].Id : null;
            Services.Library!.UpdateImage(image);
            onChanged();
            win.Close();
        };
        var deleteBtn = new Button { Content = "Delete", Style = (Style)FindResource("DangerButton"), MinWidth = 90, Height = 32, Margin = new Thickness(8, 0, 0, 0) };
        deleteBtn.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(win,
                $"Delete '{image.DisplayName}' from the library?",
                "Delete Joseph",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                Services.Library!.MarkDeleted(image.Id);
                onChanged();
                win.Close();
            }
        };
        btnRow.Children.Add(saveBtn);
        btnRow.Children.Add(deleteBtn);
        panel.Children.Add(btnRow);

        win.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Padding = new Thickness(0, 0, 10, 0) };
        win.ShowDialog();
    }

    private static TextBlock CreateDetailLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)),
            Margin = new Thickness(0, 0, 0, 0)
        };
    }

    // =================================================================
    // SETTINGS VIEW
    // =================================================================

    private void ShowSettingsView()
    {
        SetPrimaryNav("Settings");
        UpdateStatusBar();

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 48) };
        
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

        root.Children.Add(CreatePageHeader("Settings", "Customize your Joseph experience."));

        // Sub-navigation bar
        var nav = CreateSettingsNavBar();
        root.Children.Add(nav);
        _settingsContentHost = new StackPanel { Margin = new Thickness(0, 8, 0, 16) };
        root.Children.Add(_settingsContentHost);

        RefreshSettingsContent(_settingsContentHost);

        scroll.Content = root;
        _settingsScroll = scroll;
        CrossFade(scroll);
    }

    private void ShowMarketView()
    {
        SetPrimaryNav("Market");
        UpdateStatusBar();

        var naddSvc = Services.NaddService;
        if (naddSvc is not null)
        {
            // Keep the price chart live while this page is open.
            _naddChartHandler ??= () =>
            {
                if (_currentPageTag != "Market") return;
                Dispatcher.Invoke(RenderMarketChartFromCurrent);
            };
            naddSvc.DataUpdated -= _naddChartHandler;
            naddSvc.DataUpdated += _naddChartHandler;

            if (naddSvc.CurrentData is null && !naddSvc.IsRefreshing)
            {
                _ = naddSvc.RefreshAsync();
            }
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 48) };
        
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

        root.Children.Add(CreatePageHeader("Market", "Real NADD/SOL token data from GeckoTerminal and celebration activity stats."));

        var nadd = Services.NaddService?.CurrentData;

        // Real NADD/SOL market data
        root.Children.Add(CreateSectionHeader("NADD / SOL TOKEN"));
        var marketCard = CreateCard(16);

        var marketGrid = new Grid();
        marketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marketGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marketInfo = new StackPanel { Margin = new Thickness(12, 12, 0, 12) };
        marketInfo.Children.Add(new TextBlock
        {
            Text = nadd is not null
                ? RealNaddService.FormatPrice(nadd.PriceUsd)
                : "Loading...",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = nadd is not null ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextMutedBrush")
        });

        if (nadd is not null)
        {
            marketInfo.Children.Add(new TextBlock
            {
                Text = $"24h Change: {nadd.Change24hPercent:+0.00;-0.00;0.00}%",
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = nadd.Change24hPercent >= 0
                    ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A))
                    : new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x79))
            });

            marketInfo.Children.Add(new TextBlock
            {
                Text = $"Liquidity: ${nadd.LiquidityUsd:N0}",
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });

            marketInfo.Children.Add(new TextBlock
            {
                Text = $"24h Volume: ${nadd.Volume24hUsd:N0}",
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });

            var txText = nadd.Transactions24h > 0
                ? $"Transactions (24h): {nadd.Transactions24h} ({nadd.BuyTransactions24h} buys / {nadd.SellTransactions24h} sells)"
                : "Transactions (24h): N/A";
            marketInfo.Children.Add(new TextBlock
            {
                Text = txText,
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });

            if (nadd.MarketCapUsd > 0)
            {
                marketInfo.Children.Add(new TextBlock
                {
                    Text = $"Market Cap: ${nadd.MarketCapUsd:N0} | FDV: ${nadd.FdvUsd:N0}",
                    FontSize = 10.5,
                    Margin = new Thickness(0, 2, 0, 0),
                    Foreground = (Brush)FindResource("TextSecondaryBrush")
                });
            }

            marketInfo.Children.Add(new TextBlock
            {
                Text = $"Updated: {nadd.LastUpdated:HH:mm:ss} UTC",
                FontSize = 9,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource("TextMutedBrush")
            });
        }
        else
        {
            marketInfo.Children.Add(new TextBlock
            {
                Text = "Waiting for GeckoTerminal API response...",
                FontSize = 10.5,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource("TextMutedBrush")
            });
        }

        Grid.SetColumn(marketInfo, 0);
        marketGrid.Children.Add(marketInfo);

        // Price chart
        _marketChartContainer = new Grid();
        Grid.SetColumn(_marketChartContainer, 1);
        marketGrid.Children.Add(_marketChartContainer);
        RenderMarketChart(_marketChartContainer, nadd?.OhlcvData);

        marketCard.Child = marketGrid;
        root.Children.Add(marketCard);

        // OHLCV range selector
        root.Children.Add(CreateSectionHeader("PRICE RANGE"));
        var rangeBar = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
        var ranges = new[] { "1H", "6H", "24H", "7D", "30D" };
        foreach (var r in ranges)
        {
            var btn = new Button
            {
                Content = r,
                FontSize = 10.5,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0)
            };
            btn.Click += async (_, _) =>
            {
                var data = await Services.NaddService?.FetchOhlcvRangeAsync(r) ?? new();
                if (_marketChartContainer is null) return;
                _marketChartContainer.Children.Clear();
                RenderMarketChart(_marketChartContainer, data);
            };
            rangeBar.Children.Add(btn);
        }
        root.Children.Add(rangeBar);

        // Celebration Activity graph
        root.Children.Add(CreateSectionHeader("CELEBRATION ACTIVITY"));
        root.Children.Add(new TextBlock
        {
            Text = "Celebration frequency over time. Based on real SQLite history data.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var activityCard = CreateCard(16);
        var stats = Services.Library!.GetStats();
        activityCard.Child = BuildActivityChart(stats, HistoryRange.Hours24);
        root.Children.Add(activityCard);

        // Activity range selector
        root.Children.Add(new TextBlock
        {
            Text = "Range:",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 8, 0, 4)
        });
        var actRangeBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var activityRanges = new[]
        {
            ("24 Hours", HistoryRange.Hours24),
            ("7 Days", HistoryRange.Days7),
            ("30 Days", HistoryRange.Days30),
            ("90 Days", HistoryRange.Days90),
            ("All Time", HistoryRange.All)
        };
        foreach (var (label, range) in activityRanges)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 10.5,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0)
            };
            btn.Click += (_, _) =>
            {
                activityCard.Child = BuildActivityChart(stats, range);
            };
            actRangeBar.Children.Add(btn);
        }
        root.Children.Add(actRangeBar);

        // Internal Joseph Coins info
        root.Children.Add(CreateSectionHeader("JOSEPH COINS"));
        root.Children.Add(new TextBlock
        {
            Text = "These are internal reward points earned from celebrations — NOT the real NADD/SOL token. See the Market section above for real token data.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        var coinsCard = CreateCard(16);
        coinsCard.Child = new TextBlock
        {
            Text = $"Sounds Played: {stats.SoundsPlayed:N0}",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            Margin = new Thickness(12, 12, 12, 12)
        };
        root.Children.Add(coinsCard);

        scroll.Content = root;
        CrossFade(scroll);
    }

    private void RenderMarketChartFromCurrent()
    {
        if (_marketChartContainer is null) return;
        _marketChartContainer.Children.Clear();
        RenderMarketChart(_marketChartContainer, Services.NaddService?.CurrentData?.OhlcvData);
    }

    private void RenderMarketChart(Grid container, IReadOnlyList<OhlcvPoint>? data)
    {
        container.Children.Clear();
        if (data is null || data.Count <= 1)
        {
            container.Children.Add(new TextBlock
            {
                Text = data is null
                    ? "No price history yet — waiting for GeckoTerminal data…"
                    : "Not enough price history to draw a chart yet.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            });
            return;
        }
        var chart = new BarChartView { Height = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
        var closes = data.Select(p => p.Close).ToList();
        var timeLabels = data.Select(p => p.Timestamp.ToString("HH:mm")).ToList();
        chart.SetPriceData(closes, timeLabels);
        chart.Margin = new Thickness(0, 0, 12, 0);
        container.Children.Add(chart);
    }

    private FrameworkElement BuildActivityChart(LibraryStats stats, HistoryRange range)
    {
        var buckets = Services.Library!.GetHistoryBuckets(range);
        var counts = buckets.Select(b => b.Count).ToList();
        var labels = buckets.Select(b => FormatBucketLabel(b.Timestamp, ActivityBucketSize(range))).ToList();

        var chart = new BarChartView { Height = 180 };
        chart.SetBarData(counts, labels);

        var info = new TextBlock
        {
            Text = $"Total celebrations: {stats.TotalCelebrations:N0}",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(12, 0, 12, 12)
        };

        var panel = new StackPanel();
        panel.Children.Add(chart);
        panel.Children.Add(info);
        return panel;
    }

    private static TimeSpan ActivityBucketSize(HistoryRange range) => range switch
    {
        HistoryRange.Hours24 => TimeSpan.FromHours(1),
        HistoryRange.Days7 => TimeSpan.FromHours(6),
        HistoryRange.Days30 => TimeSpan.FromDays(1),
        HistoryRange.Days90 => TimeSpan.FromDays(1),
        _ => TimeSpan.FromDays(1)
    };

    private static string FormatBucketLabel(DateTime t, TimeSpan bucket) =>
        bucket >= TimeSpan.FromDays(1) ? t.ToString("MM-dd")
        : bucket >= TimeSpan.FromHours(6) ? t.ToString("MM-dd HH:mm")
        : t.ToString("HH:mm");

    private void RefreshSettingsContent(StackPanel contentHost)
    {
        contentHost.Children.Clear();
        var page = _settingsSubPage switch
        {
            "General" => BuildSettingsGeneral(),
            "Overlay" => BuildSettingsOverlay(),
            "Text" => BuildSettingsText(),
            "Animations" => BuildSettingsAnimations(),
            "Sounds" => BuildSettingsSounds(),
            "Hotkeys" => BuildSettingsHotkeys(),
            "Updates" => BuildSettingsUpdates(),
            "Advanced" => BuildSettingsAdvanced(),
            _ => BuildSettingsGeneral()
        };
        contentHost.Children.Add(page);
    }

    private void RefreshSettingsPage()
    {
        if (_settingsContentHost is not null)
            RefreshSettingsContent(_settingsContentHost);
        _settingsScroll?.ScrollToTop();
    }

    private FrameworkElement CreateSettingsNavBar()
    {
        var items = new[] { "General", "Overlay", "Text", "Animations", "Sounds", "Hotkeys", "Updates", "Advanced" };
        var wrap = new WrapPanel { Margin = new Thickness(16), IsItemsHost = true };

        foreach (var item in items)
        {
            var isActive = _settingsSubPage == item;
            var btn = new Button
            {
                Content = item,
                FontSize = 13,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Padding = new Thickness(16, 10, 16, 10),
                Height = 36,
                MinWidth = 90,
                BorderThickness = new Thickness(1),
                BorderBrush = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush"),
                Background = isActive ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("ElevatedBrush"),
                Foreground = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextSecondaryBrush"),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 6),
                OverridesDefaultStyle = true,
                SnapsToDevicePixels = true,
            };
            btn.Click += (_, _) =>
            {
                _settingsSubPage = item;
                RefreshSettingsPage();
            };
            wrap.Children.Add(btn);
        }

        var wrapper = new StackPanel();
        wrapper.Children.Add(wrap);
        wrapper.Children.Add(new Separator { Margin = new Thickness(6, 8, 6, 8), BorderBrush = (Brush)FindResource("BorderStrongBrush"), Opacity = 0.3 });
        return wrapper;
    }

    // ------------------------------------------------------------------
    // Settings sub-page builders
    // ------------------------------------------------------------------

    private StackPanel BuildSettingsGeneral()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(16) };

        // Celebrations enabled toggle
        panel.Children.Add(CreateSectionHeader("CELEBRATIONS"));
        panel.Children.Add(CreateToggleRow("Celebrations enabled", settings.Enabled, v =>
        {
            settings.Enabled = v;
            Services.Settings.Save();
            UpdateStatusBar();
        }));

        // Start/Close behavior group
        panel.Children.Add(CreateSectionHeader("START & CLOSE"));
        panel.Children.Add(CreateToggleRow("Start with Windows", settings.StartWithWindows, v =>
        {
            settings.StartWithWindows = v;
            StartupService.SetEnabled(v);
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateToggleRow("Minimize to tray", settings.MinimizeToTray, v =>
        {
            settings.MinimizeToTray = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateToggleRow("Close to tray", settings.CloseToTray, v =>
        {
            settings.CloseToTray = v;
            Services.Settings.Save();
        }));

        // Launch/Close behavior group
        panel.Children.Add(CreateSectionHeader("LAUNCH & CLOSE BEHAVIOR"));
        panel.Children.Add(CreateEnumComboRow("Launch behavior", settings.LaunchBehavior, v =>
        {
            settings.LaunchBehavior = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateEnumComboRow("Close behavior", settings.CloseBehavior, v =>
        {
            settings.CloseBehavior = v;
            Services.Settings.Save();
        }));

        return panel;
    }
private StackPanel BuildSettingsOverlay()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(16) };

        // Position group
        panel.Children.Add(CreateSectionHeader("POSITION"));
        panel.Children.Add(new TextBlock
        {
            Text = "Screen Position",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(CreatePositionPicker(settings, _ => { }));
        panel.Children.Add(CreateSliderRow("Custom X", 0.0, 1.0, settings.CustomPositionX, v =>
        {
            settings.CustomPositionX = Math.Clamp(v, 0.0, 1.0);
            Services.Settings.Save();
        }, 100, 6));
        panel.Children.Add(CreateSliderRow("Custom Y", 0.0, 1.0, settings.CustomPositionY, v =>
        {
            settings.CustomPositionY = Math.Clamp(v, 0.0, 1.0);
            Services.Settings.Save();
        }, 100, 6));

        // Monitor group
        panel.Children.Add(CreateSectionHeader("MONITOR"));
        panel.Children.Add(CreateEnumComboRow("Monitor", settings.MonitorMode, v =>
        {
            settings.MonitorMode = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Fit mode", settings.FitMode, v =>
        {
            settings.FitMode = v;
            Services.Settings.Save();
        }, 140));

        // Appearance group
        panel.Children.Add(CreateSectionHeader("APPEARANCE"));
        panel.Children.Add(CreateToggleRow("Lock aspect ratio", settings.LockAspectRatio, v =>
        {
            settings.LockAspectRatio = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateSliderRow("Joseph opacity", 0.25, 1.0, settings.ImageOpacity, v =>
        {
            settings.ImageOpacity = Math.Clamp(v, 0.25, 1.0);
            Services.Settings.Save();
        }, 100, 4));
        panel.Children.Add(CreateEnumComboRow("Size preset", settings.SizePreset, v =>
        {
            settings.SizePreset = v;
            Services.Settings.Save();
        }, 140));
        if (settings.SizePreset == SizePreset.Custom)
        {
            panel.Children.Add(CreateSliderRow("Custom scale (%)", 25, 300, settings.CustomScalePercent, v =>
            {
                settings.CustomScalePercent = (int)v;
                Services.Settings.Save();
            }, 100, 6));
        }

        panel.Children.Add(CreateEnumComboRow("Safe margin", settings.SafeMarginPreset, v =>
        {
            settings.SafeMarginPreset = v;
            Services.Settings.Save();
        }, 140));
        if (settings.SafeMarginPreset == SafeMarginPreset.Custom)
        {
            panel.Children.Add(CreateSliderRow("Custom margin (px)", 0, 200, settings.SafeMarginPixels, v =>
            {
                settings.SafeMarginPixels = v;
                Services.Settings.Save();
            }, 100, 6));
        }

        // Behavior group
        panel.Children.Add(CreateSectionHeader("BEHAVIOR"));
        panel.Children.Add(CreateToggleRow("Low-distraction mode", settings.LowDistraction, v =>
        {
            settings.LowDistraction = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateToggleRow("Debug overlay bounds", settings.DebugOverlayBounds, v =>
        {
            settings.DebugOverlayBounds = v;
            Services.Settings.Save();
        }, 140));

        return panel;
    }

    private StackPanel BuildSettingsText()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(16) };

        // Master toggle
        panel.Children.Add(CreateSectionHeader("CELEBRATION TEXT"));
        panel.Children.Add(CreateToggleRow("Show celebration text", settings.ShowCelebrationText, v =>
        {
            settings.ShowCelebrationText = v;
            Services.Settings.Save();
        }, 140));

        if (!settings.ShowCelebrationText)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Text is disabled. Enable to show celebratory quotes.",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        // Text content group
        panel.Children.Add(CreateSectionHeader("TEXT CONTENT"));
        panel.Children.Add(CreateEnumComboRow("Text mode", settings.TextMode, v =>
        {
            settings.TextMode = v;
            Services.Settings.Save();
        }, 140));

        if (settings.TextMode == TextMode.CustomGlobal)
        {
            var globalText = new TextBox
            {
                Text = settings.CelebrationText,
                ToolTip = "Custom text shown during celebrations.",
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                Height = 36
            };
            globalText.TextChanged += (_, __) =>
            {
                settings.CelebrationText = globalText.Text;
                Services.Settings.Save();
            };
            panel.Children.Add(globalText);
        }

        // Quote source info
        panel.Children.Add(new TextBlock
        {
            Text = "Quotes come from the built-in lore pool. Set Text mode to Custom to override.",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 8, 0, 6)
        });

        // Text appearance group
        panel.Children.Add(CreateSectionHeader("TEXT APPEARANCE"));
        panel.Children.Add(CreateEnumComboRow("Position", settings.TextPosition, v =>
        {
            settings.TextPosition = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Font size preset", settings.TextFontSizePreset, v =>
        {
            settings.TextFontSizePreset = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateSliderRow("Font scale", 0.5, 2.0, settings.TextScale, v =>
        {
            settings.TextScale = Math.Round(v, 2);
            Services.Settings.Save();
        }, 100, 4));
        panel.Children.Add(CreateEnumComboRow("Weight", settings.TextWeight, v =>
        {
            settings.TextWeight = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateSliderRow("Opacity", 0.0, 1.0, settings.TextOpacity, v =>
        {
            settings.TextOpacity = Math.Round(v, 2);
            Services.Settings.Save();
        }, 100, 4));

        // Text FX group
        panel.Children.Add(CreateSectionHeader("TEXT FX"));
        panel.Children.Add(CreateEnumComboRow("Text FX style", settings.TextFx, v =>
        {
            settings.TextFx = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Text shadow", settings.TextShadow, v =>
        {
            settings.TextShadow = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Text outline", settings.TextOutline, v =>
        {
            settings.TextOutline = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Text animation", settings.TextAnimation, v =>
        {
            settings.TextAnimation = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateSliderRow("Text delay (ms)", 0, 500, settings.TextDelayMs, v =>
        {
            settings.TextDelayMs = v;
            Services.Settings.Save();
        }, 100, 4));

        return panel;
    }
private StackPanel BuildSettingsAnimations()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(16) };

        // Preset group
        panel.Children.Add(CreateSectionHeader("PRESET"));
        panel.Children.Add(CreateInfoRow("Pick a vibe. Each preset controls image animation, text FX, intensity, duration and placement."));
        panel.Children.Add(CreatePresetGrid(settings));

        panel.Children.Add(new Separator());

        // Image animation group
        panel.Children.Add(CreateSectionHeader("IMAGE ANIMATION"));
        panel.Children.Add(CreateEnumComboRow("Animation style", settings.AnimationStyle, v =>
        {
            settings.AnimationStyle = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Easing", settings.EasingStyle, v =>
        {
            settings.EasingStyle = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Entry speed", settings.EntrySpeed, v =>
        {
            settings.EntrySpeed = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateEnumComboRow("Exit style", settings.ExitStyle, v =>
        {
            settings.ExitStyle = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(new Separator());
        panel.Children.Add(CreateToggleRow("Random animation", settings.RandomAnimation, v =>
        {
            settings.RandomAnimation = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateToggleRow("Exclude jumpscare from random", settings.ExcludeJumpscareFromRandom, v =>
        {
            settings.ExcludeJumpscareFromRandom = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateToggleRow("Exclude \"None\" from random", settings.ExcludeNoneFromRandom, v =>
        {
            settings.ExcludeNoneFromRandom = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateToggleRow("Synchronize FX", settings.SynchronizeFx, v =>
        {
            settings.SynchronizeFx = v;
            Services.Settings.Save();
        }, 140));

        // Image selection group
        panel.Children.Add(new Separator());
        panel.Children.Add(CreateSectionHeader("IMAGE SELECTION"));
        panel.Children.Add(CreateEnumComboRow("Deploy mode", settings.ImageMode, v =>
        {
            settings.ImageMode = v;
            Services.Settings.Save();
        }, 140));
        if (settings.ImageMode == ImageMode.Specific)
        {
            var images = Services.Library!.GetAllImages();
            var current = settings.SpecificImageId?.ToString();
            var currentName = images.FirstOrDefault(i => i.Id == current)?.DisplayName ?? "Pick an image";
            panel.Children.Add(CreateComboRow("Image", images.Select(i => i.DisplayName).DefaultIfEmpty("(none)"), currentName, v =>
            {
                var match = images.FirstOrDefault(i => i.DisplayName == v);
                if (match is not null)
                {
                    settings.SpecificImageId = Guid.Parse(match.Id);
                    Services.Settings.Save();
                }
            }, 140));
        }
        panel.Children.Add(CreateToggleRow("Avoid repeats", settings.AvoidImmediateRepeats, v =>
        {
            settings.AvoidImmediateRepeats = v;
            Services.Settings.Save();
        }, 140));
        panel.Children.Add(CreateSliderRow("Repeat cooldown (min)", 0, 30, settings.RepeatCooldown, v =>
        {
            settings.RepeatCooldown = (int)v;
            Services.Settings.Save();
        }, 100, 4));

        // Preset intensity group
        panel.Children.Add(new Separator());
        panel.Children.Add(CreateSectionHeader("PRESET INTENSITY"));
        panel.Children.Add(CreateInfoRow("Controls the strength of animations and text FX."));
        var intensityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var intensityOptions = Enum.GetNames<FxIntensity>();
        foreach (var opt in intensityOptions)
        {
            var isActive = settings.FxIntensity.ToString() == opt;
            var btn = new Button
            {
                Content = DisplayNames.GetFriendlyName<FxIntensity>(Enum.Parse<FxIntensity>(opt)),
                FontSize = 10.5,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(4, 0, 4, 0),
                BorderThickness = new Thickness(isActive ? 3 : 2),
                BorderBrush = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush"),
                Background = isActive ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("ElevatedBrush"),
                Foreground = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextSecondaryBrush"),
                Cursor = Cursors.Hand,
                ToolTip = opt
            };
            btn.Click += (_, _) =>
            {
                settings.FxIntensity = Enum.Parse<FxIntensity>(opt);
                Services.Settings.Save();
                RefreshSettingsPage();
            };
            intensityRow.Children.Add(btn);
        }
        panel.Children.Add(intensityRow);

        return panel;
    }

private StackPanel BuildSettingsSounds()
    {
        var settings = Services.Settings!.Current;
        var sounds = Services.SoundLibrary;
        var all = sounds?.GetAllSounds() ?? new List<SoundClip>();

        var panel = new StackPanel { Margin = new Thickness(16) };

        // Master switch
        panel.Children.Add(CreateSectionHeader("MASTER SWITCH"));
        panel.Children.Add(CreateToggleRow("Celebration Sounds", settings.PlaySound, v =>
        {
            settings.PlaySound = v;
            Services.Settings.Save();
            RefreshSettingsPage();
        }, 140));

        panel.Children.Add(CreateSliderRow("Volume (%)", 0, 100, settings.SoundVolume * 100, v =>
        {
            settings.SoundVolume = Math.Round(v / 100.0, 2);
            Services.Settings.Save();
        }, 150, 6));

        panel.Children.Add(CreateEnumComboRow("Sound Mode", settings.SoundMode, v =>
        {
            settings.SoundMode = v;
            Services.Settings.Save();
        }, 140));

        panel.Children.Add(CreateInfoRow("Random Sound plays a random imported clip. Assigned Sound plays the clip attached to each Joseph (set in Library → image details), falling back to the global selection. No Sound keeps celebrations silent.", 12));

        panel.Children.Add(new Separator());

        // Sound library group
        panel.Children.Add(CreateSectionHeader("SOUND LIBRARY"));

        if (!settings.PlaySound)
        {
            panel.Children.Add(CreateInfoRow("Celebration Sounds is OFF — celebrations remain silent. Turn the toggle on to hear your clips.", 12));
        }

        var uploadBtn = new Button
        {
            Content = "Upload Audio",
            Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 38,
            Margin = new Thickness(0, 8, 0, 12),
            ToolTip = "Import a WAV or MP3 clip into the managed sound library"
        };
        uploadBtn.Click += (_, _) => ImportSoundClip();
        panel.Children.Add(uploadBtn);

        if (all.Count == 0)
        {
            panel.Children.Add(CreateInfoRow("No sound clips imported yet. Upload a WAV or MP3 to add audio to your celebrations.", 12));
        }
        else
        {
            foreach (var clip in all)
            {
                panel.Children.Add(CreateSoundClipCard(clip));
            }
        }

        return panel;
    }

// ------------------------------------------------------------------
    // Sound library UI helpers
    // ------------------------------------------------------------------

    private void ImportSoundClip()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Upload Audio",
            Filter = "Audio files (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var library = Services.SoundLibrary;
        if (library is null)
        {
            MessageBox.Show(this, "Sound library is not available.", "The Joseph Experience 2.0", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = library.ImportSound(dialog.FileName);
        if (result.Imported == 1)
        {
            _settingsSubPage = "Sounds";
            ShowSettingsView();
            SetStatus("AUDIO ADDED", new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)));
        }
        else if (result.SkippedDuplicate)
        {
            MessageBox.Show(this, "That clip is already in your sound library.", "The Joseph Experience 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, string.IsNullOrEmpty(result.Error) ? "The clip could not be imported." : result.Error, "Upload Audio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private FrameworkElement CreateSoundClipCard(SoundClip clip)
    {
        var settings = Services.Settings!.Current;

        var card = new Border
        {
            Background = (Brush)FindResource("ElevatedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var row = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = clip.DisplayName,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (!clip.Exists)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = "   (file missing — will be skipped safely)",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x79)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var useToggle = new CheckBox
        {
            Style = (Style)FindResource("ToggleSwitch"),
            IsChecked = settings.SelectedSoundId == clip.Id,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0),
            ToolTip = "Set as the selected / global sound"
        };
        useToggle.Checked += (_, _) =>
        {
            settings.SelectedSoundId = clip.Id;
            Services.Settings.Save();
        };
        useToggle.Unchecked += (_, _) =>
        {
            if (settings.SelectedSoundId == clip.Id)
            {
                settings.SelectedSoundId = null;
                Services.Settings.Save();
            }
        };
        titleRow.Children.Add(useToggle);
        titleRow.Children.Add(new TextBlock
        {
            Text = "Use",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(titleRow);

        row.Children.Add(new TextBlock
        {
            Text = $"{clip.Extension.ToUpperInvariant()}   ·   {FormatBytes(clip.FileSize)}   ·   played {clip.TimesPlayed}×",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 6)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var preview = new Button
        {
            Content = "Preview",
            Style = (Style)FindResource("GhostButton"),
            FontSize = 10.5,
            Padding = new Thickness(8, 3, 8, 3),
            IsEnabled = clip.Exists
        };
        preview.Click += (_, _) => Services.Audio?.Preview(settings, clip.FilePath);
        buttons.Children.Add(preview);

        var remove = new Button
        {
            Content = "Remove",
            Style = (Style)FindResource("DangerButton"),
            FontSize = 10.5,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 0, 0, 0)
        };
        remove.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(this,
                $"Remove '{clip.DisplayName}' from the sound library? Any Joseph assigned to it will stop using it.",
                "Remove Audio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                if (settings.SelectedSoundId == clip.Id)
                {
                    settings.SelectedSoundId = null;
                    Services.Settings.Save();
                }
                Services.SoundLibrary?.DeleteSound(clip);
                RefreshSettingsPage();
            }
        };
        buttons.Children.Add(remove);
        row.Children.Add(buttons);

        card.Child = row;
        return card;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }

    private StackPanel BuildSettingsHotkeys()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(16) };

        // Primary celebration hotkey (F2)
        panel.Children.Add(CreateSectionHeader("PRIMARY CELEBRATION (F2)"));
        panel.Children.Add(new TextBlock
        {
            Text = "Press this hotkey to deploy Joseph manually.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var primaryBinding = settings.GetBinding(HotkeyAction.Celebration);
        var primaryDisplay = primaryBinding.IsEmpty
            ? HotkeyConverter.GetDefaultBinding(HotkeyAction.Celebration).DisplayName
            : primaryBinding.DisplayName;
        TextBlock primaryText = null!;
        var hotkeyBtn = CreateHotkeyBubbleLargeWithText(primaryDisplay, () =>
            OpenHotkeyDialog(primaryText, HotkeyAction.Celebration, primaryBinding));
        primaryText = hotkeyBtn.textBlock;
        hotkeyBtn.border.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(hotkeyBtn.border);

        var primaryReset = new Button
        {
            Content = "Reset",
            Style = (Style)FindResource("SecondaryButton"),
            FontSize = 10.5,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 8, 0, 0)
        };
        primaryReset.Click += (_, _) =>
        {
            _app.ResetHotkey(HotkeyAction.Celebration);
            RefreshSettingsPage();
        };
        panel.Children.Add(primaryReset);

        panel.Children.Add(new TextBlock
        {
            Text = "Click the hotkey bubble above, then press a new key combination. Use Ctrl, Alt, Shift, or Win modifiers with a letter/number, or a function key (F1-F12).",
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 12, 0, 0)
        });

        // Audio toggle hotkey (F8)
        panel.Children.Add(CreateSectionHeader("AUDIO TOGGLE (F8)"));
        panel.Children.Add(new TextBlock
        {
            Text = "Toggles celebration sounds on/off. Stops any currently playing audio when muting.",
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var audioBinding = settings.AudioToggleHotkey;
        var audioTextDisplay = audioBinding.IsEmpty
            ? "F8"
            : HotkeyConverter.GetBindingDisplayName(audioBinding.ModifierValue, audioBinding.VirtualKey, audioBinding.KeyName);

        TextBlock audioText = null!;
        var audioResult = CreateHotkeyBubbleLargeWithText(audioTextDisplay, () =>
        {
            OpenHotkeyDialog(audioText, HotkeyAction.AudioToggle, audioBinding);
        });
        audioText = audioResult.textBlock;
        audioResult.border.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(audioResult.border);

        var audioReset = new Button
        {
            Content = "Reset",
            Style = (Style)FindResource("SecondaryButton"),
            FontSize = 10.5,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 8, 0, 0)
        };
        audioReset.Click += (_, _) =>
        {
            _app.ResetHotkey(HotkeyAction.AudioToggle);
            RefreshSettingsPage();
        };
        panel.Children.Add(audioReset);

        return panel;
    }

    private (FrameworkElement border, TextBlock textBlock) CreateHotkeyBubbleLargeWithText(string displayText, Action onClick)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("AccentSoftBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Height = 44,
            Cursor = Cursors.Hand,
            ToolTip = "Click to rebind — then press a key"
        };
        var text = new TextBlock
        {
            Text = displayText,
            Foreground = (Brush)FindResource("AccentBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Child = text;
        border.MouseLeftButtonDown += (_, _) => onClick();
        border.ToolTip = "Click to rebind — then press a key";
        return (border, text);
    }

    private StackPanel BuildSettingsUpdates()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(16) };

        // ---- Update check header ----
        panel.Children.Add(CreateSectionHeader("UPDATE CHECK"));

        var checkUpdateBtn = new Button
        {
            Content = "Check for Updates",
            Style = (Style)FindResource("PrimaryButton"),
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 12)
        };
        checkUpdateBtn.Click += async (_, _) =>
        {
            checkUpdateBtn.IsEnabled = false;
            checkUpdateBtn.Content = "Checking…";
            try
            {
                var versionService = new VersionService();
                var updateService = new UpdateService(
                    versionService,
                    new HttpUpdateProvider(settings.UpdateManifestUrl ?? ""),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppContext.BaseDirectory);

                var checkResult = await updateService.CheckForUpdatesAsync();

                _lastUpdateCheckResult = checkResult;

                if (checkResult.Result == UpdateCheckResult.UpdateAvailable && checkResult.RemoteVersion is not null)
                {
                    checkUpdateBtn.IsEnabled = true;
                    checkUpdateBtn.Content = "Check for Updates";
                    ShowUpdateDialog(checkResult, updateService);
                }
                else if (checkResult.Result == UpdateCheckResult.UpToDate)
                {
                    checkUpdateBtn.IsEnabled = true;
                    checkUpdateBtn.Content = "Check for Updates";
                    UpdateStatusBar();
                    MessageBox.Show(this,
                        "You're up to date.\n\n" +
                        $"The Joseph Experience 2.0 {updateService.CurrentVersionString} is the latest version.",
                        "The Joseph Experience 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                checkUpdateBtn.IsEnabled = true;
                checkUpdateBtn.Content = "Check for Updates";
                MessageBox.Show(this, $"Update check failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        panel.Children.Add(checkUpdateBtn);

        // Dynamic update status
        var updateStatus = new TextBlock
        {
            Text = "Update status: not checked",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 4, 0, 8)
        };
        panel.Children.Add(updateStatus);

        // ---- Release notes ----
        var viewNotesBtn = new Button
        {
            Content = "View Release Notes",
            Style = (Style)FindResource("SecondaryButton"),
            Height = 28,
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4)
        };
        viewNotesBtn.Click += (_, _) =>
        {
            if (_lastUpdateCheckResult?.Result == UpdateCheckResult.UpdateAvailable && _lastUpdateCheckResult.RemoteVersion is not null)
            {
                ShowReleaseNotesDialog(_lastUpdateCheckResult);
            }
            else
            {
                MessageBox.Show(this, "No update is available. Release notes are shown when a new version is detected.", "Release Notes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        panel.Children.Add(viewNotesBtn);

        // ---- Manifest ----
        panel.Children.Add(CreateSectionHeader("MANIFEST"));
        var manifestBox = new TextBox
        {
            Text = settings.UpdateManifestUrl ?? "",
            ToolTip = "URL to the update manifest JSON.",
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 4),
            Height = 36
        };
        manifestBox.TextChanged += (_, __) =>
        {
            settings.UpdateManifestUrl = manifestBox.Text;
            Services.Settings.Save();
        };
        panel.Children.Add(manifestBox);
        panel.Children.Add(new TextBlock
        {
             Text = "Default: https://raw.githubusercontent.com/icecut710/TheJosephExperience/master/updates/update-manifest.json",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // ---- Version ----
        panel.Children.Add(CreateSectionHeader("VERSION"));
        panel.Children.Add(new TextBlock
        {
             Text = $"The Joseph Experience {App.Version}",
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Standalone desktop overlay. No game injection or memory access.",
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 0)
        });

        return panel;
    }

    private StackPanel BuildSettingsAdvanced()
    {
        var settings = Services.Settings!.Current;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

        // History
        panel.Children.Add(CreateSectionHeader("HISTORY"));
        panel.Children.Add(CreateSliderRow("Recent celebrations", 5, 100, settings.CelebrationHistoryLimit, v =>
        {
            settings.CelebrationHistoryLimit = Math.Clamp((int)v, 5, 100);
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateToggleRow("Record 3D model celebrations", settings.Record3DModelHistory, v =>
        {
            settings.Record3DModelHistory = v;
            Services.Settings.Save();
        }));

        var history = Services.History?.GetRecent(10) ?? Array.Empty<Models.CelebrationHistory>();
        if (history.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No recent celebrations yet.",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
        else
        {
            foreach (var entry in history)
            {
                 var displayName = entry.ImageId ?? "Unknown";
                 var trigger = FormatTriggerType(entry.TriggerType);
                 panel.Children.Add(new TextBlock
                 {
                     Text = $"{entry.ShownAt}  {displayName}  ({trigger})",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondaryBrush")
                });
            }
        }

        if (history.Count > 0)
        {
            panel.Children.Add(CreateButtonRow("Clear history", () =>
            {
                Services.History?.Clear();
                RefreshSettingsPage();
            }));
        }

        panel.Children.Add(new Separator());

        // Cloud sync
        panel.Children.Add(CreateSectionHeader("CLOUD SYNC"));
        var supabase = Services.Supabase;
        var isConfigured = supabase != null && supabase.Config.IsConfigured;
        // Status dot + line
        var statusRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                statusRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                statusRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // Dot
        Brush dotBrush;
        if (supabase != null && supabase.IsConnected)
        {
            dotBrush = (Brush)FindResource("SuccessBrush");
        }
        else if (supabase != null && supabase.State == SupabaseState.Syncing)
        {
            dotBrush = new SolidColorBrush(Color.FromRgb(0x72, 0x81, 0xFF));
        }
        else if (supabase != null && (supabase.State == SupabaseState.Offline || supabase.State == SupabaseState.Error))
        {
            dotBrush = (Brush)FindResource("DangerBrush");
        }
        else
        {
            dotBrush = (Brush)FindResource("MutedBrush");
        }
        var dot = new Ellipse { Width = 12, Height = 12, Fill = dotBrush, Margin = new Thickness(0, 0, 8, 0) };
        // Status text
        var statusText = new TextBlock
        {
            Text = isConfigured ? $"Supabase connected ({supabase!.StateText})" : "Supabase not configured",
            FontSize = 11,
            FontWeight = isConfigured ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isConfigured ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        // Sync now button
        var syncNow = new Button { Content = "Sync Now", Style = (Style)FindResource("PrimaryButton"), Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
        syncNow.Click += async (_, _) => await _app.SyncNowAsync(() => ShowSettingsView());
        // Info line
        var infoLine = new TextBlock
        {
            Text = "Celebration images and audio are stored in the cloud. Add a .env with SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY, and SUPABASE_BUCKET to enable sync.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        // Assemble
        var leftPanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        leftPanel.Children.Add(dot);
        leftPanel.Children.Add(statusText);
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        rightPanel.Children.Add(syncNow);
        rightPanel.Children.Add(infoLine);
        statusRow.Children.Add(leftPanel);
        statusRow.Children.Add(rightPanel);
        panel.Children.Add(statusRow);

        panel.Children.Add(CreateEnumComboRow("Sync frequency", settings.SyncFrequency, v =>
        {
            settings.SyncFrequency = v;
            Services.Settings.Save();
        }));

        panel.Children.Add(new Separator());

        // Library & Cache
        panel.Children.Add(CreateSectionHeader("LIBRARY & CACHE"));
        panel.Children.Add(CreateEnumComboRow("Grid density", settings.GridDensity, v =>
        {
            settings.GridDensity = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateEnumComboRow("Default sort", settings.LibrarySort, v =>
        {
            settings.LibrarySort = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateEnumComboRow("Default filter", settings.LibraryDefaultFilter, v =>
        {
            settings.LibraryDefaultFilter = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateToggleRow("Auto-repair missing cache", settings.AutoRepairMissingCache, v =>
        {
            settings.AutoRepairMissingCache = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateEnumComboRow("Cache size limit", settings.CacheSizeLimit, v =>
        {
            settings.CacheSizeLimit = v;
            Services.Settings.Save();
        }));
        panel.Children.Add(CreateEnumComboRow("Eviction strategy", settings.EvictionStrategy, v =>
        {
            settings.EvictionStrategy = v;
            Services.Settings.Save();
        }));

        panel.Children.Add(new Separator());

        // Advanced actions
        panel.Children.Add(CreateSectionHeader("ADVANCED"));
        var openDataBtn = new Button { Content = "Open Data Folder", Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 6) };
        openDataBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.RootDir) { UseShellExecute = true });
            }
            catch { }
        };
        panel.Children.Add(openDataBtn);

        var resetBtn = new Button { Content = "Reset Settings", Style = (Style)FindResource("DangerButton"), Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
        resetBtn.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(this,
                "Reset all settings to defaults?",
                "Reset Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                Services.Settings!.Reset();
                _app.RebindHotkey(new HotkeyBinding { ModifierValue = 0, VirtualKey = 0x71, KeyName = "F2" });
                 RefreshSettingsPage();
            }
        };
        panel.Children.Add(resetBtn);

        var hungryBtn = new Button
        {
            Content = "I'm hungry, man",
            Style = (Style)FindResource("GhostButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 4),
            Height = 28
        };
        hungryBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://www.doordash.com/store/dunya-fresh-halal-food-orange-park-713826/1005271/") { UseShellExecute = true });
            }
            catch { };
        };
        panel.Children.Add(hungryBtn);

        panel.Children.Add(new TextBlock
        {
            Text = $"SQLite Schema {SQLiteSchemaVersion.CurrentExpectedSchema}\nStandalone desktop overlay. No game injection or memory access.",
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)FindResource("TextMutedBrush")
        });

        return panel;
    }

    private FrameworkElement CreateSectionHeader(string text)
{
    var stack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
    var indicator = new Rectangle
    {
        Width = 3,
        Height = 24,
        Fill = (Brush)FindResource("AccentBrush"),
        Margin = new Thickness(0, 0, 8, 0)
    };
    var label = new TextBlock
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("TextPrimaryBrush"),
        VerticalAlignment = VerticalAlignment.Center
    };
    stack.Children.Add(indicator);
    stack.Children.Add(label);
    return stack;
}

    private FrameworkElement CreateInfoRow(string text, double fontSize = 11.5)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    /// <summary>Color-coded status for the Games page, showing one of three states.</summary>
    private (string Text, Brush Fg, Brush Bg) ComputeGameStatus(CounterStrikeConnectionPhase phase, AppSettings settings)
    {
        var success = (Brush)FindResource("SuccessBrush");
        var successSoft = (Brush)FindResource("SuccessSoftBrush");
        var accent = (Brush)FindResource("AccentBrush");
        var accentSoft = (Brush)FindResource("AccentSoftBrush");
        var danger = new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x86));
        var dangerSoft = new SolidColorBrush(Color.FromRgb(0x3A, 0x1E, 0x24));
        var warn = new SolidColorBrush(Color.FromRgb(0xD8, 0xAE, 0x63));
        var warnSoft = new SolidColorBrush(Color.FromRgb(0x33, 0x28, 0x17));

        // Map the canonical phase to one of three UI states.
        bool connected = phase is CounterStrikeConnectionPhase.ReceivingGameState;
        bool initializing = phase is CounterStrikeConnectionPhase.WaitingForCs2
                            or CounterStrikeConnectionPhase.Cs2Running
                            or CounterStrikeConnectionPhase.WaitingForGsi;
        bool disconnected = phase is CounterStrikeConnectionPhase.Disabled
                            or CounterStrikeConnectionPhase.Error
                            or CounterStrikeConnectionPhase.ConfigurationMissing
                            or CounterStrikeConnectionPhase.ConfigurationInvalid
                            or CounterStrikeConnectionPhase.PortConflict;

        if (connected)
            return ("Connected — live game state flowing", success, successSoft);
        if (initializing)
            return ("Initializing — listening for CS2…", accent, accentSoft);
        return ("Disconnected", warn, warnSoft);
    }

    private FrameworkElement CreateStatusPill(string text, Brush fg, Brush bg)
    {
        return new Border
        {
            Background = bg,
            BorderBrush = fg,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 430,
            MinHeight = 26,
            Margin = new Thickness(12, 0, 12, 10),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = fg
            }
        };
    }

    private Border CreateCard(int vPadding)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("ElevatedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        return card;
    }

    private FrameworkElement CreatePositionPicker(AppSettings settings, Action<ImagePosition> onChange)
    {
        var positions = new[]
        {
            (Models.ImagePosition.TopLeft,     "↖ Top Left"),
            (Models.ImagePosition.TopCenter,   "↑ Top Center"),
            (Models.ImagePosition.TopRight,    "↗ Top Right"),
            (Models.ImagePosition.CenterLeft,  "← Center Left"),
            (Models.ImagePosition.Center,      "● Center"),
            (Models.ImagePosition.CenterRight, "→ Center Right"),
            (Models.ImagePosition.BottomLeft,  "↙ Bottom Left"),
            (Models.ImagePosition.BottomCenter,"↓ Bottom Center"),
            (Models.ImagePosition.BottomRight, "↘ Bottom Right"),
        };

        var grid = new UniformGrid { Columns = 3, Rows = 3, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (pos, label) in positions)
        {
            var isActive = settings.ImagePosition == pos;
            var btn = new Button
            {
                Content = label,
                FontSize = 10.5,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Padding = new Thickness(4, 6, 4, 6),
                BorderThickness = new Thickness(isActive ? 2 : 1),
                BorderBrush = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush"),
                Background = isActive ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SecondarySurfaceBrush"),
                Foreground = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextSecondaryBrush"),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                settings.ImagePosition = pos;
                Services.Settings.Save();
                onChange(pos);
            };
            grid.Children.Add(btn);
        }

        var extra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        extra.Children.Add(new TextBlock { Text = "Extra:", FontSize = 9.5, Foreground = (Brush)FindResource("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        foreach (var (pos, label) in new[]
        {
            (Models.ImagePosition.CursorPosition, "Cursor"),
            (Models.ImagePosition.ActiveMonitorCenter, "Monitor Center"),
            (Models.ImagePosition.Custom, "Custom")
        })
        {
            var isActive = settings.ImagePosition == pos;
            var btn = new Button
            {
                Content = label,
                FontSize = 9.5,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Padding = new Thickness(6, 3, 6, 3),
                BorderThickness = new Thickness(isActive ? 2 : 1),
                BorderBrush = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush"),
                Background = isActive ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SecondarySurfaceBrush"),
                Foreground = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextSecondaryBrush"),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btn.Click += (_, _) =>
            {
                settings.ImagePosition = pos;
                Services.Settings.Save();
                onChange(pos);
            };
            extra.Children.Add(btn);
        }

        var container = new StackPanel();
        container.Children.Add(grid);
        container.Children.Add(extra);
        return container;
    }

    private FrameworkElement CreateButtonRow(string label, Action onClick, int topMargin = 4, int bottomMargin = 4)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = label,
            Margin = new Thickness(0, topMargin, 0, bottomMargin),
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

private FrameworkElement CreatePresetGrid(AppSettings settings)
{
    var outer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 4) };

    var grid = new UniformGrid
    {
        Columns = 2,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    var presets = new[]
    {
        (Models.CelebrationPreset.ClassicJoseph,      "Classic Joseph",      "Pop + Fade\nNormal",          Colors.White),
        (Models.CelebrationPreset.ITWizard,            "IT Wizard",           "Scale In + Impact\nStrong",    Colors.Cyan),
        (Models.CelebrationPreset.ShawarmaMode,        "Shawarma Mode",       "Bounce + Bounce\nStrong",      Colors.Orange),
        (Models.CelebrationPreset.CivicDeployment,     "Civic Deployment",    "Slide Right + Stamp\nStrong",  Colors.Red),
        (Models.CelebrationPreset.MassageChairRecovery,"Massage Chair",       "Fade + Fade\nSubtle",          Colors.LightGreen),
        (Models.CelebrationPreset.PrinterBossFight,    "Printer Boss Fight",  "Shake + Impact\nUnhinged",     Colors.DarkRed),
        (Models.CelebrationPreset.MaximumNaddaf,       "Maximum Naddaf",      "Jumpscare + Stamp\nUnhinged",  Colors.Magenta),
        (Models.CelebrationPreset.CompletelyRandom,    "Completely Random",   "Random + Random\nNormal",      Colors.LightYellow)
    };

    foreach (var (preset, label, desc, accent) in presets)
    {
        var isActive = settings.Preset == preset;
        var card = new Border
        {
            Tag = preset,
            Margin = new Thickness(3),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(8),
            BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)) : new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4B)),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            Background = isActive ? new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x24)) : new SolidColorBrush(Color.FromRgb(0x25, 0x29, 0x32))
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var title = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = isActive ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)) : new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 3)
        };
        var detail = new TextBlock
        {
            Text = desc,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(title);
        stack.Children.Add(detail);
        card.Child = stack;

        card.MouseLeftButtonDown += (_, _) =>
        {
            settings.Preset = preset;
            Services.Settings.Save();
            RefreshSettingsPage();
        };

        grid.Children.Add(card);
    }

    outer.Children.Add(grid);

    var note = new TextBlock
    {
        Text = "Tip: use Completely Random for maximum chaos, or pick a preset and tweak FX Intensity below.",
        FontSize = 10.5,
        Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)),
        Margin = new Thickness(4, 8, 4, 0),
        TextWrapping = TextWrapping.Wrap
    };
    outer.Children.Add(note);

    // FX Intensity selector (works with any preset)
    var intensityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 4, 0) };
    var intensityLabel = new TextBlock
    {
        Text = "Intensity:",
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0)
    };
    intensityRow.Children.Add(intensityLabel);

    var intensityOptions = new[] { "Subtle", "Normal", "Strong", "Unhinged" };
    var currentIntensity = settings.FxIntensity.ToString();
    foreach (var opt in intensityOptions)
    {
        var isActive = settings.FxIntensity.ToString() == opt;
        var btn = new System.Windows.Controls.Button
        {
            Content = opt,
            FontSize = 10.5,
            FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(2, 0, 2, 0),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)) : new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4B)),
            Background = isActive ? new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x24)) : new SolidColorBrush(Color.FromRgb(0x25, 0x29, 0x32)),
            Foreground = isActive ? new SolidColorBrush(Color.FromRgb(0x5F, 0xB9, 0x8A)) : new SolidColorBrush(Colors.White),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) =>
        {
            settings.FxIntensity = Enum.Parse<Models.FxIntensity>(opt);
            Services.Settings.Save();
            RefreshSettingsPage();
        };
        intensityRow.Children.Add(btn);
    }

    outer.Children.Add(intensityRow);

    // Text controls — compact row
    var textRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 0) };
    textRow.Children.Add(CreateComboRow("Text", DisplayNames.GetMembers<Models.TextMode>().Select(m => m.Friendly), DisplayNames.GetFriendlyName(settings.TextMode), v =>
    {
        var members = DisplayNames.GetMembers<Models.TextMode>();
        var idx = members.FindIndex(m => m.Friendly == v);
        if (idx >= 0)
        {
            settings.TextMode = Enum.Parse<Models.TextMode>(members[idx].Raw);
            Services.Settings.Save();
        }
    }));
    textRow.Children.Add(CreateComboRow("Text FX", DisplayNames.GetMembers<Models.TextFxStyle>().Select(m => m.Friendly), DisplayNames.GetFriendlyName(settings.TextFx), v =>
    {
        var members = DisplayNames.GetMembers<Models.TextFxStyle>();
        var idx = members.FindIndex(m => m.Friendly == v);
        if (idx >= 0)
        {
            settings.TextFx = Enum.Parse<Models.TextFxStyle>(members[idx].Raw);
            Services.Settings.Save();
        }
    }));
    outer.Children.Add(textRow);

    return outer;
}

    private FrameworkElement CreateComboRow(string label, IEnumerable<string> items, string selected, Action<string> onChange, int labelWidth = 140)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(CreateFieldLabel(label));
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var list = items.ToList();
        foreach (var it in list)
        {
            combo.Items.Add(it);
        }
        var idx = list.FindIndex(i => i == selected);
        if (idx >= 0)
        {
            combo.SelectedIndex = idx;
        }
        combo.SelectionChanged += (_, e) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < list.Count)
            {
                onChange(list[combo.SelectedIndex]);
            }
        };
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        return grid;
    }

    private FrameworkElement CreateEnumComboRow<TEnum>(string label, TEnum current, Action<TEnum> onChange, int labelWidth = 140)
        where TEnum : struct, Enum
    {
        var t = typeof(TEnum);
        if (!t.IsEnum) return null!;
        var members = DisplayNames.GetMembers<TEnum>();
        var friendlyList = members.Select(m => m.Friendly).ToList();
        var selectedFriendly = DisplayNames.GetFriendlyName(current);
        return CreateComboRow(label, friendlyList, selectedFriendly, s =>
        {
            var idx = friendlyList.FindIndex(f => f == s);
            if (idx >= 0 && idx < members.Count)
            {
                onChange((TEnum)Enum.Parse(t, s));
            }
        }, labelWidth);
    }

    private FrameworkElement CreateToggleRow(string label, bool isChecked, Action<bool> onChange, int labelWidth = 140)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelTb = CreateFieldLabel(label);
        labelTb.TextWrapping = TextWrapping.Wrap;
        labelTb.TextTrimming = TextTrimming.None;
        Grid.SetColumn(labelTb, 0);
        grid.Children.Add(labelTb);
        var toggle = new CheckBox { Style = (Style)FindResource("ToggleSwitch"), IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    private FrameworkElement CreateSliderRow(string label, double min, double max, double value, Action<double> onChange, int labelWidth = 140, int topMargin = 4, Func<double, string>? format = null)
    {
        var grid = new Grid { Margin = new Thickness(0, topMargin, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(CreateFieldLabel(label));
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = false,
            LargeChange = (max - min) / 10
        };
        slider.ValueChanged += (_, e) => onChange(e.NewValue);

        if (format is null)
        {
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);
            return grid;
        }

        var valueHost = new Grid();
        valueHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        valueHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        valueHost.Children.Add(slider);
        var valueLabel = new TextBlock
        {
            Text = format(value),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            MinWidth = 46,
            TextAlignment = System.Windows.TextAlignment.Right
        };
        slider.ValueChanged += (_, e) => valueLabel.Text = format(e.NewValue);
        Grid.SetColumn(valueLabel, 1);
        valueHost.Children.Add(valueLabel);
        Grid.SetColumn(valueHost, 1);
        grid.Children.Add(valueHost);
        return grid;
    }

    private static TextBlock CreateFieldLabel(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x97, 0xA1)),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 0);
        return tb;
    }

    // =================================================================
    // HOTKEY DIALOG
    // =================================================================

    private void OpenHotkeyDialog(TextBlock displayBox, HotkeyAction action, HotkeyBinding initialBinding)
    {
        var win = new Window
        {
            Title = "Rebind Hotkey",
            Width = 360,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("BgBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = "Press the new hotkey combination.",
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        });

        var currentText = new TextBlock
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 16)
        };
        panel.Children.Add(currentText);

        var hintText = new TextBlock
        {
            Text = "Press Escape to cancel.",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(hintText);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };

        var clearBtn = new Button
        {
            Content = "Clear",
            Style = (Style)FindResource("SecondaryButton"),
            FontSize = 11,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0)
        };
        clearBtn.Click += (_, _) =>
        {
            var emptyBinding = new HotkeyBinding();
            _app.RebindHotkey(action, emptyBinding);
            displayBox.Text = "Not assigned";
            UpdateStatusBar();
            win.Close();
        };
        buttonRow.Children.Add(clearBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Style = (Style)FindResource("SecondaryButton"),
            FontSize = 11,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(8, 0, 0, 0)
        };
        cancelBtn.Click += (_, _) => win.Close();
        buttonRow.Children.Add(cancelBtn);

        panel.Children.Add(buttonRow);

        win.Content = panel;
        currentText.Text = HotkeyConverter.GetBindingDisplayName(
            initialBinding.ModifierValue, initialBinding.VirtualKey, initialBinding.KeyName);

        win.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key;

            if (key == Key.Escape)
            {
                win.Close();
                e.Handled = true;
                return;
            }
            if (HotkeyConverter.IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            var binding = HotkeyConverter.BuildBinding(key, Keyboard.Modifiers);
            if (binding.VirtualKey == 0 && binding.ModifierValue == 0)
            {
                e.Handled = true;
                return;
            }

            currentText.Text = binding.DisplayName;

            _app.RebindHotkey(action, binding);
            displayBox.Text = binding.DisplayName;
            UpdateStatusBar();
            win.Close();
            e.Handled = true;
        };

        win.ShowDialog();
    }

    // =================================================================
    // WINDOW CLOSING
    // =================================================================

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        var settings = Services.Settings!.Current;
        if (settings.CloseToTray && !_appIsExiting)
        {
            e.Cancel = true;
            _app.HideMainWindowToTray();
        }
        else
        {
            _app.ExitApplication();
        }
    }

    private bool _appIsExiting;

    internal void NoteExitRequested()
    {
        _appIsExiting = true;
    }

    // =================================================================
    // IMPORT
    // =================================================================

    private void ImportSingle()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Joseph",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tiff)|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tiff|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            var result = Services.Library!.ImportFile(dialog.FileName);
            if (result.Imported == 1)
            {
                ShowLibraryView();
            }
            else if (result.Skipped > 0)
            {
                MessageBox.Show(this, "That Joseph was already imported (duplicate).", "The Joseph Experience 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "The file could not be imported.", "The Joseph Experience 2.0", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void CheckForUpdatesClicked(object sender, RoutedEventArgs e)
        {
            ((Button)sender).IsEnabled = false;

            try
            {
                var versionService = new VersionService();
                var updateService = new UpdateService(
                    versionService,
                    new HttpUpdateProvider(Services.Settings!.Current.UpdateManifestUrl ?? ""),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     AppContext.BaseDirectory);

                var checkResult = await updateService.CheckForUpdatesAsync();

                ((Button)sender).IsEnabled = true;

                _lastUpdateCheckResult = checkResult;

                if (checkResult.Result == UpdateCheckResult.UpdateAvailable && checkResult.RemoteVersion is not null)
                {
                    ShowUpdateDialog(checkResult, updateService);
                }
                else if (checkResult.Result == UpdateCheckResult.UpToDate)
                {
                    MessageBox.Show(this,
                        "You're up to date.\n\n" +
                        $"The Joseph Experience 2.0 {updateService.CurrentVersionString} is the latest version.",
                        "The Joseph Experience 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ((Button)sender).IsEnabled = true;
                MessageBox.Show(this, $"Update check failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowUpdateDialog(UpdateCheckResultData result, UpdateService updateService)
        {
            var notes = result.ReleaseNotes;
            var notesText = string.IsNullOrWhiteSpace(notes)
                ? "No release notes provided."
                : notes;

            var dialogHeight = Math.Max(280, Math.Min(480, 180 + notesText.Split('\n').Length * 18));

            var win = new Window
            {
                Title = "Update Available",
                Width = 420,
                Height = dialogHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "Update Available",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Version {result.RemoteVersion}",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(CreateSectionHeader("WHAT'S NEW"));

            var notesBox = new TextBox
            {
                Text = notesText,
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas, Monospace"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Background = (Brush)FindResource("ElevatedBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 16),
                MaxHeight = 260,
                AcceptsReturn = true
            };

            panel.Children.Add(notesBox);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
            buttonRow.HorizontalAlignment = HorizontalAlignment.Right;

            var laterBtn = new Button
            {
                Content = "Later",
                Style = (Style)FindResource("SecondaryButton"),
                FontSize = 11.5,
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0)
            };
            laterBtn.Click += (_, _) => win.Close();
            buttonRow.Children.Add(laterBtn);

            var updateNowBtn = new Button
            {
                Content = "Update Now",
                Style = (Style)FindResource("PrimaryButton"),
                FontSize = 11.5,
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0)
            };
            updateNowBtn.Click += async (_, _) =>
            {
                updateNowBtn.IsEnabled = false;
                laterBtn.IsEnabled = false;
                win.Close();

                await updateService.DownloadUpdateAsync(
                    result.DownloadUrl ?? "",
                    result.Sha256 ?? "",
                    CancellationToken.None);

                // After download+verify, prepare and launch installer
                var extractedPath = updateService.ValidateAndExtractStagedPackage();
                if (extractedPath is not null)
                {
                    var backupPath = updateService.PrepareInstall();
                    var targetExe = backupPath?.Replace(".old", "");
                    var newExe = System.IO.Path.Combine(extractedPath, "TheJosephExperience.exe");
                    updateService.LaunchUpdaterHelper(Environment.ProcessId, targetExe ?? "", newExe);
                    _app.Shutdown();
                }
                else
                {
                    MessageBox.Show("Update failed to extract. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            buttonRow.Children.Add(updateNowBtn);

            panel.Children.Add(buttonRow);
            win.Content = panel;
            win.ShowDialog();
        }

        private void ShowReleaseNotesDialog(UpdateCheckResultData result)
        {
            var notes = result.ReleaseNotes;
            var notesText = string.IsNullOrWhiteSpace(notes)
                ? "No release notes provided."
                : notes;

            var win = new Window
            {
                Title = $"Release Notes — v{result.RemoteVersion}",
                Width = 420,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("BgBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = $"Version {result.RemoteVersion}",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 12)
            });

            var notesBox = new TextBox
            {
                Text = notesText,
                FontSize = 11.5,
                FontFamily = new FontFamily("Consolas, Monospace"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Background = (Brush)FindResource("ElevatedBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 0),
                AcceptsReturn = true
            };
            panel.Children.Add(notesBox);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttonRow.HorizontalAlignment = HorizontalAlignment.Right;

            var closeBtn = new Button
            {
                Content = "Close",
                Style = (Style)FindResource("SecondaryButton"),
                FontSize = 11.5,
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0)
            };
            closeBtn.Click += (_, _) => win.Close();
            buttonRow.Children.Add(closeBtn);

            var viewOnlineBtn = new Button
            {
                Content = "View Full Release Notes",
                Style = (Style)FindResource("SecondaryButton"),
                FontSize = 11.5,
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0)
            };
            viewOnlineBtn.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/Kilo-Org/JosephExperience/releases",
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            buttonRow.Children.Add(viewOnlineBtn);

            panel.Children.Add(buttonRow);
            win.Content = panel;
            win.ShowDialog();
        }
    }
