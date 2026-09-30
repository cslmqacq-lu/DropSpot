using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DropSpot;

public sealed class FileMonitorService : IDisposable
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan RecentEventRetention = TimeSpan.FromSeconds(30);
    private const int MaxRecentEvents = 4096;
    private int _eventsSinceCleanup;

    private readonly List<UsnJournalVolumeWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, RecentFileEvent> _recentEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _excludedLock = new();
    private string[] _excludedPaths = Array.Empty<string>();
    private volatile bool _filterCommonNoise = true;
    private bool _disposed;

    /// <summary>是否过滤 .git、node_modules、浏览器缓存等常见噪音目录。</summary>
    public bool FilterCommonNoise
    {
        get => _filterCommonNoise;
        set => _filterCommonNoise = value;
    }

    public event EventHandler<ChangeRecord>? Changed;
    public event EventHandler<string>? MonitorError;
    public event EventHandler<VolumeMonitorStatus>? VolumeStatusChanged;

    public IReadOnlyCollection<string> ActiveScopes => _watchers.SelectMany(watcher => watcher.ScopePaths).ToArray();
    public IReadOnlyList<VolumeMonitorStatus> VolumeStatuses => _watchers
        .Select(watcher => watcher.Status)
        .OrderBy(status => status.VolumeRoot, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    public bool IsRunning => _watchers.Count > 0;

    public void Start(IEnumerable<WatchScope> scopes, IEnumerable<string>? excludedPaths = null)
    {
        Stop();
        SetExcludedPaths(excludedPaths ?? Array.Empty<string>());

        var enabledScopes = scopes
            .Where(scope => scope.Enabled)
            .Where(scope => !string.IsNullOrWhiteSpace(scope.Path))
            .Select(scope => NormalizeDirectoryPath(scope.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var missing in enabledScopes.Where(path => !Directory.Exists(path)))
        {
            MonitorError?.Invoke(this, $"路径暂不可用，将等待恢复：{missing}");
        }

        foreach (var group in enabledScopes
                     .Select(path => new { Path = path, Root = Path.GetPathRoot(path) })
                     .Where(item => !string.IsNullOrWhiteSpace(item.Root))
                     .GroupBy(item => item.Root!, item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new UsnJournalVolumeWatcher(
                    group.Key,
                    group.ToArray(),
                    Publish,
                    message => MonitorError?.Invoke(this, message),
                    status => VolumeStatusChanged?.Invoke(this, status));
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                MonitorError?.Invoke(this, $"{group.Key} USN 监听启动失败：{ex.Message}");
            }
        }
    }

    public void SetExcludedPaths(IEnumerable<string> excludedPaths)
    {
        var normalizedPaths = excludedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(PathRules.NormalizeRule)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToArray();

        lock (_excludedLock)
        {
            _excludedPaths = normalizedPaths;
        }
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _recentEvents.Clear();
    }

    private void Publish(ChangeRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.FilePath))
        {
            return;
        }

        if (ShouldIgnore(record.FolderPath, record.FileName)
            || (_filterCommonNoise && PathRules.ContainsCommonNoiseDirectory(record.FolderPath))
            || IsExcluded(record.FilePath))
        {
            return;
        }

        var key = NormalizeEventKey(record.FilePath);
        if (_recentEvents.TryGetValue(key, out var recent) && record.Time - recent.Time < DuplicateWindow)
        {
            if (ShouldSuppressNearDuplicate(recent.ChangeKind, record.ChangeKind))
            {
                _recentEvents[key] = recent with { Time = record.Time };
                return;
            }
        }

        _recentEvents[key] = new RecentFileEvent(record.Time, record.ChangeKind);
        CleanupRecentEvents(record.Time);
        Changed?.Invoke(this, record);
    }

    private bool IsExcluded(string path)
    {
        string[] excludedPaths;
        lock (_excludedLock)
        {
            excludedPaths = _excludedPaths;
        }

        // 同时检查文件路径中的每一级目录与文件名，所以既能排除完整路径，也能按名称排除（如 node_modules、*.log）。
        return PathRules.MatchesAny(path, excludedPaths);
    }

    private static bool ShouldSuppressNearDuplicate(string previousKind, string currentKind)
    {
        if (previousKind == currentKind)
        {
            return true;
        }

        return currentKind == "更新" && previousKind is "新建" or "重命名";
    }

    private static string NormalizeEventKey(string changedPath)
    {
        try
        {
            return Path.GetFullPath(changedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return changedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.TrimEnd(Path.AltDirectorySeparatorChar);
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    internal static bool ShouldIgnore(string folderPath, string fileName)
    {
        if (PathRules.IsTemporaryFileName(fileName))
        {
            return true;
        }

        var normalized = folderPath.Replace('/', '\\');
        return normalized.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\System Volume Information\\", StringComparison.OrdinalIgnoreCase);
    }

    private void CleanupRecentEvents(DateTime now)
    {
        var shouldCleanByTime = Interlocked.Increment(ref _eventsSinceCleanup) >= 256;
        if (!shouldCleanByTime && _recentEvents.Count <= MaxRecentEvents)
        {
            return;
        }

        Interlocked.Exchange(ref _eventsSinceCleanup, 0);

        foreach (var pair in _recentEvents)
        {
            if (now - pair.Value.Time > RecentEventRetention)
            {
                _recentEvents.TryRemove(pair.Key, out _);
            }
        }

        if (_recentEvents.Count > MaxRecentEvents)
        {
            foreach (var pair in _recentEvents
                         .OrderBy(item => item.Value.Time)
                         .Take(_recentEvents.Count - MaxRecentEvents))
            {
                _recentEvents.TryRemove(pair.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private readonly record struct RecentFileEvent(DateTime Time, string ChangeKind);
}

internal sealed class UsnJournalVolumeWatcher : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileReadAttributes = 0x00000080;

    private const uint FsctlReadUsnJournal = 0x000900BB;
    private const uint FsctlQueryUsnJournal = 0x000900F4;

    private const uint UsnReasonDataOverwrite = 0x00000001;
    private const uint UsnReasonDataExtend = 0x00000002;
    private const uint UsnReasonDataTruncation = 0x00000004;
    private const uint UsnReasonFileCreate = 0x00000100;
    private const uint UsnReasonRenameNewName = 0x00002000;
    private const uint RelevantReasonMask = UsnReasonDataOverwrite
        | UsnReasonDataExtend
        | UsnReasonDataTruncation
        | UsnReasonFileCreate
        | UsnReasonRenameNewName;

    private const uint FileAttributeDirectory = 0x00000010;
    private const int ErrorInvalidParameter = 87;

    private readonly string _volumeRoot;
    private readonly string[] _scopePaths;
    private readonly Action<ChangeRecord> _publish;
    private readonly Action<string> _reportError;
    private readonly Action<VolumeMonitorStatus> _reportStatus;
    private readonly Dictionary<FileReference, CachedDirectoryPath> _directoryPathCache = new();
    private readonly HashSet<ushort> _reportedUnsupportedVersions = new();
    private readonly Dictionary<string, DateTime> _reportedErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _statusLock = new();
    private readonly object _handleLock = new();
    private Task? _task;
    private SafeFileHandle? _volumeHandle;
    private VolumeMonitorStatus _status;
    private long _resumeUsn;
    private bool _useV1ReadInput = true;
    private ulong _resumeJournalId;

    public UsnJournalVolumeWatcher(
        string volumeRoot,
        string[] scopePaths,
        Action<ChangeRecord> publish,
        Action<string> reportError,
        Action<VolumeMonitorStatus> reportStatus)
    {
        _volumeRoot = NormalizeRoot(volumeRoot);
        _scopePaths = scopePaths.Select(NormalizeDirectoryPath).ToArray();
        _publish = publish;
        _reportError = reportError;
        _reportStatus = reportStatus;
        _status = VolumeMonitorStatus.Waiting(_volumeRoot);
    }

    public IReadOnlyList<string> ScopePaths => _scopePaths;
    public VolumeMonitorStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    public void Start()
    {
        PublishStatus(_status);
        _task = Task.Run(() => Run(_cts.Token));
    }

    private void Run(CancellationToken token)
    {
        var buffer = new byte[1024 * 1024];
        var retryCount = 0;
        var connectedOnce = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                PublishStatus(Status with
                {
                    State = connectedOnce ? VolumeMonitorState.Reconnecting : VolumeMonitorState.Connecting,
                    Message = connectedOnce ? "正在重新连接" : "正在连接",
                    RetryCount = retryCount
                });

                var drive = new DriveInfo(_volumeRoot);
                if (!drive.IsReady)
                {
                    throw new IOException("磁盘尚未就绪");
                }

                if (drive.DriveFormat is not ("NTFS" or "ReFS"))
                {
                    throw new NotSupportedException($"文件系统 {drive.DriveFormat} 不支持 USN 监听");
                }

                using var volumeHandle = OpenVolume(_volumeRoot);
                lock (_handleLock)
                {
                    _volumeHandle = volumeHandle;
                }

                var journal = QueryJournal(volumeHandle);
                var canResume = _resumeJournalId == journal.UsnJournalId
                    && _resumeUsn >= journal.FirstUsn
                    && _resumeUsn <= journal.NextUsn;
                var nextUsn = canResume ? _resumeUsn : journal.NextUsn;
                _resumeJournalId = journal.UsnJournalId;
                _resumeUsn = nextUsn;
                connectedOnce = true;
                PublishStatus(Status with
                {
                    State = VolumeMonitorState.Healthy,
                    Message = "监视正常",
                    RetryCount = retryCount,
                    LastConnectedAt = DateTime.Now,
                    AccessDenied = false
                });

                ReadJournal(volumeHandle, buffer, nextUsn, journal.UsnJournalId, token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                retryCount++;
                var unsupported = ex is NotSupportedException;
                var accessDenied = Elevation.IsAccessDenied(ex);
                var message = accessDenied
                    ? $"{_volumeRoot} 需要管理员权限才能读取 USN 日志"
                    : $"{_volumeRoot} USN 监听异常：{ex.Message}";
                ReportError(message);
                PublishStatus(Status with
                {
                    State = unsupported ? VolumeMonitorState.Error : connectedOnce ? VolumeMonitorState.Reconnecting : VolumeMonitorState.Waiting,
                    Message = accessDenied ? "需要管理员权限" : ex.Message,
                    RetryCount = retryCount,
                    LastErrorAt = DateTime.Now,
                    AccessDenied = accessDenied
                });

                var delay = unsupported || accessDenied ? TimeSpan.FromSeconds(30) : RetryDelay(retryCount);
                if (token.WaitHandle.WaitOne(delay))
                {
                    break;
                }
            }
            finally
            {
                lock (_handleLock)
                {
                    _volumeHandle = null;
                }

                _directoryPathCache.Clear();
            }
        }

        PublishStatus(Status with { State = VolumeMonitorState.Stopped, Message = "已停止" });
    }

    private void ReadJournal(
        SafeFileHandle volumeHandle,
        byte[] buffer,
        long startUsn,
        ulong journalId,
        CancellationToken token)
    {
        var nextUsn = startUsn;
        while (!token.IsCancellationRequested)
        {
            // 优先使用 V1 输入结构并允许 V2~V3 记录：ReFS 使用 128 位文件 ID，只能以 V3 记录返回。
            // 旧系统不认识 V1 结构时（ERROR_INVALID_PARAMETER）自动退回 V0。
            bool succeeded;
            int bytesReturned;
            if (_useV1ReadInput)
            {
                var inputV1 = new ReadUsnJournalDataV1
                {
                    StartUsn = nextUsn,
                    ReasonMask = RelevantReasonMask,
                    ReturnOnlyOnClose = 1,
                    Timeout = 1,
                    BytesToWaitFor = 1,
                    UsnJournalId = journalId,
                    MinMajorVersion = 2,
                    MaxMajorVersion = 3
                };
                succeeded = DeviceIoControl(volumeHandle, FsctlReadUsnJournal, ref inputV1, Marshal.SizeOf<ReadUsnJournalDataV1>(), buffer, buffer.Length, out bytesReturned, IntPtr.Zero);
                if (!succeeded && Marshal.GetLastWin32Error() == ErrorInvalidParameter)
                {
                    _useV1ReadInput = false;
                    ReportError($"{_volumeRoot} 系统不支持 USN V1 读取结构，已退回 V0");
                    continue;
                }
            }
            else
            {
                var input = new ReadUsnJournalData
                {
                    StartUsn = nextUsn,
                    ReasonMask = RelevantReasonMask,
                    ReturnOnlyOnClose = 1,
                    Timeout = 1,
                    BytesToWaitFor = 1,
                    UsnJournalId = journalId
                };
                succeeded = DeviceIoControl(volumeHandle, FsctlReadUsnJournal, ref input, Marshal.SizeOf<ReadUsnJournalData>(), buffer, buffer.Length, out bytesReturned, IntPtr.Zero);
            }

            if (!succeeded)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 38)
                {
                    if (token.WaitHandle.WaitOne(500))
                    {
                        return;
                    }

                    continue;
                }

                throw new Win32Exception(error);
            }

            if (bytesReturned >= 8)
            {
                nextUsn = BitConverter.ToInt64(buffer, 0);
                _resumeUsn = nextUsn;
                ParseRecords(volumeHandle, buffer, bytesReturned);
            }
        }
    }

    internal static TimeSpan RetryDelay(int retryCount)
    {
        return TimeSpan.FromSeconds(Math.Min(30, Math.Max(2, retryCount * 2)));
    }

    private void ParseRecords(SafeFileHandle volumeHandle, byte[] buffer, int bytesReturned)
    {
        var offset = 8;
        while (offset + 8 <= bytesReturned)
        {
            var recordLength = BitConverter.ToUInt32(buffer, offset);
            if (recordLength < 8 || offset + recordLength > bytesReturned)
            {
                break;
            }

            var majorVersion = BitConverter.ToUInt16(buffer, offset + 4);
            if (majorVersion == 2 && recordLength >= 60)
            {
                ParseV2Record(volumeHandle, buffer, offset, (int)recordLength);
            }
            else if (majorVersion == 3 && recordLength >= 76)
            {
                ParseV3Record(volumeHandle, buffer, offset, (int)recordLength);
            }
            else if (_reportedUnsupportedVersions.Add(majorVersion))
            {
                ReportError($"{_volumeRoot} 暂不支持 USN V{majorVersion} 记录，该版本记录将被跳过");
            }

            offset += (int)recordLength;
        }
    }

    private void ParseV2Record(SafeFileHandle volumeHandle, byte[] buffer, int offset, int recordLength)
    {
        var fileReference = FileReference.From64(BitConverter.ToUInt64(buffer, offset + 8));
        var parentReference = FileReference.From64(BitConverter.ToUInt64(buffer, offset + 16));
        var reason = BitConverter.ToUInt32(buffer, offset + 40);
        var fileAttributes = BitConverter.ToUInt32(buffer, offset + 52);
        var fileNameLength = BitConverter.ToUInt16(buffer, offset + 56);
        var fileNameOffset = BitConverter.ToUInt16(buffer, offset + 58);

        ParseNamedRecord(
            volumeHandle,
            buffer,
            offset,
            recordLength,
            fileReference,
            parentReference,
            reason,
            fileAttributes,
            fileNameLength,
            fileNameOffset);
    }

    private void ParseV3Record(SafeFileHandle volumeHandle, byte[] buffer, int offset, int recordLength)
    {
        var fileReference = FileReference.From128(
            BitConverter.ToUInt64(buffer, offset + 8),
            BitConverter.ToUInt64(buffer, offset + 16));
        var parentReference = FileReference.From128(
            BitConverter.ToUInt64(buffer, offset + 24),
            BitConverter.ToUInt64(buffer, offset + 32));
        var reason = BitConverter.ToUInt32(buffer, offset + 56);
        var fileAttributes = BitConverter.ToUInt32(buffer, offset + 68);
        var fileNameLength = BitConverter.ToUInt16(buffer, offset + 72);
        var fileNameOffset = BitConverter.ToUInt16(buffer, offset + 74);

        ParseNamedRecord(
            volumeHandle,
            buffer,
            offset,
            recordLength,
            fileReference,
            parentReference,
            reason,
            fileAttributes,
            fileNameLength,
            fileNameOffset);
    }

    private void ParseNamedRecord(
        SafeFileHandle volumeHandle,
        byte[] buffer,
        int offset,
        int recordLength,
        FileReference fileReference,
        FileReference parentReference,
        uint reason,
        uint fileAttributes,
        ushort fileNameLength,
        ushort fileNameOffset)
    {
        if (fileNameLength == 0 || fileNameOffset + fileNameLength > recordLength)
        {
            return;
        }

        var fileName = System.Text.Encoding.Unicode.GetString(buffer, offset + fileNameOffset, fileNameLength);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        if ((fileAttributes & FileAttributeDirectory) != 0)
        {
            if ((reason & UsnReasonRenameNewName) != 0)
            {
                _directoryPathCache.Clear();
            }

            var directoryPath = TryGetPathByFileId(volumeHandle, fileReference);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                CacheDirectoryPath(fileReference, directoryPath);
            }

            return;
        }

        var changeKind = ReasonToKind(reason);
        if (changeKind is null)
        {
            return;
        }

        var filePath = ResolvePath(volumeHandle, fileReference, parentReference, fileName);
        if (filePath is null || !IsInsideAnyScope(filePath))
        {
            return;
        }

        var folderPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var now = DateTime.Now;
        MarkEvent(now);
        _publish(new ChangeRecord
        {
            Time = now,
            ChangeKind = changeKind,
            FolderPath = folderPath,
            FilePath = filePath,
            ScopePath = BestScopeFor(filePath),
            FileName = Path.GetFileName(filePath)
        });
    }

    private string? ResolvePath(
        SafeFileHandle volumeHandle,
        FileReference fileReference,
        FileReference parentReference,
        string fileName)
    {
        if (TryGetCachedDirectoryPath(parentReference, out var cachedParent))
        {
            return Path.Combine(cachedParent, fileName);
        }

        var parent = TryGetPathByFileId(volumeHandle, parentReference);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            CacheDirectoryPath(parentReference, parent);
            return Path.Combine(parent, fileName);
        }

        return TryGetPathByFileId(volumeHandle, fileReference);
    }

    private string? TryGetPathByFileId(SafeFileHandle volumeHandle, FileReference fileReference)
    {
        var descriptor = new FileIdDescriptor
        {
            Size = (uint)Marshal.SizeOf<FileIdDescriptor>(),
            Type = fileReference.IsExtended ? FileIdType.ExtendedFileIdType : FileIdType.FileIdType,
            FileId = fileReference.Low,
            ExtendedFileIdHighPart = fileReference.High
        };

        using var handle = OpenFileById(
            volumeHandle,
            ref descriptor,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            FileFlagBackupSemantics);

        if (handle.IsInvalid)
        {
            return null;
        }

        var chars = new char[1024];
        var length = GetFinalPathNameByHandle(handle, chars, chars.Length, 0);
        if (length == 0)
        {
            return null;
        }

        if (length > chars.Length)
        {
            chars = new char[length + 1];
            length = GetFinalPathNameByHandle(handle, chars, chars.Length, 0);
        }

        var path = new string(chars, 0, (int)length);
        return NormalizeFinalPath(path);
    }

    private bool TryGetCachedDirectoryPath(FileReference reference, out string path)
    {
        if (_directoryPathCache.TryGetValue(reference, out var cached)
            && DateTime.UtcNow - cached.LastUsedUtc < TimeSpan.FromMinutes(5))
        {
            path = cached.Path;
            _directoryPathCache[reference] = cached with { LastUsedUtc = DateTime.UtcNow };
            return true;
        }

        _directoryPathCache.Remove(reference);
        path = string.Empty;
        return false;
    }

    private void CacheDirectoryPath(FileReference reference, string path)
    {
        if (_directoryPathCache.Count >= 4096)
        {
            foreach (var stale in _directoryPathCache
                         .OrderBy(pair => pair.Value.LastUsedUtc)
                         .Take(512)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _directoryPathCache.Remove(stale);
            }
        }

        _directoryPathCache[reference] = new CachedDirectoryPath(path, DateTime.UtcNow);
    }

    private bool IsInsideAnyScope(string filePath)
    {
        var normalized = NormalizeDirectoryPath(filePath);
        return _scopePaths.Any(scope => IsPathUnderScope(normalized, scope));
    }

    private string BestScopeFor(string filePath)
    {
        var normalized = NormalizeDirectoryPath(filePath);
        return _scopePaths
            .Where(scope => IsPathUnderScope(normalized, scope))
            .OrderByDescending(scope => scope.Length)
            .FirstOrDefault() ?? _volumeRoot;
    }

    private static bool IsPathUnderScope(string normalizedPath, string scope)
    {
        if (string.Equals(normalizedPath, scope, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = scope.EndsWith(Path.DirectorySeparatorChar)
            ? scope
            : scope + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReasonToKind(uint reason)
    {
        if ((reason & UsnReasonRenameNewName) != 0)
        {
            return "重命名";
        }

        if ((reason & UsnReasonFileCreate) != 0)
        {
            return "新建";
        }

        if ((reason & (UsnReasonDataOverwrite | UsnReasonDataExtend | UsnReasonDataTruncation)) != 0)
        {
            return "更新";
        }

        return null;
    }

    private static SafeFileHandle OpenVolume(string volumeRoot)
    {
        var drive = volumeRoot.TrimEnd('\\');
        var handle = CreateFile(
            @"\\.\" + drive,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }

    private static UsnJournalData QueryJournal(SafeFileHandle volumeHandle)
    {
        var data = new UsnJournalData();
        if (!DeviceIoControl(volumeHandle, FsctlQueryUsnJournal, IntPtr.Zero, 0, ref data, Marshal.SizeOf<UsnJournalData>(), out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return data;
    }

    private static string NormalizeFinalPath(string path)
    {
        const string longPathPrefix = @"\\?\";
        if (path.StartsWith(longPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[longPathPrefix.Length..];
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var pathRoot = Path.GetPathRoot(full);
        return pathRoot ?? full;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.TrimEnd(Path.AltDirectorySeparatorChar);
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_handleLock)
        {
            _volumeHandle?.Dispose();
        }

        try
        {
            _task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown should not surface background read errors.
        }

        _cts.Dispose();
    }

    private void MarkEvent(DateTime time)
    {
        lock (_statusLock)
        {
            _status = _status with { LastEventAt = time };
        }
    }

    private void PublishStatus(VolumeMonitorStatus status)
    {
        lock (_statusLock)
        {
            _status = status;
        }

        _reportStatus(status);
    }

    private void ReportError(string message)
    {
        var now = DateTime.UtcNow;
        if (_reportedErrors.TryGetValue(message, out var lastReported)
            && now - lastReported < TimeSpan.FromSeconds(15))
        {
            return;
        }

        _reportedErrors[message] = now;
        _reportError(message);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref ReadUsnJournalData lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref ReadUsnJournalDataV1 lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        ref UsnJournalData lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FileIdDescriptor lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        [Out] char[] lpszFilePath,
        int cchFilePath,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalData
    {
        public ulong UsnJournalId;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadUsnJournalDataV1
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }

    private enum FileIdType : uint
    {
        FileIdType = 0,
        ExtendedFileIdType = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdDescriptor
    {
        public uint Size;
        public FileIdType Type;
        public ulong FileId;
        public ulong ExtendedFileIdHighPart;
    }

    private readonly record struct FileReference(ulong Low, ulong High, bool IsExtended)
    {
        public static FileReference From64(ulong value) => new(value, 0, false);
        public static FileReference From128(ulong low, ulong high) => new(low, high, true);
    }

    private readonly record struct CachedDirectoryPath(string Path, DateTime LastUsedUtc);
}
