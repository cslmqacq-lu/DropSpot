using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DropSpot;

public sealed class GlobalHotKeyManager : IDisposable
{
    internal const int OpenLatestFolderId = 0x4453;
    internal const int CopyLatestFolderPathId = 0x4454;
    internal const int HotKeyMessage = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private readonly IntPtr _windowHandle;
    private readonly Action _openLatestFolder;
    private readonly Action _copyLatestFolderPath;
    private readonly SavedHotKey _openLatestBinding;
    private readonly SavedHotKey _copyLatestBinding;
    private readonly HashSet<int> _registered = new();

    public GlobalHotKeyManager(
        IntPtr windowHandle,
        SavedHotKey openLatestBinding,
        SavedHotKey copyLatestBinding,
        Action openLatestFolder,
        Action copyLatestFolderPath)
    {
        _windowHandle = windowHandle;
        _openLatestBinding = openLatestBinding.Clone();
        _copyLatestBinding = copyLatestBinding.Clone();
        _openLatestFolder = openLatestFolder;
        _copyLatestFolderPath = copyLatestFolderPath;
    }

    public IReadOnlyList<string> Register()
    {
        var errors = new List<string>();
        TryRegister(OpenLatestFolderId, _openLatestBinding, "打开最新文件夹", errors);
        TryRegister(CopyLatestFolderPathId, _copyLatestBinding, "复制最新文件夹地址", errors);
        return errors;
    }

    public bool ProcessMessage(int message, IntPtr id)
    {
        if (message != HotKeyMessage)
        {
            return false;
        }

        switch (id.ToInt32())
        {
            case OpenLatestFolderId:
                _openLatestFolder();
                return true;
            case CopyLatestFolderPathId:
                _copyLatestFolderPath();
                return true;
            default:
                return false;
        }
    }

    private void TryRegister(int id, SavedHotKey binding, string actionName, ICollection<string> errors)
    {
        if (!binding.TryValidate(out var validationError) || !binding.TryGetVirtualKey(out var virtualKey))
        {
            errors.Add($"{actionName}快捷键无效：{validationError}");
            return;
        }

        var modifiers = ModNoRepeat;
        if (binding.Ctrl) modifiers |= ModControl;
        if (binding.Alt) modifiers |= ModAlt;
        if (binding.Shift) modifiers |= ModShift;
        if (RegisterHotKey(_windowHandle, id, modifiers, virtualKey))
        {
            _registered.Add(id);
            return;
        }

        errors.Add($"{actionName}快捷键 {binding.DisplayText()} 注册失败：{new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }

    public void Dispose()
    {
        foreach (var id in _registered)
        {
            _ = UnregisterHotKey(_windowHandle, id);
        }

        _registered.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
