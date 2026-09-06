using System.Windows.Input;
using System.Windows.Interop;
using JosephExperience.Models;
using JosephExperience.Services;

namespace JosephExperience.Utilities;

/// <summary>
/// Converts between WPF key values and structural HotkeyBinding / Win32 VKs.
///
/// The binding represents physical shortcut semantics: "Shift + 3" is
/// MOD_SHIFT + VK_3 (0x33), never the resulting typed character ('#').
/// </summary>
public static class HotkeyConverter
{
    // Win32 virtual key codes
    private const uint VK_F1 = 0x70;
    private const uint VK_0 = 0x30;
    private const uint VK_NUMPAD0 = 0x60;
    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_SPACE = 0x20;
    private const uint VK_BACK = 0x08;
    private const uint VK_TAB = 0x09;
    private const uint VK_RETURN = 0x0D;
    private const uint VK_DELETE = 0x2E;
    private const uint VK_INSERT = 0x2D;
    private const uint VK_HOME = 0x24;
    private const uint VK_END = 0x23;
    private const uint VK_PRIOR = 0x21;
    private const uint VK_NEXT = 0x22;
    private const uint VK_UP = 0x26;
    private const uint VK_DOWN = 0x28;
    private const uint VK_LEFT = 0x25;
    private const uint VK_RIGHT = 0x27;
    private const uint VK_OEM_PLUS = 0xBB;
    private const uint VK_OEM_MINUS = 0xBD;
    private const uint VK_OEM_PERIOD = 0xBE;
    private const uint VK_OEM_COMMA = 0xBC;

