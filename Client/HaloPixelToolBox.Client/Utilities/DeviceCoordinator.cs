namespace HaloPixelToolBox.Client.Utilities;

internal enum LyricSourceKind
{
    CloudMusic,
    Spotify
}

/// <summary>
/// 串行化两个歌词来源对同一块 PixelBar 的状态切换。
/// generation 使已进入一轮循环的旧来源在失去所有权后无法继续写入。
/// </summary>
internal static class DeviceCoordinator
{
    public static HaloPixelToolBox.Core.Utilities.HaloPixelDevice Device { get; } = HaloPixelToolBox.Core.Utilities.HaloPixelDevice.Shared;

    private static readonly object SyncRoot = new();
    private static LyricSourceKind? _owner;
    private static long _generation;
    private static bool _stopping;

    public static void BeginShutdown()
    {
        lock (SyncRoot)
            _stopping = true;
    }

    public static void Dispose()
    {
        lock (SyncRoot)
        {
            _stopping = true;
            _owner = null;
            _generation++;
            Device.Dispose();
        }
    }

    public static long Activate(LyricSourceKind source)
    {
        lock (SyncRoot)
        {
            if (_stopping) return 0;
            _owner = source;
            return ++_generation;
        }
    }

    public static bool ExecuteIfOwner(LyricSourceKind source, long generation, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (SyncRoot)
        {
            if (_stopping || generation == 0 || _owner != source || _generation != generation)
                return false;

            action();
            return true;
        }
    }

    public static bool IsOwner(LyricSourceKind source, long generation)
    {
        lock (SyncRoot)
            return !_stopping && generation != 0 && _owner == source && _generation == generation;
    }

    public static bool Deactivate(
        LyricSourceKind source,
        long generation,
        Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (SyncRoot)
        {
            if (generation == 0 || _owner != source || _generation != generation)
                return false;

            try
            {
                cleanup();
            }
            finally
            {
                _owner = null;
                _generation++;
            }

            return true;
        }
    }
}
