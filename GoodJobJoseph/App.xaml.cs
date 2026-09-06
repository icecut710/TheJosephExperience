using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using JosephExperience.Data;
using JosephExperience.Models;
using JosephExperience.Services;
using JosephExperience.Services.CounterStrike;
using JosephExperience.Utilities;
using JosephExperience.Views;

namespace JosephExperience;

public partial class App : Application
{
    public static RollingFileLogger Logger = null!;
    public static AppSettingsService? Services;
    public static readonly string Version = "2.0.3";

    private DatabaseService? _db;
    private SettingsService? _settings;
    private ImageLibraryService? _library;
    private HistoryService? _history;
    private SoundLibraryService? _soundLibrary;
    private HotkeyService? _hotkey;
    private OverlayService? _overlay;
    private SupabaseService? _supabase;
    private CelebrationService? _celebration;
    private AudioService? _audio;
    private TrayService? _tray;
    private CounterStrikeIntegrationService? _cs2;
    private RealNaddService? _naddService;
    private MainWindow? _mainWindow;
    private bool _firstRun;
    private bool _exiting;
    private System.IO.Pipes.NamedPipeServerStream? _ipcServer;
    private System.Threading.CancellationTokenSource? _ipcCts;
    private Mutex? _mutex;

protected override void OnStartup(StartupEventArgs e)
        {
            // ---- Single-instance guard: must happen BEFORE any initialization ----
            const string mutexName = "JosephExperience.SingleInstance.5A3B7C9D";
            _mutex = new Mutex(true, mutexName, out var createdNew);

            if (!createdNew)
            {
                // Another instance is already running. Signal it to show its window
                // and then exit. We use a named pipe so the primary instance can
                // receive the signal without requiring the secondary to fully start.
                try
                {
using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                         ".", "JosephExperience.Ipc.ShowWindow", System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.None);
                    pipe.Connect(1000);
                    using var writer = new System.IO.StreamWriter(pipe);
                    writer.Write("SHOW_WINDOW");
                    writer.Flush();
                }
                catch
                {
                    // If IPC fails, just fall through to below; the primary will
                    // handle showing its window via the mutex release pattern.
                }

                // Exit the secondary instance immediately — do NOT initialize services,
                // do NOT create a tray icon, do NOT register hotkeys.
                Shutdown();
                return;
            }

            // Mutex acquired successfully — this is the primary instance.
            // The mutex field (_mutex) stays alive for the entire app lifetime
            // and is released in ExitApplication.

            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            Logger = new RollingFileLogger(AppPaths.LogsDir);
            AppLog.Initialize(Logger);
            Logger.Info("Starting The Joseph Experience 2.0 v2.0.0");

            try
            {
                InitializeCore();
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal error during initialization", ex);
                MessageBox.Show(
                    "The Joseph Experience 2.0 failed to start.\n\n" + ex.Message,
                    "The Joseph Experience 2.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            CleanupCorruptedOrDummyFiles();
            _firstRun = !File.Exists(AppPaths.DatabasePath) || _db!.GetAllImages().Count == 0;
            EnsureOriginalJoseph();

            // Run SQLite schema migrations if needed
            var dbPath = AppPaths.DatabasePath;
            if (File.Exists(dbPath))
            {
                var migrationResult = SchemaMigrationManager.RunMigrations(dbPath);
                if (migrationResult)
                {
                    var newSchema = SQLiteSchemaVersion.ReadFromDatabase(dbPath);
                    Logger.Info($"SQLite schema migrated/verified: {newSchema}");
                }
                else
                {
                    Logger.Warn("SQLite schema migration failed");
                }
            }

            // Single-instance IPC: start named pipe server so secondary launches can
            // signal this primary instance to show its window.
            StartIpcListener();

            // Initialize tray service BEFORE main window — but verify success before hiding.
            _tray = new TrayService();
            _tray.OpenRequested += () => Dispatcher.Invoke(ShowMainWindow);
            _tray.ToggleEnabledRequested += () => Dispatcher.Invoke(() =>
            {
                _settings!.Current.Enabled = !_settings.Current.Enabled;
                _settings.Save();
                _tray.UpdateEnabledState(_settings.Current.Enabled);
                _mainWindow?.RefreshState();
            });
            _tray.TestRequested += () => Dispatcher.Invoke(() => _celebration?.Trigger("tray"));
            _tray.ExitRequested += () => Dispatcher.Invoke(ExitApplication);

            _tray.Show(string.Empty, "The Joseph Experience 2.0");
            _tray.UpdateEnabledState(_settings!.Current.Enabled);

            // Create main window AFTER tray is set up so OpenRequested can target it.
            _mainWindow = new MainWindow(this);
            MainWindow = _mainWindow;

            // Wire up real NADD market data updates to the status bar
            _naddService!.DataUpdated += () => Dispatcher.Invoke(() => _mainWindow.UpdateNaddPrice());

            RegisterHotkey();

            // Apply startup behavior: hide to tray only if user has minimize-to-tray
            if (_settings!.Current.MinimizeToTray)
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
            }

            StartGameIntegration();

            Logger.Info("Application ready.");
            RunStartupIntegrityScan();
            StartBackgroundSync();
            ConfigurePeriodicSync();
        }

