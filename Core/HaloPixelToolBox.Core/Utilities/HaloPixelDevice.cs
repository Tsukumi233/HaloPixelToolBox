using System.Diagnostics;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.Display;
using HaloPixelToolBox.Core.Models.Lighting;
using HidSharp;

namespace HaloPixelToolBox.Core.Utilities;

/// <summary>
/// 串行访问花再 HID 命令通道。A160 歌词状态、设备 epoch 和重试状态都由
/// 共享驱动持有，避免网易云与 Spotify 各自缓存一份已经失效的固件状态。
/// </summary>
public partial class HaloPixelDevice
{
    private const int EdifierVendorId = 0x2d99;
    private const int HaloPixelBarA160ProductId = 0xa160;
    private const uint CommandChannelUsage = 0xff140001;
    private const int AckTimeoutMilliseconds = 180;
    private const int HidWriteTimeoutMilliseconds = 750;
    private static readonly TimeSpan DeviceProbeInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DeviceListRecoveryDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan DeviceListRecoveryCooldown = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LyricTransitionHealthInterval = TimeSpan.FromSeconds(20);
    private static readonly object HidWriteLock = new();

    private long _deviceListVersion = 1;
    private long _resolvedDeviceListVersion;
    private long _firstPendingDeviceListChangeTimestamp;
    private long _lastDeviceListRecoveryTimestamp;
    private DateTime _nextDeviceProbeUtc = DateTime.MinValue;
    private bool _pixelStateQueryPending = true;
    private LyricControlProtocol _lyricControlProtocol;
    private HaloPixelColor _pixelScreenColor = new(0xf0, 0xb4, 0xc8);
    private byte _pixelModeGroup;
    private bool _lyricTransitionEnabled;
    private bool _lyricTransitionConfirmed;
    private LyricTransitionPreset _lyricTransitionPreset = LyricTransitionPreset.Preset1;
    private int _lyricTransitionRetryCount;
    private DateTime _nextLyricTransitionRetryUtc = DateTime.MinValue;
    private DateTime _lastLyricTransitionWriteUtc = DateTime.MinValue;
    private string _lastLyricText = string.Empty;
    private bool _replayLastLyricAfterReconnect;

    public static HaloPixelDevice Shared { get; } = new();

    public HidDevice? CurrentDevice { get; private set; }
    public bool SupportsFirmwareLyricTransitions =>
        CurrentDevice?.ProductID == HaloPixelBarA160ProductId;

    // Kept for consumers that want to refresh connection indicators. The driver itself
    // never opens or disposes a stream on HidSharp's device-list callback thread.
    public event EventHandler? DeviceListChanged;

    public HaloPixelDevice()
    {
        DeviceList.Local.Changed += Local_Changed;
    }

