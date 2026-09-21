using System.Runtime.Versioning;
using HaloPixelToolBox.Core.Models.Bar;
using HaloPixelToolBox.Core.Utilities;

namespace HaloPixelToolBox.Client.Test;

[SupportedOSPlatform("windows")]
internal class Program
{
    [SMTest]
    public static void TestMethod()
    {
        var device = new HaloPixelDevice();
        device.Initialize();
    }

    [SMTest]
    public static void LyricPacketsMatchOfficialGoldenFrames()
    {
        AssertFrame(
            HidPacketBuilder.BuildLyricTransition(LyricTransitionPreset.Preset1),
            "2EAAED0200050103000101A4");
        AssertFrame(
            HidPacketBuilder.BuildLyricTransition(LyricTransitionPreset.Preset5),
            "2EAAED0200050103000105A8");
        AssertFrame(
            HidPacketBuilder.BuildLyricTransition(LyricTransitionPreset.Preset1, false),
            "2EAAED0200050103000001A3");
        AssertFrame(
            HidPacketBuilder.BuildPixelScreenStateQuery(),
            "2EAAED01000098");
        AssertFrame(
            HidPacketBuilder.BuildCompatiblePixelScreenStateQuery(),
            "2EAAECEE000084");
        AssertFrame(
            HidPacketBuilder.BuildCompatibleLyricTransition(
                LyricTransitionPreset.Preset1,
                enabled: true,
                new HaloPixelToolBox.Core.Models.Display.HaloPixelColor(0xa7, 0x68, 0x55)),
            "2EAAECEF000901A768550000010000F4");
        AssertFrame(
            HidPacketBuilder.BuildCompatibleLyricTransition(
                LyricTransitionPreset.Preset5,
                enabled: true,
                new HaloPixelToolBox.Core.Models.Display.HaloPixelColor(0xa7, 0x68, 0x55)),
            "2EAAECEF000901A768550000010004F8");
        AssertFrame(
            HidPacketBuilder.BuildA160PixelScreenColor(
                new HaloPixelToolBox.Core.Models.Display.HaloPixelColor(0xf0, 0xb4, 0xc8)),
            "2EAAED0200050102F0B4C80D");
        AssertFrame(
            HidPacketBuilder.BuildLyricText("测"),
            "2EAAECE800050E03E6B58BBA");
        AssertFrame(
            HidPacketBuilder.BuildLyricText(string.Empty),
            "2EAAECE800020E008E");

        var customText = HidPacketBuilder.BuildText("测");
        Ensure(customText[2] == 0xec && customText[3] == 0xe8 &&
               customText[6] == 0x00 && customText[7] == 0x03,
            "Custom text must use EC E8 source 0x00 instead of lyric source 0x0E.");
        var customLayout = HidPacketBuilder.ConvertLayout(
            HaloPixelToolBox.Core.Models.Bar.HaloPixelTextLayout.Center);
        Ensure(customLayout[2] == 0xec && customLayout[3] == 0xef &&
               customLayout[10] == 0x00 && customLayout[11] == 0x02,
            "Custom text mode must select the EC EF SPACE layout before writing text.");
    }

