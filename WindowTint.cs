using System.Runtime.InteropServices;

namespace DiskWriteWatcher;

internal static class WindowTint
{
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableTransparentGradient = 2;

    public static void Apply(Form form, Color color, int opacityPercent)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        var opacity = AppSettings.NormalizeFloatingOpacityPercent(opacityPercent);
        var alpha = (byte)Math.Round(opacity / 100D * 255D);
        var policy = new AccentPolicy
        {
            AccentState = AccentEnableTransparentGradient,
            AccentFlags = 2,
            GradientColor = alpha << 24 | color.B << 16 | color.G << 8 | color.R
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var policyPointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, fDeleteOld: false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = size
            };
            _ = SetWindowCompositionAttribute(form.Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);
}