    private void RunStartupIntegrityScan()
    {
        try
        {
            var library = _library;
            if (library is null) return;
            Task.Run(() =>
            {
                var scan = library.RunIntegrityScan();
                Logger.Info($"Integrity scan: total={scan.Total}, ready={scan.Ready}, missing={scan.MissingCache}, broken={scan.Broken}, stale={scan.Stale}, localOnly={scan.LocalOnly}");
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Integrity scan failed: {ex.Message}");
        }
    }

    private void ConfigurePeriodicSync()
    {
        var supabase = _supabase;
        if (supabase is null) return;

        TimeSpan IntervalFor(SyncFrequency freq) => freq switch
        {
            SyncFrequency.Every5Minutes => TimeSpan.FromMinutes(5),
            SyncFrequency.Every15Minutes => TimeSpan.FromMinutes(15),
            SyncFrequency.Every30Minutes => TimeSpan.FromMinutes(30),
            SyncFrequency.Every1Hour => TimeSpan.FromHours(1),
            _ => TimeSpan.MaxValue
        };

        var freq = _settings?.Current.SyncFrequency ?? SyncFrequency.OnStartup;
        if (freq == SyncFrequency.ManualOnly || freq == SyncFrequency.OnStartup)
        {
            return;
        }

        var interval = IntervalFor(freq);
        supabase.ConfigurePeriodicSync(
            () => IntervalFor(_settings?.Current.SyncFrequency ?? SyncFrequency.OnStartup),
            async () =>
            {
                try
                {
                    if (_settings?.Current.SyncFrequency is SyncFrequency.ManualOnly or SyncFrequency.OnStartup)
                    {
                        return;
                    }
                    await SyncNowAsync(null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Periodic sync failed: {ex.Message}");
                }
            });
    }

    private async void StartBackgroundSync()
    {
        try
        {
            await SyncNowAsync(null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Background sync failed: {ex.Message}");
        }
    }

    private void StartIpcListener()
    {
        _ipcCts = new System.Threading.CancellationTokenSource();
        var token = _ipcCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_exiting)
            {
                try
                {
using var server = new System.IO.Pipes.NamedPipeServerStream(
                         "JosephExperience.Ipc.ShowWindow",
                        System.IO.Pipes.PipeDirection.In);
                    _ipcServer = server;

                    await server.WaitForConnectionAsync(token);

                    using var reader = new System.IO.StreamReader(server);
                    var message = await reader.ReadToEndAsync();

                    if (message.Contains("SHOW_WINDOW", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.Invoke(ShowMainWindow);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger?.Error($"IPC listener error", ex);
                }
            }
        }, token);
    }

    private void InitializeCore()
    {
        _settings = new SettingsService(AppPaths.SettingsPath);
        _db = new DatabaseService(AppPaths.DatabasePath);
        _db.Initialize();
        _library = new ImageLibraryService(_db);
        _soundLibrary = new SoundLibraryService(_db);
        _audio = new AudioService(_soundLibrary);
        _overlay = new OverlayService();
        _history = new HistoryService(_settings.Current.CelebrationHistoryLimit);
        _celebration = new CelebrationService(_library, _overlay, _audio, _settings, _history, _soundLibrary, _db, _tray);
        _naddService = new RealNaddService();
        _supabase = new SupabaseService(AppPaths.RootDir);

        Services = new AppSettingsService
        {
            Settings = _settings,
            Database = _db,
            Library = _library,
            SoundLibrary = _soundLibrary,
            Audio = _audio,
            Overlay = _overlay,
            Celebration = _celebration,
            History = _history,
            Supabase = _supabase,
            GameIntegration = () => _cs2,
            NaddService = _naddService
        };
    }

    internal void StartGameIntegration()
    {
        // New Counter-Strike subsystem: GSI loopback listener + state-transition
        // detector + router into the normal CelebrationService. The legacy
        // GameStateIntegrationService listener is no longer started (it would
        // conflict over the same port).
        if (_cs2 is null)
        {
            _cs2 = new CounterStrikeIntegrationService(
                () => _celebration ?? throw new InvalidOperationException("CelebrationService not initialized"),
                () => _settings!.Current);
            _cs2.StateChanged += state => AppLog.Info($"CS2 status: {state}");
        }

        if (_settings?.Current.GameIntegrationEnabled == true)
        {
            _cs2.Start();
        }
        else
        {
            _cs2.Stop();
        }
    }

    /// <summary>The live Counter-Strike integration (listener + detector + router), or null before first start.</summary>
    internal CounterStrikeIntegrationService? Cs2 => _cs2;

    /// <summary>
    /// One-click installer: locates Counter-Strike 2 / CS:GO on this machine via the
    /// Steam registry and writes the gamestate integration cfg directly into the
    /// game's csgo/cfg folder.
    /// </summary>
    internal AttachResult AttachCs2()
    {
        // Ensure the listener is running with current settings.
        StartGameIntegration();
        var svc = _cs2;
        if (svc is null)
            return new AttachResult(false, "Game integration not initialized.", null);

        var s = _settings!.Current;
        var port = Math.Clamp(s.GameIntegrationPort, 1, 65535);
        var token = string.IsNullOrEmpty(s.GameIntegrationAuthToken) ? null : s.GameIntegrationAuthToken;

        var folder = svc.ConfigManager.FindConfigFolder(s.Cs2AttachPath);
        if (folder is not null)
        {
            svc.ConfigManager.MigrateOldConfigs(folder, port, token);
        }

        var result = svc.ConfigManager.InstallOrUpdate(
            s.Cs2AttachPath, port, token);

        if (result.Success)
        {
            s.Cs2AttachedUtc = DateTime.UtcNow;
            _settings.Save();
            // Restart the listener so a changed port/token takes effect immediately.
            svc.Restart();
        }

        return new AttachResult(result.Success, result.Message, result.CfgFolder);
    }

    /// <summary>True when the user has previously attached and the GSI cfg still validates.</summary>
    internal bool IsCs2Attached()
    {
        var s = _settings!.Current;
        if (string.IsNullOrEmpty(s.Cs2AttachPath)) return false;
        var svc = _cs2;
        if (svc is null) return false;
        var folder = svc.ConfigManager.FindConfigFolder(s.Cs2AttachPath);
        if (folder is null) return false;
        return svc.ConfigManager.ValidateConfig(
            System.IO.Path.Combine(folder, svc.ConfigManager.ConfigFileName),
            Math.Clamp(s.GameIntegrationPort, 1, 65535),
            string.IsNullOrEmpty(s.GameIntegrationAuthToken) ? null : s.GameIntegrationAuthToken);
    }

    private void CleanupCorruptedOrDummyFiles()
    {
        try
        {
            if (_db == null) return;
            var all = _db.GetAllImages();
            foreach (var img in all)
            {
                bool isBad = false;
                if (!File.Exists(img.FilePath))
                {
                    isBad = true;
                }
                else
                {
                    try
                    {
                        var fi = new FileInfo(img.FilePath);
                        if (fi.Length < 100 || !ImageLibraryService.LoadImageSourceSafe(img.FilePath))
                        {
                            isBad = true;
                            try { File.Delete(img.FilePath); } catch { }
                        }
                    }
                    catch
                    {
                        isBad = true;
                    }
                }

                if (isBad)
                {
                    Logger.Info($"Purging corrupted or dummy image record {img.Id} ({img.DisplayName})");
                    _db.DeleteImage(img.Id);
                }
            }

            if (Directory.Exists(AppPaths.ImagesDir))
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.ImagesDir))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length < 100 || !ImageLibraryService.LoadImageSourceSafe(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Cleanup of corrupted files failed: {ex.Message}");
        }
    }

    private void EnsureOriginalJoseph()
    {
        var readyImages = _library?.GetAllImages().Where(i => i.IsReady && ImageLibraryService.LoadImageSourceSafe(i.FilePath)).ToList();
        if (readyImages != null && readyImages.Count > 0 && !_firstRun)
        {
            return;
        }

        // Look for the bundled Original Joseph asset in multiple potential locations
        string? candidate = null;
        var potentialDirs = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets"),
            System.IO.Path.Combine(AppPaths.RootDir, "Assets"),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"),
            System.IO.Path.Combine(AppContext.BaseDirectory),
        };

        foreach (var dir in potentialDirs)
        {
            if (Directory.Exists(dir))
            {
                candidate = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => ImageUtilities.IsSupportedImageFile(f) &&
                                         Path.GetFileName(f).Contains("Joseph", StringComparison.OrdinalIgnoreCase));
                if (candidate != null) break;
            }
        }

        if (candidate is null)
        {
            foreach (var dir in potentialDirs)
            {
                if (Directory.Exists(dir))
                {
                    candidate = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => ImageUtilities.IsSupportedImageFile(f));
                    if (candidate != null) break;
                }
            }
        }

