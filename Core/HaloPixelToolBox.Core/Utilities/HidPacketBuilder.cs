using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.Display;
using HaloPixelToolBox.Core.Models.Lighting;
using System.Text;

namespace HaloPixelToolBox.Core.Utilities;

public class HidPacketBuilder
{
    private const int MaxTextBytesPerFrame = 55;
    private const int MaxLyricTextBytes = byte.MaxValue;

    /// <summary>
    /// 布局头
    /// </summary>
    public static readonly byte[] LayoutHeader =
    [
        0x2E, 0xAA, 0xEC, 0xEF, 0x00, 0x09, 0x01, 0xf0, 0xb4, 0xc8, 0x00, 0x02, 0x00
    ];

    /// <summary>
    /// 固定包长度（64 bytes）
    /// </summary>
    private const int FixedPacketLength = 64;

    /// <summary>
    /// 构造 HID 协议包
    /// </summary>
    public static byte[] BuildText(string text)
        => BuildTextPacket(text, 0x00);

    /// <summary>
    /// 构造歌词专用文本包。source 0x0E 会让 PixelBar 固件在文本替换时
    /// 执行当前选中的“旧歌词退出 + 新歌词进入”转场。
    /// </summary>
    public static byte[] BuildLyricText(string text)
    {
        var frames = BuildLyricTextFrames(text);
        if (frames.Count != 1)
            throw new ArgumentException("Lyric text needs multiple HID frames; use BuildLyricTextFrames instead.", nameof(text));

        return frames[0];
    }

    /// <summary>
    /// 构造一句歌词的所有 HID 分片。官方协议每片最多携带 55 个文本字节，
    /// 每片的歌词长度字段仍为完整 UTF-8 字节数，由固件按写入顺序重组。
    /// </summary>
    public static IReadOnlyList<byte[]> BuildLyricTextFrames(string text)
    {
        var textBytes = EncodeUtf8(text, MaxLyricTextBytes);
        var fullTextLength = (byte)textBytes.Length;
        if (textBytes.Length == 0)
            return [BuildLyricTextFrame([], fullTextLength)];

        var frames = new List<byte[]>((textBytes.Length + MaxTextBytesPerFrame - 1) / MaxTextBytesPerFrame);
        for (var offset = 0; offset < textBytes.Length; offset += MaxTextBytesPerFrame)
        {
            var chunkLength = Math.Min(MaxTextBytesPerFrame, textBytes.Length - offset);
            frames.Add(BuildLyricTextFrame(textBytes.AsSpan(offset, chunkLength), fullTextLength));
        }

        return frames;
    }

    private static byte[] BuildTextPacket(string text, byte source)
    {
        var textBytes = EncodeUtf8(text, MaxTextBytesPerFrame);
        var payload = new List<byte>(textBytes.Length + 2)
        {
            source,
            (byte)textBytes.Length
        };
        payload.AddRange(textBytes);
        return BuildFrame(0x2e, 0xece8, payload);
    }

    private static byte[] BuildLyricTextFrame(ReadOnlySpan<byte> textChunk, byte fullTextLength)
    {
        var payload = new List<byte>(textChunk.Length + 2)
        {
            0x0e,
            fullTextLength
        };
        payload.AddRange(textChunk.ToArray());
        return BuildFrame(0x2e, 0xece8, payload);
    }

    private static byte[] EncodeUtf8(string text, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        var buffer = new byte[maxBytes];
        var encoder = Encoding.UTF8.GetEncoder();
        encoder.Convert(text.AsSpan(), buffer.AsSpan(), false, out _, out var bytesUsed, out _);
        return buffer[..bytesUsed];
    }

    /// <summary>
    /// 选择并启用/停用 PixelBar 固件歌词动画。
    /// </summary>
    public static byte[] BuildLyricTransition(LyricTransitionPreset preset, bool enabled = true)
    {
        if (preset is < LyricTransitionPreset.Preset1 or > LyricTransitionPreset.Preset5)
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Lyric transition preset must be between 1 and 5.");

        return BuildFrame(
            0x2e,
            0xed02,
            [0x01, 0x03, 0x00, enabled ? (byte)0x01 : (byte)0x00, (byte)preset]);
    }

    /// <summary>
    /// 查询 A160 像素屏的完整状态。TempoHub 在打开歌词设置页时先发送该命令，
    /// 设备通过 input report 0x2F 返回 ED01 状态数据。
    /// </summary>
    public static byte[] BuildPixelScreenStateQuery()
        => BuildFrame(0x2e, 0xed01, Array.Empty<byte>());

