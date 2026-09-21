using HaloPixelToolBox.Core.Models.Bar;
using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Client.Profiles.CrossVersionProfiles;

public partial class CloudMusicLyricsProfile : XFEProfile
{
    public CloudMusicLyricsProfile() => ProfilePath = $@"{AppPathHelper.LocalProfile}\{nameof(CloudMusicLyricsProfile)}";
    /// <summary>
    /// 切换回默认显示内容的时间
    /// </summary>
    [ProfileProperty]
    private int switchBackTimeout = 60;
    /// <summary>
    /// 是否启用网易云音乐歌词
    /// </summary>
    [ProfileProperty]
    private bool enableCloudMusicLyrics = false;
    /// <summary>
    /// 歌词显示协议
    /// </summary>
    [ProfileProperty]
    private LyricDisplayProtocol lyricDisplayProtocol = LyricDisplayProtocol.FirmwareLyric;
    /// <summary>
    /// PixelBar 固件歌词转场预设（协议值 1-5）
    /// </summary>
    [ProfileProperty]
    private LyricTransitionPreset lyricTransitionPreset = LyricTransitionPreset.Preset1;
    /// <summary>
    /// 当暂停时切换回默认显示内容
    /// </summary>
    [ProfileProperty]
    private bool switchBackWhenPause = true;
    /// <summary>
    /// 使用输入的内存地址
    /// </summary>
    [ProfileProperty]
    private bool useInputedAddress = false;
    /// <summary>
    /// 输入的内存地址
    /// </summary>
    [ProfileProperty]
    private string inputedAddress = string.Empty;
    /// <summary>
    /// 默认歌词显示布局
    /// </summary>
    [ProfileProperty]
    private HaloPixelTextLayout defaultHaloPixelTextLayout = HaloPixelTextLayout.Center;
    /// <summary>
    /// 默认暂停界面
    /// </summary>
    [ProfileProperty]
    private HaloPixelUIModel defaultHaloPixelUIModel = HaloPixelUIModel.Clock;

    /// <summary>
    /// 是否开启像素屏颜色同步
    /// </summary>
    [ProfileProperty]
    private bool enableScreenColorSync = false;

    /// <summary>
    /// 是否开启氛围灯颜色同步
    /// </summary>
    [ProfileProperty]
    private bool enableAmbientColorSync = false;

    /// <summary>
    /// 歌词同步时的氛围灯效果
    /// </summary>
    [ProfileProperty]
    private Core.Models.Lighting.AmbientLightEffect syncAmbientLightEffect = Core.Models.Lighting.AmbientLightEffect.Static;

    /// <summary>
    /// 默认/暂停时的氛围灯效果
    /// </summary>
    [ProfileProperty]
    private Core.Models.Lighting.AmbientLightEffect defaultAmbientLightEffect = Core.Models.Lighting.AmbientLightEffect.Breathing;

    /// <summary>
    /// 歌词同步时的氛围灯亮度
    /// </summary>
    [ProfileProperty]
    private Core.Models.Lighting.AmbientLightBrightness syncAmbientLightBrightness = Core.Models.Lighting.AmbientLightBrightness.High;

    /// <summary>
    /// 歌词同步时的氛围灯速度
    /// </summary>
    [ProfileProperty]
    private int syncAmbientLightSpeed = 5;

    /// <summary>
    /// 暂停时的氛围灯亮度
    /// </summary>
    [ProfileProperty]
    private Core.Models.Lighting.AmbientLightBrightness defaultAmbientLightBrightness = Core.Models.Lighting.AmbientLightBrightness.High;

    /// <summary>
    /// 暂停时的氛围灯速度
    /// </summary>
    [ProfileProperty]
    private int defaultAmbientLightSpeed = 5;

    /// <summary>
    /// 暂停时的氛围灯颜色 Hex
    /// </summary>
    [ProfileProperty]
    private string defaultAmbientLightColor = "#F0B4C8";

    /// <summary>
    /// 暂停时的像素屏颜色 Hex
    /// </summary>
    [ProfileProperty]
    private string defaultScreenColor = "#F0B4C8";
}
