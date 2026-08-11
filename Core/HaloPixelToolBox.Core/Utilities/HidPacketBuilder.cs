using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Models.Display;
using HaloPixelToolBox.Core.Models.Lighting;
using System.Text;

namespace HaloPixelToolBox.Core.Utilities;

public class HidPacketBuilder
{
    /// <summary>
    /// 文本头
    /// </summary>
    public static readonly byte[] TextHeader =
    [
        0x2E, 0xAA, 0xEC, 0xE8, 0x00
    ];

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
    {
        if (text.Length > 50)
            text = text.Substring(0, 50);
        var textBytes = Encoding.UTF8.GetBytes(text);
        var textLen = (byte)textBytes.Length;
        // 有效载荷长度 = TextLen(1) + Text(N) + Checksum(1)
        var totalLen = (ushort)(1 + textLen + 1);

        var list = TextHeader.ToList();

        // TotalLen (2 bytes, little-endian)
        list.AddRange(BitConverter.GetBytes(totalLen));

        // TextLen (1 byte)
        list.Add(textLen);

        // Text bytes
        list.AddRange(textBytes);

        // Checksum (1 byte)
        list.Add(byte.Parse(Checksum(textBytes).ToString()));

        return Build(list);
    }

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
    public static int Checksum(byte[] textBytes)
    {
        var acc = 128;
        foreach (var ch in textBytes.Select(v => (char)v))
        {
            acc += ch + 2;
        }
        return acc % 256;
    }

    /// <summary>
    /// 把包转成 hex 字符串（小写，不带空格）
    /// </summary>
    public static string ToHex(byte[] packet)
    {
        return BitConverter.ToString(packet).Replace("-", "").ToLower();
    }

    /// <summary>
    /// 构造像素屏主题颜色包。
    /// 协议来自 LiLyric 的 RGBController.set_pixel_color：2E AA EC EF 00 04 03 + RGB + checksum。
    /// </summary>
    public static byte[] BuildPixelScreenColor(HaloPixelColor color)
        => BuildEdifierPacket(0xef, [0x03, color.Red, color.Green, color.Blue]);

    /// <summary>
    /// 构造氛围灯效果包。
    /// 协议来自 LiLyric 的 RGBController._set_light_color：2E AA EC 6B 00 07 13 + effect + RGB + brightness + speed + checksum。
    /// </summary>
    public static byte[] BuildAmbientLight(AmbientLightOptions options)
    {
        var speed = Math.Clamp(options.Speed, (byte)1, (byte)10);
        byte[] payload =
        [
            0x13,
            (byte)options.Effect,
            options.Color.Red,
            options.Color.Green,
            options.Color.Blue,
            ConvertAmbientBrightness(options.Brightness),
            speed
        ];

        return BuildEdifierPacket(0x6b, payload);
    }

    /// <summary>
    /// 构造氛围灯开关包。
    /// 协议来自 TempoHub 的 mood_lighting_splice/set_device_light：B 字段 1=开启、0=关闭。
    /// </summary>
    public static byte[] BuildAmbientLightPower(bool enabled)
    {
        byte mode = enabled ? (byte)0x01 : (byte)0x00;
        return BuildEdifierPacket(0x6b, [0x00, 0x00, 0x00, 0x00, mode, 0xff, 0xff]);
    }

    private static byte ConvertAmbientBrightness(AmbientLightBrightness brightness) => brightness switch
    {
        AmbientLightBrightness.Low => 0x14,
        AmbientLightBrightness.Medium => 0x28,
        AmbientLightBrightness.High => 0x3c,
        _ => 0x3c
    };

    /// <summary>
    /// 构造通用 Edifier HID 包：2E AA EC + 指令号 + payload 长度 + payload + 校验。
    /// </summary>
    public static byte[] BuildEdifierPacket(byte commandIndex, IReadOnlyCollection<byte> payload)
    {
        var packet = new List<byte>(6 + payload.Count + 1)
        {
            0x2e,
            0xaa,
            0xec,
            commandIndex,
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