        if (candidate is null)
        {
            Logger.Warn("Bundled Original Joseph asset not found; skipping default import.");
            return;
        }

        var result = _library!.ImportFile(candidate, "Original Joseph", "Classic", "original, classic, joseph");
        if (result.Imported == 1 && result.ImportedImage is not null)
        {
            var img = result.ImportedImage;
            img.Favorite = true;
            img.Weight = 1;
            img.Enabled = true;
            _db!.UpdateImage(img);
            Logger.Info("Imported Original Joseph.");
        }
        else if (result.Skipped > 0)
        {
            var existing = _db!.GetAllImages().FirstOrDefault(i => i.DisplayName == "Original Joseph");
            if (existing != null && !existing.Enabled)
            {
                existing.Enabled = true;
                _db.UpdateImage(existing);
            }
        }
        else
        {
            Logger.Warn($"Failed to import Original Joseph: {result.LastError ?? "unknown"}");
        }
    }

    internal void RegisterHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = new HotkeyService();
        _hotkey.HotkeyPressed += (action) => Dispatcher.Invoke(() => HandleHotkey(action));

        var settings = _settings!.Current;
        var binding = HotkeyConverter.Resolve(settings);

        // Migrate legacy F8 default to F2 if needed.
        if (binding.VirtualKey == 0x77 && binding.ModifierValue == 0)
        {
            binding = new Models.HotkeyBinding { VirtualKey = 0x71, KeyName = "F2" };
            settings.HotkeyModifierValue = 0;
            settings.HotkeyVirtualKey = 0x71;
            settings.HotkeyKeyName = "F2";
            settings.HotkeyModifiers = "None";
            settings.HotkeyKey = "F2";
            _settings.Save();
        }

        var result = _hotkey.Register(HotkeyAction.Celebration, "Celebration", binding);
        if (result != RegistrationResult.Success)
        {
            Logger.Error($"Hotkey registration failed: {binding.DisplayName} ({HotkeyService.GetResultMessage(result, _hotkey.GetRegistration(HotkeyAction.Celebration)?.ErrorCode, HotkeyAction.Celebration)})");
            var f2 = new Models.HotkeyBinding { VirtualKey = 0x71, KeyName = "F2" };
            var fallbackResult = _hotkey.Register(HotkeyAction.Celebration, "Celebration", f2);
            if (fallbackResult != RegistrationResult.Success)
            {
                Logger.Warn("Hotkey registration failed after fallback — F2 may be in use by Windows or another app.");
            }
        }

        // Register audio toggle hotkey (F8 by default)
        var audioToggleBinding = settings.AudioToggleHotkey.IsEmpty
            ? new HotkeyBinding { VirtualKey = 0x77, KeyName = "F8" }
            : settings.AudioToggleHotkey;
        var audioResult = _hotkey.Register(HotkeyAction.AudioToggle, "Audio Toggle", audioToggleBinding);
        if (audioResult != RegistrationResult.Success && audioResult != RegistrationResult.NoKeySelected)
        {
            Logger.Warn($"Audio toggle hotkey registration failed: {audioToggleBinding.DisplayName}");
        }

        UpdateStatusIndicator();
    }

    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Celebration:
                _celebration?.Trigger("hotkey");
                break;
            case HotkeyAction.AudioToggle:
                _celebration?.ToggleAudio();
                break;
        }
    }

    internal void RebindHotkey(HotkeyBinding binding)
    {
        _settings!.Current.HotkeyModifierValue = binding.ModifierValue;
        _settings.Current.HotkeyVirtualKey = binding.VirtualKey;
        _settings.Current.HotkeyKeyName = binding.KeyName;
        _settings.Current.HotkeyModifiers = ModifierMaskToString(binding.ModifierValue);
        _settings.Current.HotkeyKey = binding.KeyName;
        _settings.Save();

        var result = _hotkey!.Register(HotkeyAction.Celebration, "Celebration", binding);
        if (result == RegistrationResult.Success)
        {
            Logger.Info($"Hotkey rebound: {binding.DisplayName}");
            _mainWindow?.ShowHotkeySuccess(binding.DisplayName);
        }
        else
        {
            Logger.Error($"Hotkey rebind failed: {HotkeyService.GetResultMessage(result, _hotkey.GetRegistration(HotkeyAction.Celebration)?.ErrorCode, HotkeyAction.Celebration)}");
            _mainWindow?.ShowHotkeyError($"The new shortcut could not be registered.\n\n{HotkeyService.GetResultMessage(result, _hotkey.GetRegistration(HotkeyAction.Celebration)?.ErrorCode, HotkeyAction.Celebration)}", binding);
        }
        UpdateStatusIndicator();
    }

    internal void RebindHotkey(HotkeyAction action, HotkeyBinding binding)
    {
        var collision = _hotkey?.CheckCollision(action, binding);
        if (collision is not null && collision.HasCollision)
        {
            var existingAction = collision.ExistingAction?.ToString() ?? "another action";
            _mainWindow?.ShowHotkeyError(
                $"\"{binding.DisplayName}\" is already in use by {existingAction}.\n\nChoose another shortcut.", binding);
            return;
        }

        var settings = _settings!.Current;
        switch (action)
        {
            case HotkeyAction.Celebration:
                settings.HotkeyModifierValue = binding.ModifierValue;
                settings.HotkeyVirtualKey = binding.VirtualKey;
                settings.HotkeyKeyName = binding.KeyName;
                settings.HotkeyModifiers = ModifierMaskToString(binding.ModifierValue);
                settings.HotkeyKey = binding.KeyName;
                break;
            case HotkeyAction.AudioToggle:
                settings.AudioToggleHotkey = binding;
                break;
        }
        _settings.Save();

        var result = _hotkey!.Register(action, action.ToString(), binding);
        if (result == RegistrationResult.Success)
        {
            Logger.Info($"Hotkey rebound: {action} -> {binding.DisplayName}");
            _mainWindow?.ShowHotkeySuccess($"{action}: {binding.DisplayName}");
        }
        else
        {
            Logger.Error($"Hotkey rebind failed: {HotkeyService.GetResultMessage(result, _hotkey.GetRegistration(action)?.ErrorCode, action)}");
            _mainWindow?.ShowHotkeyError(
                $"The key combination '{binding.DisplayName}' could not be registered.\n\n" +
                $"{HotkeyService.GetResultMessage(result, _hotkey.GetRegistration(action)?.ErrorCode, action)}", binding);
        }
        UpdateStatusIndicator();
    }

    internal void ResetHotkey(HotkeyAction action)
    {
        var binding = HotkeyConverter.GetDefaultBinding(action);
        RebindHotkey(action, binding);
    }

    internal void UpdateStatusIndicator()
    {
        _mainWindow?.RefreshState();
        // Update the hotkey status bar binding via the service's RegistrationChanged event
        if (_hotkey != null)
        {
            foreach (var reg in _hotkey.GetAllRegistrations())
            {
                _ = reg.Result;
                _ = HotkeyService.GetResultMessage(reg.Result, reg.ErrorCode, reg.Action);
            }
        }
    }

    private static string ModifierMaskToString(uint mask)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mask & 0x1) != 0) parts.Add("Alt");
        if ((mask & 0x2) != 0) parts.Add("Ctrl");
        if ((mask & 0x4) != 0) parts.Add("Shift");
        if ((mask & 0x8) != 0) parts.Add("Win");
        return parts.Count == 0 ? "None" : string.Join("+", parts);
    }

    internal void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            Logger?.Warn("ShowMainWindow: MainWindow is null");
            return;
        }
        try
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Logger?.Error("ShowMainWindow: failed to show window", ex);
        }
    }

    internal void HideMainWindowToTray()
    {
        _mainWindow?.Hide();
    }

    internal async Task SyncNowAsync(Action? onComplete)
    {
        var supabase = _supabase;
        var library = _library;
        if (supabase is null || library is null)
        {
            onComplete?.Invoke();
            return;
        }

        Logger.Info("Sync requested.");
        var result = await supabase.SyncAsync(library, AppPaths.CacheDir).ConfigureAwait(true);
        if (result.Success)
        {
            Logger.Info($"Sync complete: downloaded={result.Downloaded}, imported={result.Imported}, skipped={result.Skipped}, failed={result.Failed}");
        }
        else
        {
            Logger.Warn($"Sync failed: {result.Error}");
        }

        onComplete?.Invoke();
        _mainWindow?.RefreshState();
    }

    internal void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        _ipcCts?.Cancel();
        _ipcServer?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mainWindow?.NoteExitRequested();
        Logger!.Info("Shutting down.");
        _hotkey?.Dispose();
        _tray?.Hide();
        _tray?.Dispose();
        _supabase?.Dispose();
        _cs2?.Dispose();
        _audio?.Dispose();
        _naddService?.Dispose();
        _db?.Dispose();
        Logger.Dispose();
        Shutdown(0);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger?.Error("Unhandled dispatcher exception", e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger?.Error("Unhandled app domain exception", ex);
        }
    }
}

    public class AppSettingsService
    {
        public SettingsService? Settings { get; init; }
        public DatabaseService? Database { get; init; }
        public ImageLibraryService? Library { get; init; }
        public SoundLibraryService? SoundLibrary { get; init; }
        public AudioService? Audio { get; init; }
        public OverlayService? Overlay { get; init; }
        public CelebrationService? Celebration { get; init; }
        public HistoryService? History { get; init; }
        public SupabaseService? Supabase { get; init; }
        public Func<CounterStrikeIntegrationService?>? GameIntegration { get; init; }
        public RealNaddService? NaddService { get; init; }
    }
