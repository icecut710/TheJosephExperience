namespace JosephExperience.Models;

/// <summary>
/// Structural representation of a global hotkey. The display string is derived
/// and is only used for presentation; the modifier mask and virtual key are the
/// authoritative values passed to RegisterHotKey.
/// </summary>
public class HotkeyBinding
{
    public string ModifierMask { get; set; } = "None";
    public uint ModifierValue { get; set; } = 0;

    /// <summary>Virtual key code (e.g. 0x33 for the '3' key).</summary>
    public uint VirtualKey { get; set; } = 0x71;

    public string KeyName { get; set; } = "F2";

    public bool IsEmpty => VirtualKey == 0;

    public string DisplayName
    {
        get
        {
            if (IsEmpty)
            {
                return "None";
            }
            var mods = new System.Collections.Generic.List<string>();
            if ((ModifierValue & 0x1) != 0) mods.Add("Alt");
            if ((ModifierValue & 0x2) != 0) mods.Add("Ctrl");
            if ((ModifierValue & 0x4) != 0) mods.Add("Shift");
            if ((ModifierValue & 0x8) != 0) mods.Add("Win");
            return mods.Count == 0 ? KeyName : string.Join(" + ", mods) + " + " + KeyName;
        }
    }

    public override string ToString() => DisplayName;
}
