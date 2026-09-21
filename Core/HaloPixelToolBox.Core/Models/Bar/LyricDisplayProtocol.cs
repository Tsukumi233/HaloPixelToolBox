namespace HaloPixelToolBox.Core.Models.Bar;

/// <summary>
/// PixelBar 歌词显示路径。
/// </summary>
public enum LyricDisplayProtocol
{
    /// <summary>
    /// 使用固件歌词模式与 source 0x0E，支持固件转场动画。
    /// </summary>
    FirmwareLyric,

    /// <summary>
    /// 使用 SPACE 自定义文字模式与 source 0x00。
    /// </summary>
    CustomText
}
