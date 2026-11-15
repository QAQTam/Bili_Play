namespace BiliPlay.Core;

public sealed record HotkeyBinding(uint Modifiers, uint Key)
{
    public static IReadOnlyList<HotkeyBinding> Defaults { get; } = [new(3, 0x21), new(3, 0x22), new(3, 0x20)];
    public bool IsValid => Modifiers is > 0 and <= 7 && (Modifiers & 3) != 0
        && (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A or 0x20 or >= 0x21 and <= 0x28 or 0x2D or 0x2E)
        && !(Modifiers == 3 && Key == 0x2E);
    public override string ToString() => $"{Modifiers}:{Key}";
    public static HotkeyBinding Parse(string text, HotkeyBinding fallback)
    {
        var pieces = text.Split(':');
        return pieces.Length == 2 && uint.TryParse(pieces[0], out var modifiers) && uint.TryParse(pieces[1], out var key)
            && new HotkeyBinding(modifiers, key) is { IsValid: true } binding ? binding : fallback;
    }
    public string Label => ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "")
        + ((Modifiers & 4) != 0 ? "Shift + " : "") + (Key switch
        {
            0x20 => "空格", 0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
            0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓", 0x2D => "Insert", 0x2E => "Delete",
            >= 0x70 and <= 0x7A => "F" + (Key - 0x6F), _ => ((char)Key).ToString()
        });
}
