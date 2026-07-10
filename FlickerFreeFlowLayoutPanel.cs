namespace DiskWriteWatcher;

public sealed class FlickerFreeFlowLayoutPanel : FlowLayoutPanel
{
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
}
