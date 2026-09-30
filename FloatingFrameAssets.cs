using System.Reflection;

namespace DropSpot;

internal static class FloatingFrameAssets
{
    private static readonly Lazy<Bitmap> MainFrameValue = new(() => Load("DropSpot.Floating.Main.png"));
    private static readonly Lazy<Bitmap> FavoriteFrameValue = new(() => Load("DropSpot.Floating.Favorite.png"));
    private static readonly Lazy<Bitmap> PinnedFrameValue = new(() => Load("DropSpot.Floating.Pinned.png"));
    private static readonly Lazy<Bitmap> PopupFrameValue = new(() => Load("DropSpot.Floating.Popup.png"));

    public static Bitmap MainFrame => MainFrameValue.Value;
    public static Bitmap FavoriteFrame => FavoriteFrameValue.Value;
    public static Bitmap PinnedFrame => PinnedFrameValue.Value;
    public static Bitmap PopupFrame => PopupFrameValue.Value;

    private static Bitmap Load(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing floating frame resource: {resourceName}");
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }
}
