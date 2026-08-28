namespace DesktopManager.Core;

public sealed record GlobalHotKeyBinding(
    string DisplayText,
    uint NativeModifiers,
    uint VirtualKey)
{
    private const uint AltModifier = 0x0001;
    private const uint ControlModifier = 0x0002;
    private const uint ShiftModifier = 0x0004;
    private const uint WindowsModifier = 0x0008;

    public static GlobalHotKeyBinding Default { get; } = new(
        "Ctrl + Alt + Space",
        NativeModifiers: 0x0003,
        VirtualKey: 0x20);

    public static bool TryCreate(
        string key,
        bool ctrl,
        bool alt,
        bool shift,
        bool windows,
        out GlobalHotKeyBinding? binding,
        out string message)
    {
        var modifierCount = (ctrl ? 1 : 0)
            + (alt ? 1 : 0)
            + (shift ? 1 : 0)
            + (windows ? 1 : 0);
        if (modifierCount < 2)
        {
            binding = null;
            message = "全局快捷键至少需要两个修饰键。";
            return false;
        }

        if (!TryGetVirtualKey(key, out var normalizedKey, out var virtualKey))
        {
            binding = null;
            message = "仅支持 Space、A–Z、0–9 和 F1–F12。";
            return false;
        }

        var modifiers = (ctrl ? ControlModifier : 0u)
            | (alt ? AltModifier : 0u)
            | (shift ? ShiftModifier : 0u)
            | (windows ? WindowsModifier : 0u);
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (windows) parts.Add("Win");
        parts.Add(normalizedKey);
        binding = new GlobalHotKeyBinding(string.Join(" + ", parts), modifiers, virtualKey);
        message = "快捷键有效。";
        return true;
    }

    public static GlobalHotKeyBinding NormalizeOrDefault(GlobalHotKeyBinding? binding)
    {
        if (binding is null || (binding.NativeModifiers & ~0x000Fu) != 0)
        {
            return Default;
        }

        var ctrl = (binding.NativeModifiers & ControlModifier) != 0;
        var alt = (binding.NativeModifiers & AltModifier) != 0;
        var shift = (binding.NativeModifiers & ShiftModifier) != 0;
        var windows = (binding.NativeModifiers & WindowsModifier) != 0;
        if (!TryGetKeyName(binding.VirtualKey, out var key)
            || !TryCreate(key, ctrl, alt, shift, windows, out var normalized, out _))
        {
            return Default;
        }

        return normalized!;
    }

    private static bool TryGetVirtualKey(
        string key,
        out string normalizedKey,
        out uint virtualKey)
    {
        normalizedKey = key.Trim().ToUpperInvariant();
        if (normalizedKey == "SPACE")
        {
            normalizedKey = "Space";
            virtualKey = 0x20;
            return true;
        }
        if (normalizedKey.Length == 1
            && (char.IsAsciiLetter(normalizedKey[0]) || char.IsAsciiDigit(normalizedKey[0])))
        {
            virtualKey = normalizedKey[0];
            return true;
        }
        if (normalizedKey.Length is 2 or 3
            && normalizedKey[0] == 'F'
            && int.TryParse(normalizedKey[1..], out var functionKey)
            && functionKey is >= 1 and <= 12)
        {
            normalizedKey = $"F{functionKey}";
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static bool TryGetKeyName(uint virtualKey, out string key)
    {
        if (virtualKey == 0x20)
        {
            key = "Space";
            return true;
        }
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            key = ((char)virtualKey).ToString();
            return true;
        }
        if (virtualKey is >= 0x70 and <= 0x7B)
        {
            key = $"F{virtualKey - 0x70 + 1}";
            return true;
        }

        key = string.Empty;
        return false;
    }
}