    // Modifier mask bits (subset of NativeMethods, kept local & pure for tests)
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>True if the WPF key is a modifier key itself (never a hotkey).</summary>
    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;
    }

    /// <summary>
    /// Builds a HotkeyBinding from a non-modifier WPF key + the current modifiers.
    /// Pass <paramref name="applyShiftToNumberKeys"/> = true so that a capture of
    /// Shift + D3 yields Shift modifier + VK_3 (physical semantics).
    /// </summary>
    public static HotkeyBinding BuildBinding(Key key, ModifierKeys modifiers)
    {
        return new HotkeyBinding
        {
            ModifierValue = ConvertWpfModifiers(modifiers),
            VirtualKey = KeyToVirtualKey(key, out var name),
            KeyName = name
        };
    }

    public static uint ConvertWpfModifiers(ModifierKeys modifiers)
    {
        uint mask = 0;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) mask |= MOD_CONTROL;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) mask |= MOD_ALT;
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) mask |= MOD_SHIFT;
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) mask |= MOD_WIN;
        return mask;
    }

    /// <summary>
    /// Converts a WPF Key to its Win32 virtual key code and a display name.
    /// Follows physical-key semantics.
    /// </summary>
    public static uint KeyToVirtualKey(Key key, out string name)
    {
        name = "?";
        if (key >= Key.F1 && key <= Key.F24)
        {
            name = key.ToString();
            return VK_F1 + (uint)(key - Key.F1);
        }
        if (key >= Key.D0 && key <= Key.D9)
        {
            name = ((int)key - (int)Key.D0).ToString();
            return VK_0 + (uint)((int)key - (int)Key.D0);
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            name = "Num " + ((int)key - (int)Key.NumPad0);
            return VK_NUMPAD0 + (uint)((int)key - (int)Key.NumPad0);
        }
        if (key >= Key.A && key <= Key.Z)
        {
            name = key.ToString();
            return (uint)key.ToAsciiUpper();
        }

        switch (key)
        {
            case Key.Escape: name = "Esc"; return VK_ESCAPE;
            case Key.Space: name = "Space"; return VK_SPACE;
            case Key.Back: name = "Backspace"; return VK_BACK;
            case Key.Tab: name = "Tab"; return VK_TAB;
            case Key.Enter: name = "Enter"; return VK_RETURN;
            case Key.Delete: name = "Del"; return VK_DELETE;
            case Key.Insert: name = "Ins"; return VK_INSERT;
            case Key.Home: name = "Home"; return VK_HOME;
            case Key.End: name = "End"; return VK_END;
            case Key.PageUp: name = "PgUp"; return VK_PRIOR;
            case Key.PageDown: name = "PgDn"; return VK_NEXT;
            case Key.Up: name = "Up"; return VK_UP;
            case Key.Down: name = "Down"; return VK_DOWN;
            case Key.Left: name = "Left"; return VK_LEFT;
            case Key.Right: name = "Right"; return VK_RIGHT;
            case Key.OemPlus: name = "+"; return VK_OEM_PLUS;
            case Key.OemMinus: name = "-"; return VK_OEM_MINUS;
            case Key.OemPeriod: name = "."; return VK_OEM_PERIOD;
            case Key.OemComma: name = ","; return VK_OEM_COMMA;
            default:
                // Fall back to WPF's own mapping (may return 0 for unlisted keys).
                try
                {
                    var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    if (vk != 0)
                    {
                        name = key.ToString();
                        return vk;
                    }
                }
                catch
                {
                    // fall through
                }
                return 0;
        }
    }

    /// <summary>
    /// Formats a modifier mask + key name into a display string like "Shift + 3" or "F8".
    /// </summary>
    public static string GetBindingDisplayName(uint modifierValue, uint virtualKey, string keyName)
    {
        if (virtualKey == 0) return "None";
        var mods = new List<string>();
        if ((modifierValue & MOD_ALT) != 0) mods.Add("Alt");
        if ((modifierValue & MOD_CONTROL) != 0) mods.Add("Ctrl");
        if ((modifierValue & MOD_SHIFT) != 0) mods.Add("Shift");
        if ((modifierValue & MOD_WIN) != 0) mods.Add("Win");
        return mods.Count == 0 ? keyName : string.Join(" + ", mods) + " + " + keyName;
    }

    /// <summary>
    /// Resolves a modifier mask + key (by name or single char) into a binding,
    /// used when loading a persisted hotkey. Returns null if unresolvable.
    /// </summary>
    public static HotkeyBinding? ParseBinding(string? modifierMask, string? key)
    {
        uint mods = ParseModifiers(modifierMask);
        var (vk, name) = ResolveKeyName(key);
        if (vk == 0 || name is null)
        {
            return null;
        }
        return new HotkeyBinding { ModifierValue = mods, VirtualKey = vk, KeyName = name };
    }

    public static uint ParseModifiers(string? modifierMask)
    {
        uint result = 0;
        if (string.IsNullOrWhiteSpace(modifierMask) || modifierMask == "None")
        {
            return result;
        }
        foreach (var part in modifierMask.Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": result |= MOD_CONTROL; break;
                case "alt": result |= MOD_ALT; break;
                case "shift": result |= MOD_SHIFT; break;
                case "win": case "windows": result |= MOD_WIN; break;
            }
        }
        return result;
    }

    /// <summary>
    /// Resolves the persisted hotkey from settings into a structural binding.
    /// Prefers the structural fields; falls back to parsing the legacy strings.
    /// </summary>
    /// <summary>
    /// Returns the default binding for a given hotkey action.
    /// </summary>
    public static HotkeyBinding GetDefaultBinding(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.Celebration => new HotkeyBinding { ModifierValue = 0, VirtualKey = 0x71, KeyName = "F2" },
            HotkeyAction.SecondaryCelebration => new HotkeyBinding { ModifierValue = 0x4, VirtualKey = 0x33, KeyName = "3" },
            HotkeyAction.CycleSound => new HotkeyBinding { ModifierValue = 0, VirtualKey = 0x77, KeyName = "F8" },
            HotkeyAction.StopAudio => new HotkeyBinding { VirtualKey = 0, KeyName = "" },
            _ => new HotkeyBinding(),
        };
    }

    public static HotkeyBinding Resolve(JosephExperience.Models.AppSettings settings)
    {
        if (settings.HotkeyVirtualKey != 0 && !string.IsNullOrEmpty(settings.HotkeyKeyName))
        {
            return new HotkeyBinding
            {
                ModifierValue = settings.HotkeyModifierValue,
                VirtualKey = settings.HotkeyVirtualKey,
                KeyName = settings.HotkeyKeyName
            };
        }
        return ParseBinding(settings.HotkeyModifiers, settings.HotkeyKey)
               ?? new HotkeyBinding { VirtualKey = 0x71, KeyName = "F2" };
    }

    private static (uint Vk, string? Name) ResolveKeyName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return (0, null);
        }
        key = key.Trim();

        // Single alphanumeric character
        if (key.Length == 1)
        {
            if (char.IsLetter(key[0]))
            {
                var c = char.ToUpperInvariant(key[0]);
                return ((uint)c, c.ToString());
            }
            if (char.IsDigit(key[0]))
            {
                return (VK_0 + (uint)(key[0] - '0'), key);
            }
        }

        // D-prefixed digit ("D3")
        if (key.StartsWith("D", StringComparison.OrdinalIgnoreCase) && key.Length == 2 && char.IsDigit(key[1]))
        {
            var digit = key[1] - '0';
            return (VK_0 + (uint)digit, digit.ToString());
        }

        var upper = key.ToUpperInvariant();

        // A-Z
        if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z')
        {
            return ((uint)upper[0], upper);
        }

        // Function keys
        if (upper.Length >= 2 && upper[0] == 'F' && int.TryParse(upper[1..], out var fnum) && fnum >= 1 && fnum <= 24)
        {
            return (VK_F1 + (uint)(fnum - 1), upper);
        }

        return upper switch
        {
            "ESC" or "ESCAPE" => (VK_ESCAPE, "Esc"),
            "SPACE" => (VK_SPACE, "Space"),
            "BACK" or "BACKSPACE" => (VK_BACK, "Backspace"),
            "TAB" => (VK_TAB, "Tab"),
            "ENTER" or "RETURN" => (VK_RETURN, "Enter"),
            "DELETE" or "DEL" => (VK_DELETE, "Del"),
            "INSERT" or "INS" => (VK_INSERT, "Ins"),
            "HOME" => (VK_HOME, "Home"),
            "END" => (VK_END, "End"),
            "PAGEUP" or "PGUP" => (VK_PRIOR, "PgUp"),
            "PAGEDOWN" or "PGDN" => (VK_NEXT, "PgDn"),
            "UP" => (VK_UP, "Up"),
            "DOWN" => (VK_DOWN, "Down"),
            "LEFT" => (VK_LEFT, "Left"),
            "RIGHT" => (VK_RIGHT, "Right"),
            "OEMPLUS" or "+" => (VK_OEM_PLUS, "+"),
            "OEMMINUS" or "-" => (VK_OEM_MINUS, "-"),
            "OEMPERIOD" or "." => (VK_OEM_PERIOD, "."),
            "OEMCOMMA" or "," => (VK_OEM_COMMA, ","),
            _ => (0, null)
        };
    }
}

internal static class KeyExtensions
{
    public static char ToAsciiUpper(this Key key)
    {
        return (char)((int)key + (int)'A' - (int)Key.A);
    }
}
