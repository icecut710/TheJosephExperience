using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Services;

/// <summary>
/// Result of a hotkey registration attempt.
/// </summary>
public enum RegistrationResult
{
    /// <summary>Hotkey successfully registered.</summary>
    Success,
    /// <summary>No key selected (binding was empty).</summary>
    NoKeySelected,
    /// <summary>Windows rejected the hotkey registration (with Win32 error code).</summary>
    FailedWin32Error,
    /// <summary>The shortcut is already in use by Windows or another application.</summary>
    AlreadyInUse,
    /// <summary>The hotkey message host is unavailable.</summary>
    HostUnavailable
}

/// <summary>
/// Identifies which registered hotkey fired.
/// </summary>
public enum HotkeyAction
{
    Celebration,
    SecondaryCelebration,
    CycleSound,
    StopAudio,
    None
}

/// <summary>
/// Maps a registered hotkey ID to its action and display name.
/// </summary>
public class HotkeyRegistration
{
    public int Id { get; init; }
    public HotkeyAction Action { get; init; }
    public string Name { get; init; } = "";
    public HotkeyBinding Binding { get; init; } = new();
    public RegistrationResult Result { get; set; } = RegistrationResult.NoKeySelected;
    public int? ErrorCode { get; set; }
}

/// <summary>
/// Result of a collision check between two hotkeys.
/// </summary>
public class CollisionResult
{
    public bool HasCollision { get; init; }
    public HotkeyAction? ExistingAction { get; init; }
    public HotkeyAction? NewAction { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Manages system-wide hotkeys with support for multiple simultaneous bindings.
/// Each binding is identified by a unique ID and fires a typed action.
/// </summary>
public class HotkeyService : IDisposable
{
    private const int IdStep = 0x4A4F5345; // "JOSE" base

    private readonly Dictionary<HotkeyAction, HotkeyRegistration> _registrations = new();
    private readonly Dictionary<int, HotkeyAction> _idToAction = new();
    private HwndSource? _source;
    private bool _disposed;

    /// <summary>
    /// Fired when any registered hotkey is pressed.
    /// Provides the action that was triggered.
    /// </summary>
    public event Action<HotkeyAction>? HotkeyPressed;

    /// <summary>
    /// Fired when registration status changes for any hotkey.
    /// </summary>
    public event Action<HotkeyAction, RegistrationResult, int?>? RegistrationChanged;

    public bool IsHostAvailable => _source is not null && _source.Handle != IntPtr.Zero;

    public HotkeyService()
    {
        var params_ = new HwndSourceParameters("JosephExperienceHotkeyHost")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            ParentWindow = IntPtr.Zero,
            UsesPerPixelOpacity = false
        };
        _source = new HwndSource(params_);
        _source.AddHook(WndProc);
        AppLog.Info("HotkeyService: message host created.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_idToAction.TryGetValue(id, out var action))
            {
                AppLog.Info($"Hotkey: WM_HOTKEY received (id={id}, action={action}). Triggering.");
                HotkeyPressed?.Invoke(action);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Generates a unique hotkey ID for the given action.
    /// </summary>
    private int GetIdForAction(HotkeyAction action) =>
        IdStep + (int)action * 0x100;

    /// <summary>
    /// Checks whether the given binding collides with any currently registered hotkey.
    /// </summary>
    public CollisionResult CheckCollision(HotkeyAction action, HotkeyBinding binding)
    {
        if (binding.IsEmpty)
            return new CollisionResult { HasCollision = false };

        foreach (var reg in _registrations.Values)
        {
            if (reg.Action == action)
                continue;

            if (reg.Binding.VirtualKey == binding.VirtualKey &&
                reg.Binding.ModifierValue == binding.ModifierValue)
            {
                return new CollisionResult
                {
                    HasCollision = true,
                    ExistingAction = reg.Action,
                    NewAction = action,
                    Message = $"{binding.DisplayName} conflicts with {reg.Name} ({reg.Action})."
                };
            }
        }

        return new CollisionResult { HasCollision = false };
    }

    /// <summary>
    /// Registers a hotkey for the given action. Returns the registration result.
    /// Unregisters any previous binding for the same action.
    /// </summary>
    public RegistrationResult Register(HotkeyAction action, string name, HotkeyBinding binding)
    {
        Unregister(action);

        var id = GetIdForAction(action);
        var registration = new HotkeyRegistration
        {
            Id = id,
            Action = action,
            Name = name,
            Binding = binding,
            Result = RegistrationResult.NoKeySelected
        };

        if (binding.IsEmpty)
        {
            _registrations[action] = registration;
            RegistrationChanged?.Invoke(action, RegistrationResult.NoKeySelected, null);
            return RegistrationResult.NoKeySelected;
        }

        var handle = _source?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            registration.Result = RegistrationResult.HostUnavailable;
            registration.ErrorCode = -1;
            _registrations[action] = registration;
            RegistrationChanged?.Invoke(action, RegistrationResult.HostUnavailable, -1);
            return RegistrationResult.HostUnavailable;
        }

        var collision = CheckCollision(action, binding);
        if (collision.HasCollision)
        {
            registration.Result = RegistrationResult.AlreadyInUse;
            registration.ErrorCode = null;
            _registrations[action] = registration;
            AppLog.Warn($"Hotkey collision: {collision.Message}");
            RegistrationChanged?.Invoke(action, RegistrationResult.AlreadyInUse, null);
            return RegistrationResult.AlreadyInUse;
        }

        var fsModifiers = binding.ModifierValue | NativeMethods.MOD_NOREPEAT;
        var success = NativeMethods.RegisterHotKey(handle, id, fsModifiers, binding.VirtualKey);

        if (!success)
        {
            var code = Marshal.GetLastWin32Error();
            registration.ErrorCode = code;
            if (code == 1409)
            {
                registration.Result = RegistrationResult.AlreadyInUse;
            }
            else
            {
                registration.Result = RegistrationResult.FailedWin32Error;
            }
            AppLog.Warn($"Hotkey: RegisterHotKey failed for {action}, Win32 error {code}");
        }
        else
        {
            registration.Result = RegistrationResult.Success;
            _idToAction[id] = action;
            AppLog.Info($"Hotkey: registered {binding.DisplayName} for {action} (id=0x{id:X})");
        }

        _registrations[action] = registration;
        RegistrationChanged?.Invoke(action, registration.Result, registration.ErrorCode);
        return registration.Result;
    }

    /// <summary>
    /// Convenience method to register with a display name derived from the action.
    /// </summary>
    public RegistrationResult Register(HotkeyAction action, HotkeyBinding binding)
    {
        var name = action switch
        {
            HotkeyAction.Celebration => "Celebration (F2)",
            HotkeyAction.SecondaryCelebration => "Secondary Celebration (Shift+3)",
            HotkeyAction.CycleSound => "Cycle Sound (F8)",
            HotkeyAction.StopAudio => "Stop Audio",
            _ => action.ToString()
        };
        return Register(action, name, binding);
    }

    /// <summary>
    /// Removes a registration for the given action but does not unregister others.
    /// </summary>
    public void Unregister(HotkeyAction action)
    {
        if (!_registrations.TryGetValue(action, out var reg))
            return;

        if (reg.Result == RegistrationResult.Success)
        {
            var handle = _source?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(handle, reg.Id);
            }
        }

        _registrations.Remove(action);
        _idToAction.Remove(reg.Id);
        reg.Result = RegistrationResult.NoKeySelected;
        RegistrationChanged?.Invoke(action, RegistrationResult.NoKeySelected, null);
    }

    /// <summary>
    /// Unregisters all hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var action in _registrations.Keys.ToArray())
        {
            Unregister(action);
        }
    }

    /// <summary>
    /// Gets the registration status for a given action.
    /// </summary>
    public HotkeyRegistration? GetRegistration(HotkeyAction action) =>
        _registrations.TryGetValue(action, out var reg) ? reg : null;

    /// <summary>
    /// Gets all current registrations.
    /// </summary>
    public IReadOnlyList<HotkeyRegistration> GetAllRegistrations() =>
        _registrations.Values.ToList().AsReadOnly();

    /// <summary>
    /// Gets the display name for a registration result.
    /// </summary>
    public static string GetResultMessage(RegistrationResult result, int? errorCode, HotkeyAction action)
    {
        return result switch
        {
            RegistrationResult.Success => "Active",
            RegistrationResult.NoKeySelected => "Not set",
            RegistrationResult.FailedWin32Error => $"Failed (error {errorCode})",
            RegistrationResult.AlreadyInUse => action != HotkeyAction.None
                ? $"Unavailable • {action} in use by another app"
                : "Unavailable • Hotkey in use by another app",
            RegistrationResult.HostUnavailable => "Unavailable • Hotkey host error",
            _ => "Unknown"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