    [SMTest]
    public static void LyricTransitionResponsesAreValidatedByFieldsAndChecksum()
    {
        var enabled = HidPacketBuilder.BuildLyricTransition(LyricTransitionPreset.Preset1);
        enabled[0] = 0x2f;
        enabled[1] = 0xcc;
        RecalculateChecksum(enabled);
        Ensure(
            HidPacketBuilder.IsLyricTransitionResponse(
                enabled,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "A valid ED02 input report must confirm the requested lyric transition.");

        var wrongPreset = (byte[])enabled.Clone();
        wrongPreset[10] = (byte)LyricTransitionPreset.Preset2;
        wrongPreset[11]++;
        Ensure(
            !HidPacketBuilder.IsLyricTransitionResponse(
                wrongPreset,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "An ACK for another preset must not confirm this request.");

        var badChecksum = (byte[])enabled.Clone();
        badChecksum[11] ^= 0xff;
        Ensure(
            !HidPacketBuilder.IsLyricTransitionResponse(
                badChecksum,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "A response with a bad checksum must be rejected.");

        var outputReport = (byte[])enabled.Clone();
        outputReport[0] = 0x2e;
        Ensure(
            !HidPacketBuilder.IsLyricTransitionResponse(
                outputReport,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "An output report must not be mistaken for a device ACK.");

        var compactAck = Convert.FromHexString("2FCCED02000101BD");
        Ensure(
            HidPacketBuilder.IsLyricTransitionResponse(
                compactAck,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "A compact 0xCC success ACK must be accepted.");

        var lyricAck = Convert.FromHexString("2FCCECE8000101A2");
        Ensure(
            HidPacketBuilder.IsLyricTextResponse(lyricAck),
            "The A160 E8 success ACK captured from TempoHub must be accepted.");

        lyricAck[^1] ^= 0xff;
        Ensure(
            !HidPacketBuilder.IsLyricTextResponse(lyricAck),
            "A corrupt E8 ACK must be rejected.");

        var compatibleAck = Convert.FromHexString("2FBBECEF000901A76855000001000005");
        Ensure(
            HidPacketBuilder.IsCompatibleLyricTransitionResponse(
                compatibleAck,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "The EC EF response captured from this A160 firmware must confirm lyric mode.");

        compatibleAck[11] = 0x03;
        RecalculateChecksum(compatibleAck);
        Ensure(
            !HidPacketBuilder.IsCompatibleLyricTransitionResponse(
                compatibleAck,
                LyricTransitionPreset.Preset1,
                enabled: true),
            "An EC EF response that rejected MUSIC mode 0 must not be accepted.");
    }

    private static void RecalculateChecksum(byte[] frame)
    {
        var payloadLength = (frame[4] << 8) | frame[5];
        var checksumIndex = 6 + payloadLength;
        byte checksum = 0;
        for (var index = 1; index < checksumIndex; index++)
            checksum += frame[index];
        frame[checksumIndex] = checksum;
    }

    [SMTest]
    public static void LyricPacketsRespectBoundariesAndFragmentation()
    {
        var frames = HidPacketBuilder.BuildLyricTextFrames(new string('A', 56));
        Ensure(frames.Count == 2, "A 56-byte lyric must use two frames.");
        Ensure(frames[0][4] == 0 && frames[0][5] == 57, "First frame payload length must be 57.");
        Ensure(frames[1][4] == 0 && frames[1][5] == 3, "Second frame payload length must be 3.");
        Ensure(frames.All(static frame => frame[6] == 0x0e && frame[7] == 56),
            "Every fragment must carry source 0x0E and the complete lyric byte length.");

        var exactSingleFrame = HidPacketBuilder.BuildLyricText(new string('A', 55));
        ValidateFrame(exactSingleFrame);
        Ensure(exactSingleFrame[5] == 57, "Exactly 55 text bytes must fit in one frame.");

        var splitCharacterFrames = HidPacketBuilder.BuildLyricTextFrames(new string('A', 54) + "测");
        Ensure(splitCharacterFrames.Count == 2 && splitCharacterFrames[0][62] == 0xe6 &&
               splitCharacterFrames[1][8] == 0xb5 && splitCharacterFrames[1][9] == 0x8b,
            "Official fragmentation must split the UTF-8 byte stream at 55 bytes.");
        var splitCharacterBytes = splitCharacterFrames
            .SelectMany(static frame => frame.Skip(8).Take(((frame[4] << 8) | frame[5]) - 2))
            .ToArray();
        Ensure(System.Text.Encoding.UTF8.GetString(splitCharacterBytes) == new string('A', 54) + "测",
            "A UTF-8 character crossing frame boundaries must reconstruct correctly.");

        var unicodeFrames = HidPacketBuilder.BuildLyricTextFrames(new string('A', 252) + "测");
        var reconstructed = unicodeFrames
            .SelectMany(static frame => frame.Skip(8).Take(((frame[4] << 8) | frame[5]) - 2))
            .ToArray();
        Ensure(reconstructed.Length == byte.MaxValue, "Long lyrics must be capped at 255 complete UTF-8 bytes.");
        Ensure(System.Text.Encoding.UTF8.GetString(reconstructed) == new string('A', 252) + "测",
            "UTF-8 truncation must not split the final character.");
        Ensure(unicodeFrames.All(static frame => frame[7] == byte.MaxValue),
            "Every long-lyric fragment must carry the complete 255-byte length.");

        var truncatedAtCharacterBoundary = HidPacketBuilder.BuildLyricTextFrames(new string('A', 254) + "测");
        var truncatedBytes = truncatedAtCharacterBoundary
            .SelectMany(static frame => frame.Skip(8).Take(((frame[4] << 8) | frame[5]) - 2))
            .ToArray();
        Ensure(truncatedBytes.Length == 254 && truncatedBytes.All(static value => value == (byte)'A'),
            "The 255-byte cap must not retain a partial UTF-8 character.");

        foreach (var frame in frames.Concat(unicodeFrames).Concat(truncatedAtCharacterBoundary).Concat(splitCharacterFrames))
            ValidateFrame(frame);
        Ensure(truncatedAtCharacterBoundary.All(static frame => frame[7] == 254),
            "Truncated fragments must carry their actual complete byte length.");

        ExpectArgumentOutOfRange(() => HidPacketBuilder.BuildLyricTransition((LyricTransitionPreset)0), "Preset 0");
        ExpectArgumentOutOfRange(() => HidPacketBuilder.BuildLyricTransition((LyricTransitionPreset)6), "Preset 6");

        var maximumFrame = HidPacketBuilder.BuildFrame(0x2e, 0xece8, new byte[57]);
        Ensure(maximumFrame.Length == 64, "A 57-byte payload must fit exactly in one HID frame.");
        ValidateFrame(maximumFrame);
        ExpectArgumentOutOfRange(() => HidPacketBuilder.BuildFrame(0x2e, 0xece8, new byte[58]), "A 58-byte payload");

        ExpectExactArgumentException(
            () => HidPacketBuilder.BuildLyricText(new string('A', 56)),
            "text",
            "The single-frame lyric API");
        ExpectExactArgumentException(
            () => HidPacketBuilder.BuildLyricText(new string('A', 54) + "测"),
            "text",
            "The single-frame lyric API with multi-byte text");
    }

    private static void AssertFrame(byte[] actual, string expectedPrefix)
    {
        var expected = Convert.FromHexString(expectedPrefix);
        Ensure(actual.Length == 64, "HID frames must be exactly 64 bytes.");
        Ensure(actual.AsSpan(0, expected.Length).SequenceEqual(expected),
            $"Frame mismatch. Expected {expectedPrefix}, got {Convert.ToHexString(actual.AsSpan(0, expected.Length))}.");
        ValidateFrame(actual);
    }

    private static void ValidateFrame(byte[] frame)
    {
        Ensure(frame.Length == 64, "HID frames must be exactly 64 bytes.");
        var payloadLength = (frame[4] << 8) | frame[5];
        var checksumIndex = 6 + payloadLength;
        Ensure(checksumIndex < frame.Length, "Checksum must fit in the HID frame.");

        byte checksum = 0;
        for (var index = 1; index < checksumIndex; index++)
            checksum += frame[index];
        Ensure(frame[checksumIndex] == checksum, "Frame checksum is invalid.");
        Ensure(frame.Skip(checksumIndex + 1).All(static value => value == 0),
            "Bytes after the checksum must be zero padded.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void ExpectArgumentOutOfRange(Action action, string caseName)
    {
        try
        {
            action();
            throw new InvalidOperationException($"{caseName} must be rejected.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static void ExpectExactArgumentException(Action action, string parameterName, string caseName)
    {
        try
        {
            action();
            throw new InvalidOperationException($"{caseName} must be rejected.");
        }
        catch (ArgumentException ex) when (ex.GetType() == typeof(ArgumentException) && ex.ParamName == parameterName)
        {
        }
    }
}