    private void Local_Changed(object? sender, DeviceListChangedEventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _firstPendingDeviceListChangeTimestamp, now, 0);
        Interlocked.Increment(ref _deviceListVersion);
        try
        {
            DeviceListChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN]处理 HID 设备变化事件失败：{ex.Message}");
        }
    }

    public bool Initialize()
    {
        lock (HidWriteLock)
            return ResolveCurrentDeviceLocked(forceProbe: CurrentDevice is null) is not null;
    }

    public void ShowText(string text) => ShowLegacyText(text);

    /// <summary>
    /// 显式使用旧设备的自定义文字来源 0x00。A160 歌词路径不会自动降级到这里。
    /// </summary>
    public bool ShowLegacyText(string text)
        => WritePacket(HidPacketBuilder.BuildText(text), invalidatesLyricMode: true);

    /// <summary>
    /// 使用 A160 固件歌词来源 0x0E。需要重新武装时，EC EF 或 ED02、回包读取及所有
    /// E8 分片在同一 HID 流和同一全局写锁中完成。
    /// </summary>
    public LyricWriteResult ShowLyricText(string text, LyricTransitionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(text))
            return new LyricWriteResult(LyricWriteStatus.NoChange);

        var frames = HidPacketBuilder.BuildLyricTextFrames(text);
        lock (HidWriteLock)
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var device = ResolveCurrentDeviceLocked(forceProbe: attempt > 1);
                if (device is null)
                    return new LyricWriteResult(LyricWriteStatus.DeviceUnavailable, Attempts: attempt);
                if (device.ProductID != HaloPixelBarA160ProductId)
                    return new LyricWriteResult(LyricWriteStatus.UnsupportedDevice, Attempts: attempt);

                try
                {
                    using var stream = OpenStream(device);
                    var transition = EnsureLyricTransitionLocked(
                        stream,
                        preset,
                        enabled: true,
                        force: attempt > 1);

                    var textConfirmed = WriteLyricFramesAndConfirmLocked(stream, frames);

                    _lastLyricText = text;
                    _replayLastLyricAfterReconnect = false;
                    return new LyricWriteResult(
                        transition.Confirmed || textConfirmed
                            ? LyricWriteStatus.Success
                            : LyricWriteStatus.TransportAcceptedUnconfirmed,
                        transition.Written,
                        transition.Confirmed,
                        TextWritten: true,
                        Attempts: attempt);
                }
                catch (Exception ex) when (IsTransportException(ex))
                {
                    lastError = ex;
                    InvalidateCurrentDeviceLocked();
                    if (attempt < 2)
                        Thread.Sleep(40);
                }
            }

            Console.WriteLine($"[WARN]歌词 HID 事务失败：{lastError?.Message}");
            return new LyricWriteResult(LyricWriteStatus.IoError, Attempts: 2);
        }
    }

    /// <summary>
    /// 兼容旧调用者，使用驱动最后一次选择的预设。
    /// </summary>
    public LyricWriteResult ShowLyricText(string text)
        => ShowLyricText(text, _lyricTransitionPreset);

    /// <summary>
    /// 查询/设置固件歌词状态。驱动优先探测实机可用的 EC EE/EF 通道，
    /// 并保留 ED01/02 给采用新版 HZ 通道的固件；未确认时按 1、2、3、4、5 秒退避重试，
    /// 未确认时按 1、2、3、4、5 秒退避重试，而不会每 50ms 热循环。
    /// </summary>
    public LyricWriteResult ConfigureLyricTransition(
        LyricTransitionPreset preset,
        bool enabled = true,
        bool force = false)
    {
        ValidateLyricPreset(preset);
        lock (HidWriteLock)
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var device = ResolveCurrentDeviceLocked(forceProbe: attempt > 1);
                if (device is null)
                    return new LyricWriteResult(LyricWriteStatus.DeviceUnavailable, Attempts: attempt);
                if (device.ProductID != HaloPixelBarA160ProductId)
                    return new LyricWriteResult(LyricWriteStatus.UnsupportedDevice, Attempts: attempt);

                var transitionNeeded = ShouldWriteLyricTransitionLocked(preset, enabled, force || attempt > 1);
                var replayNeeded = enabled && _replayLastLyricAfterReconnect &&
                                   !string.IsNullOrEmpty(_lastLyricText);
                if (!transitionNeeded && !replayNeeded && !_pixelStateQueryPending)
                {
                    return new LyricWriteResult(
                        _lyricTransitionConfirmed
                            ? LyricWriteStatus.NoChange
                            : LyricWriteStatus.TransportAcceptedUnconfirmed,
                        TransitionConfirmed: _lyricTransitionConfirmed,
                        Attempts: attempt);
                }

                try
                {
                    using var stream = OpenStream(device);
                    var transition = EnsureLyricTransitionLocked(
                        stream,
                        preset,
                        enabled,
                        force || attempt > 1);

                    var textWritten = false;
                    if (enabled && _replayLastLyricAfterReconnect &&
                        !string.IsNullOrEmpty(_lastLyricText))
                    {
                        WriteLyricFramesAndConfirmLocked(
                            stream,
                            HidPacketBuilder.BuildLyricTextFrames(_lastLyricText));
                        _replayLastLyricAfterReconnect = false;
                        textWritten = true;
                    }

                    if (!enabled)
                    {
                        _lastLyricText = string.Empty;
                        _replayLastLyricAfterReconnect = false;
                    }

                    return new LyricWriteResult(
                        transition.Confirmed
                            ? LyricWriteStatus.Success
                            : LyricWriteStatus.TransportAcceptedUnconfirmed,
                        transition.Written,
                        transition.Confirmed,
                        textWritten,
                        attempt);
                }
                catch (Exception ex) when (IsTransportException(ex))
                {
                    lastError = ex;
                    InvalidateCurrentDeviceLocked();
                    if (attempt < 2)
                        Thread.Sleep(40);
                }
            }

            Console.WriteLine($"[WARN]歌词动画 HID 事务失败：{lastError?.Message}");
            return new LyricWriteResult(LyricWriteStatus.IoError, Attempts: 2);
        }
    }

    public bool SetLyricTransition(LyricTransitionPreset preset, bool enabled = true)
        => ConfigureLyricTransition(preset, enabled).Succeeded;

    /// <summary>
    /// 切歌时清除仅用于设备重连恢复的上一句缓存，避免在新歌词就绪前重播旧歌。
    /// </summary>
    public void ClearLyricReplayCache()
    {
        lock (HidWriteLock)
        {
            _lastLyricText = string.Empty;
            _replayLastLyricAfterReconnect = false;
        }
    }

    public void SetTextLayout(HaloPixelTextLayout layout)
        => WritePacket(HidPacketBuilder.ConvertLayout(layout), invalidatesLyricMode: true);

    public void SetUIModel(HaloPixelUIModel haloPixelUIModel)
        => WritePacket(HidPacketBuilder.ConvertUIModel(haloPixelUIModel), invalidatesLyricMode: true);

    public static IEnumerable<HidDevice> GetPixelDevice()
    {
        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            if (device.VendorID == EdifierVendorId && IsCommandChannel(device))
                yield return device;
        }
    }

    private static bool IsCommandChannel(HidDevice device)
    {
        try
        {
            if (device.GetMaxInputReportLength() != 64 || device.GetMaxOutputReportLength() != 64)
                return false;

            var descriptor = device.GetReportDescriptor();
            return descriptor.DeviceItems.Any(
                static item => item.Usages.ContainsValue(CommandChannelUsage) &&
                               item.OutputReports.Any(
                                   static report => report.ReportID == 0x2e && report.Length == 64) &&
                               item.InputReports.Any(
                                   static report => report.ReportID == 0x2f && report.Length == 64));
        }
        catch
        {
            return false;
        }
    }

    public static void PrintDeviceList()
    {
        foreach (var subDeivce in DeviceList.Local.GetHidDevices())
        {
            try
            {
                Console.WriteLine($"""
                    ----------------------
                    {subDeivce.GetFriendlyName()}
                    VendorID：{subDeivce.VendorID}
                    ProductID：{subDeivce.ProductID}
                    串口号：{subDeivce.GetSerialNumber()}
                    串口：{string.Join(',', subDeivce.GetSerialPorts())}
                    ReleaseNumberBcd：{subDeivce.ReleaseNumberBcd}
                    UsbPort：{subDeivce.GetUsbPort()}
                    ----------------------


                    """);
            }
            catch { }
        }
    }

    public void SetAmbientLight(AmbientLightOptions options)
    {
        lock (HidWriteLock)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var device = ResolveCurrentDeviceLocked(forceProbe: attempt > 1);
                if (device is null)
                    return;

                try
                {
                    using var stream = OpenStream(device);
                    var useA160Protocol = device.ProductID == HaloPixelBarA160ProductId;
                    WriteAmbientPacket(
                        stream,
                        HidPacketBuilder.BuildAmbientLightPower(options.IsEnabled, useA160Protocol),
                        useA160Protocol);
                    if (options.IsEnabled)
                    {
                        WriteAmbientPacket(
                            stream,
                            HidPacketBuilder.BuildAmbientLight(options, useA160Protocol),
                            useA160Protocol);
                    }
                    return;
                }
                catch (Exception ex) when (IsTransportException(ex))
                {
                    InvalidateCurrentDeviceLocked();
                    if (attempt == 2)
                        Console.WriteLine($"[WARN]氛围灯 HID 写入失败：{ex.Message}");
                    else
                        Thread.Sleep(40);
                }
            }
        }
    }

    public void SetAmbientLightEnabled(bool enabled)
    {
        lock (HidWriteLock)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var device = ResolveCurrentDeviceLocked(forceProbe: attempt > 1);
                if (device is null)
                    return;

                try
                {
                    using var stream = OpenStream(device);
                    var useA160Protocol = device.ProductID == HaloPixelBarA160ProductId;
                    WriteAmbientPacket(
                        stream,
                        HidPacketBuilder.BuildAmbientLightPower(enabled, useA160Protocol),
                        useA160Protocol);
                    return;
                }
                catch (Exception ex) when (IsTransportException(ex))
                {
                    InvalidateCurrentDeviceLocked();
                    if (attempt == 2)
                        Console.WriteLine($"[WARN]氛围灯开关 HID 写入失败：{ex.Message}");
                    else
                        Thread.Sleep(40);
                }
            }
        }
    }

    private static void WriteAmbientPacket(HidStream stream, byte[] packet, bool useA160Protocol)
    {
        if (useA160Protocol)
        {
            stream.Write(packet);
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(30);
            stream.Write(packet);
        }
    }

    public void SetPixelScreenColor(HaloPixelColor color)
    {
        lock (HidWriteLock)
        {
            _pixelScreenColor = color;
            HidPacketBuilder.CurrentColor = (color.Red, color.Green, color.Blue);
        }

        // 本机 A160 固件和 TempoHub 均使用 EC EF operation 3 更新屏幕颜色。
        // 该命令只修改颜色，不会把歌词切回自定义文字模式。
        WritePacket(HidPacketBuilder.BuildPixelScreenColor(color));
    }

    private LyricTransitionAttempt EnsureLyricTransitionLocked(
        HidStream stream,
        LyricTransitionPreset preset,
        bool enabled,
        bool force)
    {
        ValidateLyricPreset(preset);

        if (_pixelStateQueryPending)
        {
            if (TryQueryCompatiblePixelScreenState(
                    stream,
                    out var queriedEnabled,
                    out var queriedPreset,
                    out var queriedColor,
                    out var queriedModeGroup))
            {
                _lyricControlProtocol = LyricControlProtocol.EcEf;
                _lyricTransitionEnabled = queriedEnabled;
                _lyricTransitionPreset = queriedPreset;
                _pixelScreenColor = queriedColor;
                _pixelModeGroup = queriedModeGroup;
                _lyricTransitionConfirmed = true;
                _lyricTransitionRetryCount = 0;
                _nextLyricTransitionRetryUtc = DateTime.MinValue;
                _lastLyricTransitionWriteUtc = DateTime.UtcNow;
                Console.WriteLine(
                    $"[INFO]A160 歌词控制通道：EC EE/EF；歌词={(queriedEnabled ? "开" : "关")}，动画={queriedPreset}");
            }
            else if (TryQueryEdPixelScreenState(
                         stream,
                         out queriedEnabled,
                         out queriedPreset))
            {
                _lyricControlProtocol = LyricControlProtocol.Ed01Ed02;
                _lyricTransitionEnabled = queriedEnabled;
                _lyricTransitionPreset = queriedPreset;
                _lyricTransitionConfirmed = true;
                _lyricTransitionRetryCount = 0;
                _nextLyricTransitionRetryUtc = DateTime.MinValue;
                _lastLyricTransitionWriteUtc = DateTime.UtcNow;
                Console.WriteLine(
                    $"[INFO]A160 歌词控制通道：ED 01/02；歌词={(queriedEnabled ? "开" : "关")}，动画={queriedPreset}");
            }
            _pixelStateQueryPending = false;
        }

        if (!ShouldWriteLyricTransitionLocked(preset, enabled, force))
            return new LyricTransitionAttempt(false, _lyricTransitionConfirmed);

        if (_lyricTransitionEnabled != enabled || _lyricTransitionPreset != preset)
            _lyricTransitionRetryCount = 0;

        var protocol = _lyricControlProtocol == LyricControlProtocol.Unknown
            ? LyricControlProtocol.EcEf
            : _lyricControlProtocol;
        var transitionPacket = protocol == LyricControlProtocol.EcEf
            ? HidPacketBuilder.BuildCompatibleLyricTransition(
                preset,
                enabled,
                _pixelScreenColor,
                _pixelModeGroup)
            : HidPacketBuilder.BuildLyricTransition(preset, enabled);
        var ackBuffer = new byte[64];
        stream.ReadTimeout = AckTimeoutMilliseconds;
        var pendingAckRead = stream.BeginRead(
            ackBuffer,
            0,
            ackBuffer.Length,
            callback: null,
            state: null);
        stream.Write(transitionPacket);
        _lastLyricTransitionWriteUtc = DateTime.UtcNow;
        var confirmed = TryReadLyricTransitionResponse(
            stream,
            pendingAckRead,
            ackBuffer,
            preset,
            enabled,
            protocol,
            _pixelModeGroup);

        _lyricTransitionEnabled = enabled;
        _lyricTransitionPreset = preset;
        _lyricTransitionConfirmed = confirmed;
        if (confirmed)
        {
            _lyricTransitionRetryCount = 0;
            _nextLyricTransitionRetryUtc = DateTime.MinValue;
            Console.WriteLine(
                $"[INFO]A160 歌词动画已由设备确认（{protocol}）：{(enabled ? $"启用 {preset}" : "停用")}");
        }
        else
        {
            _lyricTransitionRetryCount = Math.Min(_lyricTransitionRetryCount + 1, 6);
            var retryDelaySeconds = Math.Min(_lyricTransitionRetryCount, 5);
            _nextLyricTransitionRetryUtc = DateTime.UtcNow +
                                           TimeSpan.FromSeconds(retryDelaySeconds);
            Console.WriteLine(_lyricTransitionRetryCount < 6
                ? $"[WARN]A160 已接收歌词动画写入（{protocol}）但 180ms 内未确认；将在 {retryDelaySeconds}s 后重试。"
                : "[WARN]A160 歌词动画未返回确认；已保留写入结果，后续将通过设备变化或周期健康检查重新武装。");
        }

        return new LyricTransitionAttempt(true, confirmed);
    }

    private bool ShouldWriteLyricTransitionLocked(
        LyricTransitionPreset preset,
        bool enabled,
        bool force)
    {
        if (force || _lyricTransitionEnabled != enabled || _lyricTransitionPreset != preset)
            return true;
        if (enabled && DateTime.UtcNow - _lastLyricTransitionWriteUtc >= LyricTransitionHealthInterval)
            return true;
        if (_lyricTransitionConfirmed)
            return false;
        if (_lyricTransitionRetryCount >= 6)
            return false;
        return DateTime.UtcNow >= _nextLyricTransitionRetryUtc;
    }

    private static bool TryReadLyricTransitionResponse(
        HidStream stream,
        IAsyncResult pendingRead,
        byte[] buffer,
        LyricTransitionPreset preset,
        bool enabled,
        LyricControlProtocol protocol,
        byte modeGroup)
    {
        var deadline = Environment.TickCount64 + AckTimeoutMilliseconds;
        while (true)
        {
            try
            {
                var count = stream.EndRead(pendingRead);
                var report = buffer.AsSpan(0, count);
                var accepted = protocol == LyricControlProtocol.EcEf
                    ? HidPacketBuilder.IsCompatibleLyricTransitionResponse(
                        report,
                        preset,
                        enabled,
                        modeGroup)
                    : HidPacketBuilder.IsLyricTransitionResponse(
                        report,
                        preset,
                        enabled);
                if (accepted)
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
                return false;
            }

            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return false;
            stream.ReadTimeout = (int)Math.Max(1, remaining);
            pendingRead = stream.BeginRead(buffer, 0, buffer.Length, callback: null, state: null);
        }
    }

    private static bool TryQueryCompatiblePixelScreenState(
        HidStream stream,
        out bool enabled,
        out LyricTransitionPreset preset,
        out HaloPixelColor color,
        out byte modeGroup)
    {
        var buffer = new byte[64];
        stream.ReadTimeout = AckTimeoutMilliseconds;
        var pendingRead = stream.BeginRead(buffer, 0, buffer.Length, callback: null, state: null);
        stream.Write(HidPacketBuilder.BuildCompatiblePixelScreenStateQuery());
        return TryReadCompatiblePixelScreenState(
            stream,
            pendingRead,
            buffer,
            out enabled,
            out preset,
            out color,
            out modeGroup);
    }

    private static bool TryReadCompatiblePixelScreenState(
        HidStream stream,
        IAsyncResult pendingRead,
        byte[] buffer,
        out bool enabled,
        out LyricTransitionPreset preset,
        out HaloPixelColor color,
        out byte modeGroup)
    {
        enabled = false;
        preset = LyricTransitionPreset.Preset1;
        color = new HaloPixelColor(0xf0, 0xb4, 0xc8);
        modeGroup = 0x00;
        var deadline = Environment.TickCount64 + AckTimeoutMilliseconds;

        while (true)
        {
            int count;
            try
            {
                count = stream.EndRead(pendingRead);
            }
            catch (TimeoutException)
            {
                return false;
            }

            if (TryGetInputPayload(buffer.AsSpan(0, count), 0xecee, out var payload) &&
                payload.Length >= 12)
            {
                color = new HaloPixelColor(payload[1], payload[2], payload[3]);
                modeGroup = payload[4];
                enabled = payload[9] == 0x01;
                var rawPreset = payload[11];
                if (rawPreset <= 4)
                    preset = (LyricTransitionPreset)(rawPreset + 1);
                else if (enabled)
                    return false;
                return true;
            }

            if (!BeginNextInputRead(stream, buffer, deadline, out pendingRead))
                return false;
        }
    }

    private static bool TryQueryEdPixelScreenState(
        HidStream stream,
        out bool enabled,
        out LyricTransitionPreset preset)
    {
        var buffer = new byte[64];
        stream.ReadTimeout = AckTimeoutMilliseconds;
        var pendingRead = stream.BeginRead(buffer, 0, buffer.Length, callback: null, state: null);
        stream.Write(HidPacketBuilder.BuildPixelScreenStateQuery());
        return TryReadEdPixelScreenState(stream, pendingRead, buffer, out enabled, out preset);
    }

    private static bool WriteLyricFramesAndConfirmLocked(
        HidStream stream,
        IReadOnlyList<byte[]> frames)
    {
        var allConfirmed = true;
        foreach (var frame in frames)
        {
            var ackBuffer = new byte[64];
            stream.ReadTimeout = AckTimeoutMilliseconds;
            var pendingRead = stream.BeginRead(
                ackBuffer,
                0,
                ackBuffer.Length,
                callback: null,
                state: null);
            stream.Write(frame);

            var confirmed = TryReadLyricTextResponse(stream, pendingRead, ackBuffer);
            allConfirmed &= confirmed;
        }

        if (allConfirmed)
            Console.WriteLine("[DEBUG]A160 已确认歌词 E8 写入。");
        else
            Console.WriteLine("[WARN]A160 歌词 E8 已写入，但未在超时前收到成功确认。");
        return allConfirmed;
    }

    private static bool TryReadLyricTextResponse(
        HidStream stream,
        IAsyncResult pendingRead,
        byte[] buffer)
    {
        var deadline = Environment.TickCount64 + AckTimeoutMilliseconds;
        while (true)
        {
            try
            {
                var count = stream.EndRead(pendingRead);
                if (HidPacketBuilder.IsLyricTextResponse(buffer.AsSpan(0, count)))
                    return true;
            }
            catch (TimeoutException)
            {
                return false;
            }

            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return false;
            stream.ReadTimeout = (int)Math.Max(1, remaining);
            pendingRead = stream.BeginRead(buffer, 0, buffer.Length, callback: null, state: null);
        }
    }

    private static bool TryReadEdPixelScreenState(
        HidStream stream,
        IAsyncResult pendingRead,
        byte[] buffer,
        out bool enabled,
        out LyricTransitionPreset preset)
    {
        enabled = false;
        preset = LyricTransitionPreset.Preset1;
        var deadline = Environment.TickCount64 + AckTimeoutMilliseconds;
        byte? modeGroup = null;
        byte? totalParts = null;
        var chunks = new SortedDictionary<byte, byte[]>();

        while (true)
        {
            int count;
            try
            {
                count = stream.EndRead(pendingRead);
            }
            catch (TimeoutException)
            {
                return false;
            }

            if (!TryGetInputPayload(buffer.AsSpan(0, count), 0xed01, out var rawPayload) ||
                rawPayload.Length < 3 || rawPayload[1] == 0 || rawPayload[2] >= rawPayload[1])
            {
                if (!BeginNextInputRead(stream, buffer, deadline, out pendingRead))
                    return false;
                continue;
            }

            if (modeGroup.HasValue &&
                (modeGroup.Value != rawPayload[0] || totalParts != rawPayload[1]))
            {
                modeGroup = null;
                totalParts = null;
                chunks.Clear();
            }

            modeGroup ??= rawPayload[0];
            totalParts ??= rawPayload[1];
            chunks[rawPayload[2]] = rawPayload[3..].ToArray();
            if (chunks.Count != totalParts.Value)
            {
                if (!BeginNextInputRead(stream, buffer, deadline, out pendingRead))
                    return false;
                continue;
            }

            var logicalPayload = new List<byte> { modeGroup.Value };
            foreach (var chunk in chunks.OrderBy(static item => item.Key))
                logicalPayload.AddRange(chunk.Value);

            if (logicalPayload.Count < 5 ||
                logicalPayload[0] != 0x01 ||
                logicalPayload[3] > 0x01)
            {
                return false;
            }

            enabled = logicalPayload[3] == 0x01;
            var rawPreset = logicalPayload[4];
            if (rawPreset is >= (byte)LyricTransitionPreset.Preset1 and <= (byte)LyricTransitionPreset.Preset5)
                preset = (LyricTransitionPreset)rawPreset;
            else if (enabled)
                return false;
            return true;
        }
    }

    private static bool BeginNextInputRead(
        HidStream stream,
        byte[] buffer,
        long deadline,
        out IAsyncResult pendingRead)
    {
        var remaining = deadline - Environment.TickCount64;
        if (remaining <= 0)
        {
            pendingRead = null!;
            return false;
        }

        stream.ReadTimeout = (int)Math.Max(1, remaining);
        pendingRead = stream.BeginRead(buffer, 0, buffer.Length, callback: null, state: null);
        return true;
    }

    private static bool TryGetInputPayload(
        ReadOnlySpan<byte> report,
        ushort command,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (report.Length < 7 ||
            report[0] is not (0x2f or 0x55) ||
            report[1] is not (0xbb or 0xcc or 0xaa) ||
            report[2] != (byte)(command >> 8) ||
            report[3] != (byte)command)
        {
            return false;
        }

        var payloadLength = (report[4] << 8) | report[5];
        var checksumIndex = 6 + payloadLength;
        if (payloadLength > 57 || checksumIndex >= report.Length)
            return false;

        byte checksum = 0;
        for (var index = 1; index < checksumIndex; index++)
            checksum += report[index];
        if (report[checksumIndex] != checksum)
            return false;

        payload = report.Slice(6, payloadLength);
        return true;
    }

    private HidDevice? ResolveCurrentDeviceLocked(bool forceProbe = false)
    {
        var version = Volatile.Read(ref _deviceListVersion);
        if (!forceProbe && CurrentDevice is not null && _resolvedDeviceListVersion == version)
            return CurrentDevice;
        if (!forceProbe && CurrentDevice is not null && _resolvedDeviceListVersion != version)
        {
            var firstChangeAt = Volatile.Read(ref _firstPendingDeviceListChangeTimestamp);
            if (firstChangeAt != 0 && Stopwatch.GetElapsedTime(firstChangeAt) < DeviceListRecoveryDelay)
                return CurrentDevice;

            var lastRecoveryAt = Volatile.Read(ref _lastDeviceListRecoveryTimestamp);
            if (lastRecoveryAt != 0 && Stopwatch.GetElapsedTime(lastRecoveryAt) < DeviceListRecoveryCooldown)
                return CurrentDevice;
        }
        if (!forceProbe && CurrentDevice is null && _resolvedDeviceListVersion == version &&
            DateTime.UtcNow < _nextDeviceProbeUtc)
        {
            return null;
        }

        HidDevice? resolved;
        try
        {
            resolved = GetPixelDevice()
                .OrderByDescending(static device => device.ProductID == HaloPixelBarA160ProductId)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN]枚举花再 HID 命令通道失败：{ex.Message}");
            resolved = null;
        }

        var deviceListEpochChanged = _resolvedDeviceListVersion != version;
        var deviceChanged = !ReferenceEquals(CurrentDevice, resolved);
        CurrentDevice = resolved;
        _resolvedDeviceListVersion = version;
        _nextDeviceProbeUtc = resolved is null
            ? DateTime.UtcNow + DeviceProbeInterval
            : DateTime.MinValue;

        if (deviceChanged)
        {
            ResetLyricTransportLocked(replayLastLyric: resolved is not null);
            Console.WriteLine(resolved is null
                ? "[INFO]花再 HID 命令通道已断开。"
                : $"[INFO]花再 HID 命令通道已连接：PID 0x{resolved.ProductID:X4} / {resolved.DevicePath}");
        }
        else if (deviceListEpochChanged && resolved?.ProductID == HaloPixelBarA160ProductId)
        {
            // TempoHub 启停时本机 HidSharp 会复用同一个 HidDevice 对象，但固件歌词
            // 模式仍可能被重置。等待设备列表事件安静后，把状态降为未知并重放当前句。
            ResetLyricTransportLocked(replayLastLyric: true);
            Interlocked.Exchange(ref _lastDeviceListRecoveryTimestamp, Stopwatch.GetTimestamp());
            Console.WriteLine("[INFO]HID 设备列表已稳定，正在重新确认 A160 歌词模式。");
        }
        if (deviceListEpochChanged)
            Interlocked.Exchange(ref _firstPendingDeviceListChangeTimestamp, 0);

        return resolved;
    }

    private void InvalidateCurrentDeviceLocked()
    {
        CurrentDevice = null;
        _resolvedDeviceListVersion = long.MinValue;
        _nextDeviceProbeUtc = DateTime.MinValue;
        ResetLyricTransportLocked(replayLastLyric: true);
    }

    private void ResetLyricTransportLocked(bool replayLastLyric)
    {
        _pixelStateQueryPending = true;
        _lyricControlProtocol = LyricControlProtocol.Unknown;
        _pixelModeGroup = 0x00;
        _lyricTransitionEnabled = false;
        _lyricTransitionConfirmed = false;
        _lyricTransitionRetryCount = 0;
        _nextLyricTransitionRetryUtc = DateTime.MinValue;
        _lastLyricTransitionWriteUtc = DateTime.MinValue;
        _replayLastLyricAfterReconnect = replayLastLyric && !string.IsNullOrEmpty(_lastLyricText);
    }

    private static HidStream OpenStream(HidDevice device)
    {
        var stream = device.Open();
        stream.ReadTimeout = AckTimeoutMilliseconds;
        stream.WriteTimeout = HidWriteTimeoutMilliseconds;
        return stream;
    }

    private static void ValidateLyricPreset(LyricTransitionPreset preset)
    {
        if (preset is < LyricTransitionPreset.Preset1 or > LyricTransitionPreset.Preset5)
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Lyric transition preset must be between 1 and 5.");
    }

    private static bool IsTransportException(Exception ex)
        => ex is IOException or UnauthorizedAccessException or TimeoutException;

    private bool WritePacket(byte[] packet, bool invalidatesLyricMode = false)
        => WritePacket(_ => packet, invalidatesLyricMode);

    private bool WritePacket(
        Func<HidDevice, byte[]> packetFactory,
        bool invalidatesLyricMode = false)
    {
        ArgumentNullException.ThrowIfNull(packetFactory);
        lock (HidWriteLock)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var device = ResolveCurrentDeviceLocked(forceProbe: attempt > 1);
                if (device is null)
                    return false;

                try
                {
                    using var stream = OpenStream(device);
                    stream.Write(packetFactory(device));
                    if (invalidatesLyricMode && device.ProductID == HaloPixelBarA160ProductId)
                    {
                        _lyricTransitionEnabled = false;
                        _lyricTransitionConfirmed = false;
                        _nextLyricTransitionRetryUtc = DateTime.MinValue;
                    }
                    return true;
                }
                catch (Exception ex) when (IsTransportException(ex))
                {
                    InvalidateCurrentDeviceLocked();
                    if (attempt == 2)
                        Console.WriteLine($"[WARN]HID 写入失败：{ex.Message}");
                    else
                        Thread.Sleep(40);
                }
            }
        }

        return false;
    }

    private readonly record struct LyricTransitionAttempt(bool Written, bool Confirmed);

    private enum LyricControlProtocol
    {
        Unknown,
        EcEf,
        Ed01Ed02
    }
}