    /// <summary>
    /// 查询兼容像素屏状态。部分 A160 固件沿用 EC EE/EF 屏幕控制通道，
    /// 但歌词文字仍使用 EC E8 source 0x0E。
    /// </summary>
    public static byte[] BuildCompatiblePixelScreenStateQuery()
        => BuildFrame(0x2e, 0xecee, Array.Empty<byte>());

    /// <summary>
    /// 构造 EC EF 歌词开关/动画选择。实机 A160 的 MUSIC mode 为 0，
    /// 动画索引沿用界面的 0–4（与 ED 02 的 1–5 不同）；operation 1
    /// 同时选择动画并启用歌词，operation 2 用于关闭歌词。
    /// </summary>
    public static byte[] BuildCompatibleLyricTransition(
        LyricTransitionPreset preset,
        bool enabled,
        HaloPixelColor color,
        byte modeGroup = 0x00)
    {
        if (preset is < LyricTransitionPreset.Preset1 or > LyricTransitionPreset.Preset5)
            throw new ArgumentOutOfRangeException(nameof(preset), preset, "Lyric transition preset must be between 1 and 5.");

        var compatiblePresetIndex = (byte)((byte)preset - 1);
        byte[] payload = enabled
            ? [0x01, color.Red, color.Green, color.Blue, modeGroup, 0x00, 0x01, 0x00, compatiblePresetIndex]
            : [0x02, color.Red, color.Green, color.Blue, modeGroup, 0x00, 0x00, 0xff, 0xff];
        return BuildFrame(0x2e, 0xecef, payload);
    }

    /// <summary>
    /// 验证 A160 对歌词开关命令的 ED02 回包。设备查询响应使用 0xBB，
    /// 设置响应使用 0xCC；部分旧固件会回显完整设置，较新的固件只回状态 0x01。
    /// </summary>
    public static bool IsLyricTransitionResponse(
        ReadOnlySpan<byte> report,
        LyricTransitionPreset preset,
        bool enabled)
    {
        if (preset is < LyricTransitionPreset.Preset1 or > LyricTransitionPreset.Preset5 ||
            report.Length < 8 ||
            report[0] is not (0x2f or 0x55) ||
            report[1] is not (0xbb or 0xcc or 0xaa) ||
            report[2] != 0xed ||
            report[3] != 0x02)
        {
            return false;
        }

        var payloadLength = (report[4] << 8) | report[5];
        var checksumIndex = 6 + payloadLength;
        if (payloadLength == 0 || checksumIndex >= report.Length)
            return false;

        byte checksum = 0;
        for (var index = 1; index < checksumIndex; index++)
            checksum += report[index];

        if (report[checksumIndex] != checksum)
            return false;

        if (payloadLength == 1)
            return report[1] == 0xcc && report[6] == 0x01;

        return payloadLength >= 5 &&
               report[6] == 0x01 &&
               report[7] == 0x03 &&
               report[9] == (enabled ? (byte)0x01 : (byte)0x00) &&
               report[10] == (byte)preset;
    }

