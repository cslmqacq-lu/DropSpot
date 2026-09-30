using System.Runtime.InteropServices;
using System.Text;

namespace DropSpot;

/// <summary>
/// 接收从资源管理器拖进来的文件 / 文件夹。
/// DropSpot 通常以管理员身份运行，而资源管理器是普通权限：Windows 的 UIPI 会拦截
/// 普通权限程序向管理员窗口发起的 OLE 拖放（AllowDrop 完全收不到）。
/// 这里改用 Shell 的 WM_DROPFILES 机制，并放行相关消息，管理员 / 普通身份下都能接收拖放。
/// </summary>
internal static class ShellFileDrop
{
    public const int WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A;
    private const uint WmCopyGlobalData = 0x0049;
    private const uint MsgfltAllow = 1;

    /// <summary>DropSpot 自己发起的拖出正在进行，此时不把文件当作“拖入收藏”。</summary>
    public static bool InternalDragActive { get; private set; }

    /// <summary>从 DropSpot 拖出文件（标准 FileDrop），期间忽略自身窗口的拖入。</summary>
    public static void DoInternalFileDrag(Control source, string filePath)
    {
        var data = FileActions.CreateFileDropData(filePath);
        if (data is null)
        {
            return;
        }

        InternalDragActive = true;
        try
        {
            source.DoDragDrop(data, DragDropEffects.Copy);
        }
        finally
        {
            InternalDragActive = false;
        }
    }

    /// <summary>
    /// 标准 OLE 拖放：给窗口及其所有子控件（包括之后动态加入的）开启 AllowDrop。
    /// 普通权限运行时，这是资源管理器拖入最可靠的方式。
    /// </summary>
    public static void EnableOleDrop(Control root, Action<string[]> onDrop)
    {
        Attach(root, onDrop);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Attached = new();

    private static void Attach(Control control, Action<string[]> onDrop)
    {
        if (Attached.TryGetValue(control, out _))
        {
            return;
        }

        Attached.Add(control, new object());
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = AcceptsDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragOver += (_, e) => e.Effect = AcceptsDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) =>
        {
            if (!AcceptsDrag(e) || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            {
                return;
            }

            AppLog.Info($"收到拖入：{paths.Length} 项");
            onDrop(paths);
        };
        control.ControlAdded += (_, e) =>
        {
            if (e.Control is not null)
            {
                Attach(e.Control, onDrop);
            }
        };

        foreach (Control child in control.Controls)
        {
            Attach(child, onDrop);
        }
    }

    private static bool AcceptsDrag(DragEventArgs e)
    {
        return !InternalDragActive && e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
    }

    /// <summary>在窗口句柄创建后调用（OnHandleCreated 中）。</summary>
    public static void Enable(Control window)
    {
        if (!window.IsHandleCreated)
        {
            return;
        }

        var handle = window.Handle;
        _ = ChangeWindowMessageFilterEx(handle, WmDropFiles, MsgfltAllow, IntPtr.Zero);
        _ = ChangeWindowMessageFilterEx(handle, WmCopyData, MsgfltAllow, IntPtr.Zero);
        _ = ChangeWindowMessageFilterEx(handle, WmCopyGlobalData, MsgfltAllow, IntPtr.Zero);
        DragAcceptFiles(handle, true);
    }

    /// <summary>在 WndProc 中调用；是拖放消息时取出路径并返回 true。</summary>
    public static bool TryRead(ref Message message, out string[] paths)
    {
        paths = Array.Empty<string>();
        if (message.Msg != WmDropFiles)
        {
            return false;
        }

        var drop = message.WParam;
        try
        {
            var count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
            var result = new List<string>((int)Math.Min(count, 64));
            for (uint index = 0; index < count; index++)
            {
                var length = DragQueryFile(drop, index, null, 0);
                if (length == 0)
                {
                    continue;
                }

                var buffer = new StringBuilder((int)length + 1);
                if (DragQueryFile(drop, index, buffer, (uint)buffer.Capacity) > 0)
                {
                    result.Add(buffer.ToString());
                }
            }

            paths = result.ToArray();
        }
        finally
        {
            DragFinish(drop);
        }

        message.Result = IntPtr.Zero;
        return true;
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr window, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint length);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr drop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr changeFilterStruct);
}
