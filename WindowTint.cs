using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DropSpot;

internal static class TransparentWindowStyle
{
    // The foreground only contains controls; the PNG backdrop supplies every visible background pixel.
    internal static readonly Color KeyColor = Color.FromArgb(1, 0, 1);

    public static void ApplyToForeground(Form form)
    {
        form.BackColor = KeyColor;
        form.TransparencyKey = KeyColor;
        form.Opacity = 1D;
    }
}

internal sealed class LayeredImageBackdropForm : Form
{
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const int UlwAlpha = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private readonly bool _acceptsInput;
    private Bitmap? _image;

    public LayeredImageBackdropForm(bool acceptsInput = false)
    {
        _acceptsInput = acceptsInput;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExTransparent = 0x00000020;
            const int wsExToolWindow = 0x00000080;
            const int wsExLayered = 0x00080000;
            const int wsExNoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExToolWindow | wsExLayered | wsExNoActivate;
            if (!_acceptsInput)
            {
                parameters.ExStyle |= wsExTransparent;
            }

            return parameters;
        }
    }

    public void SetImage(Image image)
    {
        _image?.Dispose();
        _image = CopyAsPremultiplied(image);
        Size = _image.Size;
        Render();
    }

    public void SyncTo(Form foreground)
    {
        Bounds = foreground.Bounds;
        if (foreground.Visible)
        {
            ShowBehind(foreground);
        }
    }

    public void ShowBehind(Form foreground)
    {
        if (!Visible)
        {
            Show();
        }

        Render();
        if (IsHandleCreated && foreground.IsHandleCreated)
        {
            _ = SetWindowPos(
                Handle,
                foreground.Handle,
                Left,
                Top,
                Width,
                Height,
                SwpNoActivate | SwpNoOwnerZOrder);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }

        base.Dispose(disposing);
    }

    private void Render()
    {
        if (_image is null || !IsHandleCreated)
        {
            return;
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = _image.GetHbitmap(Color.FromArgb(0));
        var previousBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var destination = new NativePoint(Left, Top);
            var size = new NativeSize(_image.Width, _image.Height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            _ = UpdateLayeredWindow(
                Handle,
                screenDc,
                ref destination,
                ref size,
                memoryDc,
                ref source,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
            _ = SelectObject(memoryDc, previousBitmap);
            _ = DeleteObject(bitmapHandle);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static Bitmap CopyAsPremultiplied(Image source)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return copy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint source,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
