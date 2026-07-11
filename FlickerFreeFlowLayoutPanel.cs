using System.Runtime.InteropServices;

namespace DropSpot;

public sealed class FlickerFreeFlowLayoutPanel : FlowLayoutPanel
{
    private const int SbBoth = 3;
    private const int WmPaint = 0x000F;
    private const int WmSize = 0x0005;
    private const int WmNcPaint = 0x0085;

    public FlickerFreeFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        UpdateStyles();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNativeScrollBars();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideNativeScrollBars();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        HideNativeScrollBars();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is WmPaint or WmSize or WmNcPaint)
        {
            HideNativeScrollBars();
        }
    }

    private void HideNativeScrollBars()
    {
        if (IsHandleCreated)
        {
            _ = ShowScrollBar(Handle, SbBoth, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
}
