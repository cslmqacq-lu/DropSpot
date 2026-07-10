namespace DiskWriteWatcher;

public static class Theme
{
    public static readonly Color Window = Color.FromArgb(11, 17, 26);
    public static readonly Color Panel = Color.FromArgb(16, 23, 34);
    public static readonly Color Card = Color.FromArgb(19, 29, 44);
    public static readonly Color CardLatest = Color.FromArgb(20, 33, 46);
    public static readonly Color CardAlt = Color.FromArgb(15, 23, 36);
    public static readonly Color Border = Color.FromArgb(38, 50, 68);
    public static readonly Color BorderStrong = Color.FromArgb(54, 71, 96);
    public static readonly Color Text = Color.FromArgb(226, 232, 240);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Dim = Color.FromArgb(100, 116, 139);
    public static readonly Color Accent = Color.FromArgb(61, 220, 132);
    public static readonly Color AccentDark = Color.FromArgb(22, 126, 75);
    public static readonly Color Folder = Color.FromArgb(219, 168, 55);
    public static readonly Color FolderLight = Color.FromArgb(242, 196, 92);

    public static Color TextForBackground(Color background)
    {
        return IsLight(background) ? Color.FromArgb(19, 27, 38) : Text;
    }

    public static Color MutedTextForBackground(Color background)
    {
        return IsLight(background) ? Color.FromArgb(65, 76, 90) : Muted;
    }

    public static Color HighlightTextForBackground(Color background)
    {
        return IsLight(background) ? Color.FromArgb(105, 73, 0) : Color.FromArgb(242, 205, 112);
    }

    public static Color HoverForBackground(Color background)
    {
        return Blend(background, IsLight(background) ? Color.Black : Color.White, 0.12F);
    }

    public static Color BorderForBackground(Color background)
    {
        return Blend(background, IsLight(background) ? Color.Black : Color.White, 0.22F);
    }

    private static bool IsLight(Color color)
    {
        return color.R * 0.299F + color.G * 0.587F + color.B * 0.114F >= 160F;
    }

    private static Color Blend(Color source, Color target, float amount)
    {
        return Color.FromArgb(
            255,
            (int)Math.Round(source.R + (target.R - source.R) * amount),
            (int)Math.Round(source.G + (target.G - source.G) * amount),
            (int)Math.Round(source.B + (target.B - source.B) * amount));
    }
}