    /// <summary>
    /// 验证歌词 E8 设置结果。A160 实机成功帧为：
    /// 2F CC EC E8 00 01 01 A2。
    /// </summary>
    public static bool IsLyricTextResponse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 8 ||
            report[0] is not (0x2f or 0x55) ||
            report[1] != 0xcc ||
            report[2] != 0xec ||
            report[3] != 0xe8)
        {
            return false;
        }

        var payloadLength = (report[4] << 8) | report[5];
        var checksumIndex = 6 + payloadLength;
        if (payloadLength != 1 || checksumIndex >= report.Length)
            return false;

        byte checksum = 0;
        for (var index = 1; index < checksumIndex; index++)
            checksum += report[index];

        return report[checksumIndex] == checksum && report[6] == 0x01;
    }

    /// <summary>
    /// 验证 EC EF 歌词设置回包。回包会返回固件实际采用的 9 字节设置，
    /// 因而也能识别固件拒绝了错误 mode 值的情况。
    /// </summary>
    public static bool IsCompatibleLyricTransitionResponse(
        ReadOnlySpan<byte> report,
        LyricTransitionPreset preset,
        bool enabled,
        byte modeGroup = 0x00)
    {
        if (!TryGetValidatedPayload(report, 0xecef, out var payload) || payload.Length != 9)
            return false;

        if (payload[4] != modeGroup || payload[5] != 0x00 || payload[6] != (enabled ? (byte)0x01 : (byte)0x00))
            return false;

        var compatiblePresetIndex = (byte)((byte)preset - 1);
        return enabled
            ? payload[0] == 0x01 && payload[7] == 0x00 && payload[8] == compatiblePresetIndex
            : payload[0] == 0x02;
    }

    private static bool TryGetValidatedPayload(
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

    /// <summary>
    /// A160/HZ 官方屏幕颜色命令。它只更新像素屏文字颜色，不会切换到
    /// EC EF 的自定义文字模式。
    /// </summary>
    public static byte[] BuildA160PixelScreenColor(HaloPixelColor color)
        => BuildFrame(0x2e, 0xed02, [0x01, 0x02, color.Red, color.Green, color.Blue]);

    /// <summary>
    /// 转换布局枚举到对应的 HID 包字节数组
    /// </summary>
    public static (byte R, byte G, byte B) CurrentColor { get; set; } = (0xf0, 0xb4, 0xc8);

    /// <summary>
    /// 转换布局枚举到对应的 HID 包字节数组
    /// </summary>
    /// <param name="haloPixelTextLayout"></param>
    /// <returns></returns>
    public static byte[] ConvertLayout(HaloPixelTextLayout haloPixelTextLayout)
    {
        byte[] payload = haloPixelTextLayout switch
        {
            HaloPixelTextLayout.Left => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x00, 0xff],
            HaloPixelTextLayout.Center => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x01, 0xff],
            HaloPixelTextLayout.Right => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x02, 0xff],
            HaloPixelTextLayout.Stretch => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x00, 0x03, 0xff],
            HaloPixelTextLayout.ScrollLeftToRight => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x01, 0x00, 0xff],
            HaloPixelTextLayout.ScrollRightToLeft => [0x01, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x02, 0x01, 0x01, 0xff],
            _ => [],
        };

        if (payload.Length == 0) return [];

        var list = new List<byte> { 0x2e, 0xaa, 0xec, 0xef, 0x00, 0x09 };
        list.AddRange(payload);

        int acc = 124;
        foreach (var b in payload)
        {
            acc += b + 2;
        }
        list.Add((byte)(acc % 256));
        list.Add(0x00);

        return Build(list);
    }

    public static byte[] ConvertUIModel(HaloPixelUIModel haloPixelUIModel)
    {
        byte[] payload = haloPixelUIModel switch
        {
            HaloPixelUIModel.Clock => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x00, 0xff, 0xff],
            HaloPixelUIModel.Game => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x01, 0xff, 0xff],
            HaloPixelUIModel.Work => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x02, 0xff, 0xff],
            HaloPixelUIModel.Read => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x03, 0xff, 0xff],
            HaloPixelUIModel.Cats => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x04, 0xff, 0xff],
            HaloPixelUIModel.Dogs => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x05, 0xff, 0xff],
            HaloPixelUIModel.Memes => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x06, 0xff, 0xff],
            HaloPixelUIModel.Cyber => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x07, 0xff, 0xff],
            HaloPixelUIModel.Waves => [0x02, CurrentColor.R, CurrentColor.G, CurrentColor.B, 0x00, 0x01, 0x08, 0xff, 0xff],
            _ => [],
        };

        if (payload.Length == 0) return [];

        var list = new List<byte> { 0x2e, 0xaa, 0xec, 0xef, 0x00, 0x09 };
        list.AddRange(payload);

        int acc = 124;
        foreach (var b in payload)
        {
            acc += b + 2;
        }
        list.Add((byte)(acc % 256));
        list.Add(0x00);

        return Build(list);
    }

    /// <summary>
    /// 构造 HID 协议包
    /// </summary>
    public static byte[] Build(byte[] bytes) => Build(bytes.ToList());

    /// <summary>
    /// 构造 HID 协议包
    /// </summary>
    public static byte[] Build(List<byte> bytes)
    {
        // Padding 补 0 到固定长度（64 字节）
        while (bytes.Count < FixedPacketLength)
            bytes.Add(0x00);

        return [.. bytes];
    }

    /// <summary>
    /// 校验算法
    /// </summary>
    public static int Checksum(byte[] textBytes) =>
        (128 + textBytes.Sum(static value => value + 2)) & 0xff;

    /// <summary>
    /// 把包转成 hex 字符串（小写，不带空格）
    /// </summary>
    public static string ToHex(byte[] packet)
    {
        return BitConverter.ToString(packet).Replace("-", "").ToLower();
    }

    /// <summary>
    /// 构造像素屏主题颜色包。
    /// 协议来自 LiLyric 的 RGBController.set_pixel_color：
    /// 2E AA EC EF 00 09 03 + RGB + 00 00 FF FF FF + checksum。
    /// </summary>
    public static byte[] BuildPixelScreenColor(HaloPixelColor color)
        => BuildEdifierPacket(
            0xef,
            [0x03, color.Red, color.Green, color.Blue, 0x00, 0x00, 0xff, 0xff, 0xff]);

    /// <summary>
    /// 构造氛围灯效果包。
    /// payload 为 light-array + effect + RGB + brightness + speed；
    /// PID A160 使用 0x20 灯组，早期设备沿用 0x13 灯组。
    /// </summary>
    public static byte[] BuildAmbientLight(AmbientLightOptions options, bool useHaloPixelBarA160Protocol = false)
    {
        var lightArrayIndex = useHaloPixelBarA160Protocol ? (byte)0x20 : (byte)0x13;
        var speed = useHaloPixelBarA160Protocol && options.Effect == AmbientLightEffect.Static
            ? (byte)0xff
            : Math.Clamp(options.Speed, (byte)1, (byte)10);
        byte[] payload =
        [
            lightArrayIndex,
            (byte)options.Effect,
            options.Color.Red,
            options.Color.Green,
            options.Color.Blue,
            ConvertAmbientBrightness(options.Brightness, useHaloPixelBarA160Protocol),
            speed
        ];

        return BuildEdifierPacket(0x6b, payload);
    }

    /// <summary>
    /// 构造氛围灯开关包。
    /// 协议来自 TempoHub 的 mood_lighting_splice/set_device_light：B 字段 1=开启、0=关闭。
    /// </summary>
    public static byte[] BuildAmbientLightPower(bool enabled, bool useHaloPixelBarA160Protocol = false)
    {
        byte mode = enabled ? (byte)0x01 : (byte)0x00;
        var lightArrayIndex = useHaloPixelBarA160Protocol ? (byte)0x20 : (byte)0x00;
        var lightSwitchIndex = useHaloPixelBarA160Protocol ? (byte)0x07 : (byte)0x00;
        return BuildEdifierPacket(0x6b, [lightArrayIndex, lightSwitchIndex, 0x00, 0x00, mode, 0xff, 0xff]);
    }

    private static byte ConvertAmbientBrightness(AmbientLightBrightness brightness, bool useHaloPixelBarA160Protocol)
    {
        if (useHaloPixelBarA160Protocol)
        {
            return brightness switch
            {
                AmbientLightBrightness.Low => 0x32,
                AmbientLightBrightness.Medium => 0x7d,
                AmbientLightBrightness.High => 0xfa,
                _ => 0xfa
            };
        }

        return brightness switch
        {
            AmbientLightBrightness.Low => 0x14,
            AmbientLightBrightness.Medium => 0x28,
            AmbientLightBrightness.High => 0x3c,
            _ => 0x3c
        };
    }

    /// <summary>
    /// 构造通用 Edifier HID 包：2E AA EC + 指令号 + payload 长度 + payload + 校验。
    /// </summary>
    public static byte[] BuildEdifierPacket(byte commandIndex, IReadOnlyCollection<byte> payload)
        => BuildFrame(0x2e, (ushort)(0xec00 | commandIndex), payload);

    /// <summary>
    /// 构造通用 64-byte Edifier HID 帧。长度为大端 payload 长度，
    /// CRC 为从 0xAA 到 payload 末尾的字节和。
    /// </summary>
    public static byte[] BuildFrame(byte deviceType, ushort command, IReadOnlyCollection<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Count > FixedPacketLength - 7)
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Count, "Payload is too large for a 64-byte HID frame.");

        var packet = new List<byte>(FixedPacketLength)
        {
            deviceType,
            0xaa,
            (byte)(command >> 8),
            (byte)(command & 0xff),
            (byte)((payload.Count >> 8) & 0xff),
            (byte)(payload.Count & 0xff)
        };
        packet.AddRange(payload);
        packet.Add(CalculateEdifierChecksum(packet));
        return Build(packet);
    }

    private static byte CalculateEdifierChecksum(IReadOnlyList<byte> packetWithoutChecksum)
    {
        var sum = 0;
        for (var index = 1; index < packetWithoutChecksum.Count; index++)
            sum += packetWithoutChecksum[index];

        return (byte)(sum & 0xff);
    }
}
